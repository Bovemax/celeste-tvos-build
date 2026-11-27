using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace EverestPreflight;
public static class ProbeChecks
{
    private sealed class ModContext : AssemblyLoadContext
    {
        private readonly Assembly dependency;
        public ModContext(Assembly dependency) : base("mock-mod") { this.dependency = dependency; }
        protected override Assembly? Load(AssemblyName name) =>
            name.Name == "EverestFixture" ? dependency : null;
    }

    public static string Run(string root)
    {
        var results = new List<string> {
            "Everest interpreter preflight",
            RuntimeInformation.FrameworkDescription + " / " + RuntimeInformation.ProcessArchitecture,
            $"Dynamic code: supported={RuntimeFeature.IsDynamicCodeSupported}, compiled={RuntimeFeature.IsDynamicCodeCompiled}"
        };
        void Test(string name, Action action)
        {
            try { action(); results.Add("PASS: " + name); }
            catch (Exception e)
            {
                var cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                results.Add("FAIL: " + name + " (" + cause.GetType().Name + ": " + cause.Message + ")");
            }
        }
        var originalPath = Path.Combine(root, "original", "EverestFixture.dll");
        var patchedPath = Path.Combine(root, "patched", "EverestFixture.dll");
        var modulePath = Path.Combine(root, "module", "EverestMockMod.dll");
        Assembly? patched = null;
        Test("original and rewritten path load", () => {
            var a = new AssemblyLoadContext("original");
            var b = new AssemblyLoadContext("patched");
            var before = a.LoadFromAssemblyPath(originalPath);
            patched = b.LoadFromAssemblyPath(patchedPath);
            Check(before, a, 7);
            Check(patched, b, 42);
            Check(before, a, 7);
        });
        Test("rewritten stream load and delegates", () => {
            var context = new AssemblyLoadContext("stream");
            using var stream = File.OpenRead(patchedPath);
            Check(context.LoadFromStream(stream), context, 42);
        });
        Test("mock mod dependency and typed hooks", () => {
            if (patched == null) throw new InvalidOperationException("Patched assembly unavailable");
            var context = new ModContext(patched);
            using var stream = File.OpenRead(modulePath);
            var mod = context.LoadFromStream(stream);
            if (AssemblyLoadContext.GetLoadContext(mod) != context) throw new InvalidOperationException("Wrong mod context");
            var entry = mod.GetType("EverestMockMod.Entry", true)!;
            var dependency = entry.GetMethod("DependencyValue")!;
            if ((int)dependency.Invoke(null, null)! != 42) throw new InvalidOperationException("Wrong dependency");
            var target = patched.GetType("EverestFixture.HookTarget", true)!.GetMethod("Calculate")!;
            var calc = (Func<int,int>)target.CreateDelegate(typeof(Func<int,int>));
            if (calc(3) != 4) throw new InvalidOperationException("Original result");
            entry.GetMethod("Load")!.Invoke(null, null);
            // Last registered hook is outermost: ((3+3)*2+1+10)*2 = 46.
            if (calc(3) != 46) throw new InvalidOperationException("Hook chain ordering");
            entry.GetMethod("Unload")!.Invoke(null, null);
            if (calc(3) != 4) throw new InvalidOperationException("Hook cleanup");
        });
        if (RuntimeFeature.IsDynamicCodeSupported)
            Test("simple DynamicMethod delegate", TestDynamicMethod);
        else results.Add("SKIP: DynamicMethod not advertised");
        return string.Join("\n", results);
    }

    private static void Check(Assembly a, AssemblyLoadContext context, int expected)
    {
        if (AssemblyLoadContext.GetLoadContext(a) != context) throw new InvalidOperationException("Wrong assembly context");
        var m = a.GetType("EverestFixture.Sample", true)!.GetMethod("Value")!;
        var f = (Func<int>)m.CreateDelegate(typeof(Func<int>));
        if ((int)m.Invoke(null, null)! != expected || f() != expected) throw new InvalidOperationException("Unexpected fixture value");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TestDynamicMethod()
    {
        var dm = new DynamicMethod("Constant", typeof(int), Type.EmptyTypes);
        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 17);
        il.Emit(OpCodes.Ret);
        if (((Func<int>)dm.CreateDelegate(typeof(Func<int>)))() != 17)
            throw new InvalidOperationException("Unexpected dynamic method result");
    }
}
