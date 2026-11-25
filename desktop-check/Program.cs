using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

// Uses the exact public FNA managed assembly supplied with the host build.
// No game files are uploaded. By default, do not execute game code or native calls.
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
    foreach (var name in new[] {"Celeste.dll", "Celeste.Content.dll"})
    {
        var assembly = context.LoadFromAssemblyPath(Path.Combine(managed, name));
        Log("Loaded " + assembly.FullName);
        foreach (var reference in assembly.GetReferencedAssemblies())
            Log("  Reference: " + reference.FullName);
        var types = assembly.GetTypes();
        Log("  Resolved types: " + types.Length);
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
    Log("PASS: managed assembly/type/signature preflight. No game code or native library executed.");
    return 0;
}
catch (ReflectionTypeLoadException ex)
{
    Log(ex.ToString());
    foreach (var loader in ex.LoaderExceptions) Log(loader?.ToString() ?? "");
    return 1;
}
catch (Exception ex) { Log(ex.ToString()); return 1; }
