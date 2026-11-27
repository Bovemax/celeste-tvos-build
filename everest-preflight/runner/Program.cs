using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
if (args.Length != 2) throw new ArgumentException("Original and patched fixture paths required");
Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Dynamic code supported: {RuntimeFeature.IsDynamicCodeSupported}; compiled: {RuntimeFeature.IsDynamicCodeCompiled}");
var original = new AssemblyLoadContext("original");
var patched = new AssemblyLoadContext("patched");
var fromStream = new AssemblyLoadContext("stream");
var a = original.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var b = patched.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
using var stream = File.OpenRead(args[1]);
var c = fromStream.LoadFromStream(stream);
Check(a, original, 7);
Check(b, patched, 42);
Check(c, fromStream, 42);
Check(a, original, 7);
Console.WriteLine("PASS: original unchanged; independent contexts; rewritten assembly path and stream load; reflection and delegates");
if (RuntimeFeature.IsDynamicCodeSupported)
{
    // Pure managed fixture only: no native detours or executable memory writes.
    var dm = new DynamicMethod("Constant", typeof(int), Type.EmptyTypes);
    var il = dm.GetILGenerator();
    il.Emit(OpCodes.Ldc_I4, 17);
    il.Emit(OpCodes.Ret);
    var f = (Func<int>)dm.CreateDelegate(typeof(Func<int>));
    if (f() != 17) throw new InvalidOperationException("DynamicMethod returned incorrect value");
    Console.WriteLine("PASS: simple DynamicMethod delegate (not a native hook)");
}
else Console.WriteLine("SKIP: runtime reports no dynamic-code support");
static void Check(Assembly a, AssemblyLoadContext expectedContext, int expected)
{
    if (AssemblyLoadContext.GetLoadContext(a) != expectedContext) throw new InvalidOperationException("Wrong assembly context");
    var m = a.GetType("EverestFixture.Sample", true)!.GetMethod("Value")!;
    var f = (Func<int>)m.CreateDelegate(typeof(Func<int>));
    if ((int)m.Invoke(null, null)! != expected || f() != expected) throw new InvalidOperationException("Unexpected fixture result");
}
