using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

// Uses the exact public FNA managed assembly supplied with the host build.
// No game files are uploaded. By default, do not execute game code or native calls.
if (args.Length == 3 && args[0] == "--verify-fna")
{
    try { VerifyFna(args[1], args[2]); return 0; }
    catch (Exception error) { Console.Error.WriteLine(error); return 1; }
}
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: DesktopCheck <patched directory> <FNA.dll> <Content directory> <report file>");
    return 2;
}
using var report = new StreamWriter(Path.GetFullPath(args[3]));
void Log(string text) { Console.WriteLine(text); report.WriteLine(text); report.Flush(); }
try
{
    var managed = Path.GetFullPath(args[0]);
    var fnaPath = Path.GetFullPath(args[1]);
    var content = Path.GetFullPath(args[2]);
    if (!Directory.Exists(content) || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
        throw new DirectoryNotFoundException("Content missing or empty");
    Log("Desktop CLR: " + RuntimeInformation.FrameworkDescription);
    Log("Content: " + Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Count() + " files");
    Log("Process architecture: " + RuntimeInformation.ProcessArchitecture);
    foreach (var name in new[] {"Celeste.dll", "Celeste.Content.dll"})
    {
        using var stream = File.OpenRead(Path.Combine(managed, name));
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.CorHeader;
        if (header == null || !pe.HasMetadata)
            throw new BadImageFormatException(name + ": not a managed PE assembly");
        Log(name + ": machine=" + pe.PEHeaders.CoffHeader.Machine +
            ", CLR flags=" + header.Flags + " (0x" + ((uint)header.Flags).ToString("X8") + ")");
        if ((header.Flags & CorFlags.ILOnly) == 0 ||
            (header.Flags & CorFlags.Requires32Bit) != 0 ||
            (header.Flags & CorFlags.Prefers32Bit) != 0 ||
            pe.PEHeaders.CoffHeader.Machine != Machine.I386)
            Log("  WARNING: not an architecture-neutral AnyCPU IL assembly; check portability to tvOS ARM64.");
    }
    var context = AssemblyLoadContext.Default;
    var fna = context.LoadFromAssemblyPath(fnaPath);
    Log("Pinned FNA: " + fna.FullName);
    context.Resolving += (ctx, name) =>
    {
        if (name.Name == "FNA") return fna;
        if (name.Name == "Celeste.Content")
            return ctx.LoadFromAssemblyPath(Path.Combine(managed, "Celeste.Content.dll"));
        return null;
    };
    int unresolved = 0;
    foreach (var name in new[] {"Celeste.dll", "Celeste.Content.dll"})
    {
        var assembly = context.LoadFromAssemblyPath(Path.Combine(managed, name));
        Log("Loaded " + assembly.FullName);
        foreach (var reference in assembly.GetReferencedAssemblies())
            Log("  Reference: " + reference.FullName);
        var types = assembly.GetTypes();
        Log("  Resolved types: " + types.Length);
        unresolved += CheckFnaReferences(assembly, Path.Combine(managed, name), Log);
        var imports = new SortedSet<string>();
        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                _ = method.ReturnType;
                foreach (var parameter in method.GetParameters()) _ = parameter.ParameterType;
                var import = method.GetCustomAttribute<DllImportAttribute>();
                if (import != null) imports.Add(import.Value);
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                _ = field.FieldType;
        }
        foreach (var import in imports) Log("  Native library name: " + import);
        if (name == "Celeste.dll")
        {
            var entry = assembly.EntryPoint ?? assembly.GetType("Celeste.Celeste", true)!
                .GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (entry == null || !entry.IsStatic) throw new MissingMethodException("No static entry point");
            Log("  Entry point: " + entry);
        }
    }
    if (unresolved != 0) throw new MissingMemberException(unresolved + " unresolved FNA member references");
    Log("PASS: managed assembly/type/signature preflight and resolvable FNA member references. No game code or native library executed.");
    return 0;
}
catch (ReflectionTypeLoadException ex)
{
    Log(ex.ToString());
    foreach (var loader in ex.LoaderExceptions) Log(loader?.ToString() ?? "");
    return 1;
}
catch (Exception ex) { Log(ex.ToString()); return 1; }

static int CheckFnaReferences(Assembly assembly, string path, Action<string> log)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    bool IsFna(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
            return IsFna(md.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope);
        return handle.Kind == HandleKind.AssemblyReference &&
            md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)handle).Name) == "FNA";
    }
    int passed = 0, failed = 0, inconclusive = 0;
    foreach (var handle in md.MemberReferences)
    {
        var reference = md.GetMemberReference(handle);
        if (!IsFna(reference.Parent)) continue;
        var parent = md.GetTypeReference((TypeReferenceHandle)reference.Parent);
        string label = md.GetString(parent.Namespace) + "." + md.GetString(parent.Name) + "::" + md.GetString(reference.Name);
        try
        {
            _ = assembly.ManifestModule.ResolveMember(MetadataTokens.GetToken(handle))
                ?? throw new MissingMemberException(label);
            passed++;
        }
        catch (ArgumentException ex)
        {
            // Generic context-dependent references may require actual caller type arguments.
            log("  INCONCLUSIVE: " + label + " (" + ex.Message + ")");
            inconclusive++;
        }
        catch (Exception ex)
        {
            log("  MISSING: " + label + " (" + ex.GetType().Name + ": " + ex.Message + ")");
            failed++;
        }
    }
    log($"  Direct FNA member refs: {passed} resolved, {failed} failed, {inconclusive} inconclusive (generic TypeSpec parents not covered)");
    return failed;
}

static void VerifyFna(string original, string published)
{
    static Dictionary<string,int> Inventory(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var counts = new Dictionary<string,int>();
        void Add(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
        string TypeName(TypeDefinitionHandle handle)
        {
            var type = md.GetTypeDefinition(handle);
            var parent = type.GetDeclaringType();
            return parent.IsNil ? md.GetString(type.Namespace) + "." + md.GetString(type.Name)
                : TypeName(parent) + "+" + md.GetString(type.Name);
        }
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            string name = TypeName(handle);
            Add("type " + name);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                Add("method " + name + "::" + md.GetString(method.Name));
            }
            foreach (var fieldHandle in type.GetFields())
                Add("field " + name + "::" + md.GetString(md.GetFieldDefinition(fieldHandle).Name));
        }
        return counts;
    }
    var expected = Inventory(original);
    var actual = Inventory(published);
    var missing = expected.Where(pair => actual.GetValueOrDefault(pair.Key) < pair.Value).ToArray();
    foreach (var pair in missing) Console.Error.WriteLine("Removed: " + pair.Key);
    const string setter = "method Microsoft.Xna.Framework.GameWindow::set_Title";
    if (!actual.ContainsKey(setter) || missing.Length != 0)
        throw new InvalidOperationException($"Published FNA lost {missing.Length} member/type groups or lacks GameWindow.Title setter");
    Console.WriteLine($"Verified packaged FNA: {expected.Values.Sum()} original type/method/field definitions preserved by name/count, including GameWindow.set_Title");
}
