using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using Foundation;

namespace LocalGameHost;

internal static class FmodProbe
{
    [DllImport("fmodstudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FMOD_Studio_System_Create(out IntPtr system, uint headerVersion);
    [DllImport("fmodstudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FMOD_Studio_System_Initialize(IntPtr system, int maxChannels, uint studioFlags, uint coreFlags, IntPtr extra);
    [DllImport("fmodstudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FMOD_Studio_System_GetCoreSystem(IntPtr system, out IntPtr core);
    [DllImport("fmod", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FMOD_System_GetVersion(IntPtr system, out uint version);
    [DllImport("fmodstudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FMOD_Studio_System_Update(IntPtr system);
    [DllImport("fmodstudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int FMOD_Studio_System_Release(IntPtr system);

    private static void Check(int result, string operation)
    {
        if (result != 0) throw new InvalidOperationException(operation + ": FMOD result " + result);
    }

    // 0 absent, 1 exports only, 2 initialized and released, -1 failed.
    internal static int Run()
    {
        var bundle = NSBundle.MainBundle.BundlePath;
        var library = Path.Combine(bundle, "Frameworks", "FMODLocal.framework", "FMODLocal");
        if (!File.Exists(library)) return 0;
        IntPtr system = IntPtr.Zero;
        try
        {
            var handle = NativeLibrary.Load(library);
            try
            {
                foreach (var symbol in new[] { "FMOD_Studio_System_Create", "FMOD_Studio_System_Initialize",
                    "FMOD_Studio_System_GetCoreSystem", "FMOD_System_GetVersion",
                    "FMOD_Studio_System_Update", "FMOD_Studio_System_Release" })
                    NativeLibrary.GetExport(handle, symbol);
            }
            finally { NativeLibrary.Free(handle); }
            var versionPath = Path.Combine(bundle, "fmod-version.txt");
            if (!File.Exists(versionPath)) return 1;
            uint version = uint.Parse(File.ReadAllText(versionPath).Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Check(FMOD_Studio_System_Create(out system, version), "Studio create");
            Check(FMOD_Studio_System_GetCoreSystem(system, out var core), "Get core");
            Check(FMOD_System_GetVersion(core, out var actual), "Get version");
            Console.WriteLine("FMOD runtime version: " + actual.ToString("X8"));
            if (actual != version) throw new InvalidOperationException("FMOD header/runtime version mismatch");
            Check(FMOD_Studio_System_Initialize(system, 32, 0, 0, IntPtr.Zero), "Studio initialize");
            Check(FMOD_Studio_System_Update(system), "Studio update");
            var release = FMOD_Studio_System_Release(system);
            system = IntPtr.Zero;
            Check(release, "Studio release");
            return 2;
        }
        catch (Exception e) { Console.Error.WriteLine("FMOD probe: " + e); return -1; }
        finally { if (system != IntPtr.Zero) FMOD_Studio_System_Release(system); }
    }
}
