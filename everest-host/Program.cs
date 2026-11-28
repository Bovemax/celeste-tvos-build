using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Foundation;
using UIKit;
using GameController;
using System.Collections.Generic;
using System.Linq;

namespace EverestGameHost;

internal static class Program
{
    private static readonly string Bundle = NSBundle.MainBundle.BundlePath;
    private static readonly string Payload = Bundle; // One root for FNA and assembly-relative content.
    private static IntPtr nativeHandle, fmodHandle;
    private static string startupError = "";
    private static string stage = "Host setup";
    private static UIWindow? diagnosticWindow;
    private static string? working;
    private static readonly Dictionary<string, string> payloadFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object assemblyLock = new();
    private static readonly SdlMain Callback = Run;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlMain(int argc, IntPtr argv);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_UIKitRunApp(int argc, IntPtr argv, SdlMain callback);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetPrefPath(
        IntPtr organization, [MarshalAs(UnmanagedType.LPUTF8Str)] string application);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_free(IntPtr memory);

    private static string Framework(string name) =>
        Path.Combine(Bundle, "Frameworks", name + ".framework", name);

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? search)
    {
        var key = name.ToLowerInvariant();
        foreach (var suffix in new[] { ".dll", ".dylib", ".so" })
            if (key.EndsWith(suffix, StringComparison.Ordinal)) { key = key[..^suffix.Length]; break; }
        switch (key)
        {
            case "sdl2": case "lib sdl2": case "libsdl2":
            case "fna3d": case "libfna3d":
            case "faudio": case "libfaudio":
            case "theorafile": case "libtheorafile":
                return nativeHandle;
            case "fmod": case "libfmod": case "fmodstudio": case "libfmodstudio":
                if (fmodHandle == IntPtr.Zero)
                    fmodHandle = NativeLibrary.Load(Framework("FMODLocal"));
                return fmodHandle;
            default:
                return IntPtr.Zero;
        }
    }

    public static void Main(string[] args)
    {
        try
        {
            nativeHandle = NativeLibrary.Load(Framework("CelesteNative"));
            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, Resolve);
            VerifyIdentity();
            Environment.SetEnvironmentVariable("FNA_GRAPHICS_ENABLE_HIGHDPI", "1");
            Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "Metal");
            SDL_SetHint("SDL_JOYSTICK_MFI", "1");
            // A Siri Remote is not a gameplay controller; keep Player One for a full gamepad.
            if (SDL_SetHint("SDL_TV_REMOTE_AS_JOYSTICK", "0") == 0)
                Console.Error.WriteLine("Could not disable Siri Remote joystick mapping");
            SDL_SetHint("SDL_ACCELEROMETER_AS_JOYSTICK", "0");
            SDL_SetHint("SDL_TOUCH_MOUSE_EVENTS", "0");
            SDL_SetHint("SDL_MOUSE_TOUCH_EVENTS", "0");
        }
        catch (Exception e)
        {
            startupError = e.ToString();
            Console.Error.WriteLine(startupError);
            UIApplication.Main(args, null, typeof(DiagnosticDelegate));
            return;
        }
        SDL_UIKitRunApp(0, IntPtr.Zero, Callback);
    }

    private const string TestId = "org.example.celestelocalhost.everesttest";
    private static void VerifyIdentity()
    {
        var id = NSBundle.MainBundle.BundleIdentifier;
        if (id == TestId) return;
        if (id != null && id.StartsWith(TestId + ".", StringComparison.Ordinal)
            && id.Length == TestId.Length + 11
            && id.AsSpan(TestId.Length + 1).ToArray().All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            return;
        throw new InvalidOperationException("Everest test requires its separate application identity.");
    }

    private static void PrepareWritableLocation()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home)) throw new IOException("Missing sandbox HOME");
        working = Path.Combine(home, "Library", "Caches", "CelesteEverestTest");
        Directory.CreateDirectory(working);
        foreach (var name in new[] { "Mods", "Saves", "Backups", "Temp" })
            Directory.CreateDirectory(Path.Combine(working, name));
        // Boot test only. Caches are purgeable; no migration or persistence claim.
        Environment.SetEnvironmentVariable("EVEREST_SAVEPATH", working);
        Environment.SetEnvironmentVariable("EVEREST_TVOS_WORKDIR", working);
        Environment.SetEnvironmentVariable("EVEREST_TMPDIR", Path.Combine(working, "Temp"));
        Environment.SetEnvironmentVariable("EVEREST_NO_ERRORLOG_ON_CRASH", "1");
        Directory.SetCurrentDirectory(working);
        if (Directory.EnumerateFileSystemEntries(Path.Combine(working, "Mods")).Any())
            throw new InvalidOperationException("First Everest boot requires an empty Mods directory.");
    }

    private static void VerifyPayload()
    {
        var manifest = Path.Combine(Payload, "everest-payload.tsv");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 32768)
            throw new IOException("Missing or oversized Everest payload manifest");
        var lines = File.ReadAllLines(manifest);
        if (lines.Length < 4 || lines.Length > 100 || lines[0] != "everest-tvos-boot-v1")
            throw new InvalidDataException("Unexpected Everest manifest");
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].Length != 64
                || !parts[0].All(Uri.IsHexDigit)
                || !parts[1].EndsWith(".dll", StringComparison.Ordinal)
                || parts[1].Length > 100 || !parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                throw new InvalidDataException("Invalid manifest entry");
            var name = Path.GetFileNameWithoutExtension(parts[1]);
            if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
                || name.Equals("EverestHost", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload may not replace the host or framework");
            var path = Path.Combine(Payload, parts[1]);
            if (!File.Exists(path) || !payloadFiles.TryAdd(name, path))
                throw new InvalidDataException("Missing or duplicate assembly: " + name);
            using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload hash mismatch: " + name);
            if (!string.Equals(AssemblyName.GetAssemblyName(path).Name, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload identity mismatch: " + name);
        }
        foreach (var name in new[] { "Celeste", "Celeste.Content", "FNA", "MMHOOK_Celeste" })
            if (!payloadFiles.ContainsKey(name)) throw new InvalidDataException("Missing " + name);
        if (AssemblyName.GetAssemblyName(payloadFiles["FNA"]).Version != new Version(24,1,0,0))
            throw new InvalidDataException("Everest requires patched FNA 24.1.0.0");
        if (!File.Exists(Path.Combine(Payload, "Content", "Monocle", "MonocleDefault.xnb")))
            throw new IOException("Missing local game Content");
    }

    private static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName requested)
    {
        if (requested.Name == null || !payloadFiles.TryGetValue(requested.Name, out var path)) return null;
        lock (assemblyLock)
        {
            var loaded = context.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded;
            var assembly = context.LoadFromAssemblyPath(path);
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
            return assembly;
        }
    }

    [ObjCRuntime.MonoPInvokeCallback(typeof(SdlMain))]
    private static int Run(int argc, IntPtr argv)
    {
        try
        {
            VerifyIdentity();
            stage = "Prepare isolated cache";
            PrepareWritableLocation();
            stage = "Validate local Everest payload";
            VerifyPayload();
            var context = AssemblyLoadContext.Default;
            if (context.Assemblies.Any(a => a.GetName().Name == "FNA"))
                throw new InvalidOperationException("Unexpected preloaded FNA");
            context.Resolving += ResolveAssembly;
            stage = "Load patched FNA";
            var fna = ResolveAssembly(context, new AssemblyName("FNA"))!;
            Console.WriteLine("Loaded " + fna.FullName);
            stage = "Load actual Everest assembly";
            var game = ResolveAssembly(context, new AssemblyName("Celeste"))!;
            var entry = game.EntryPoint;
            if (entry?.DeclaringType?.FullName != "Celeste.Mod.BOOT" || entry.Name != "Main"
                || !entry.IsStatic || entry.GetParameters().Length != 1
                || entry.GetParameters()[0].ParameterType != typeof(string[]))
                throw new MissingMethodException("Expected the actual Everest BOOT.Main entry point");
            stage = "Invoke Everest BOOT.Main";
            entry.Invoke(null, new object[] { new[] { "--disable-splash" } });
            ShowDiagnostic("Everest entry point returned. See boot log below.", null);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            ShowDiagnostic("Everest startup exception", e);
            return 1;
        }
    }

    private static string ReadBootLog()
    {
        if (working == null) return "";
        try
        {
            var path = Path.Combine(working, "log.txt");
            if (!File.Exists(path)) return "\nNo Everest log written.";
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Max(0, stream.Length - 4500), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return "\nEverest log tail:\n" + reader.ReadToEnd();
        }
        catch (Exception e) { return "\nLog unavailable: " + e.Message; }
    }

    private static void ShowDiagnostic(string summary, Exception? error, bool cleanExit = false)
    {
        // SDL's UIKit delegate continues the event loop after its main callback
        // returns. Keep a strong reference to a separate window, above SDL's
        // launch window, instead of trying to start UIApplication a second time.
        var detail = error?.ToString() ?? "";
        if (error is TargetInvocationException && error.InnerException != null)
            detail = error.InnerException.ToString();
        if (!cleanExit) detail += ReadBootLog();
        var message = cleanExit ? summary : summary + "\nStage: " + stage + "\n" + detail;
        Console.Error.WriteLine(message);
        try
        {
            diagnosticWindow = new UIWindow(UIScreen.MainScreen.Bounds);
            var controller = new UIViewController();
            controller.View!.BackgroundColor = UIColor.Black;
            var bounds = controller.View.Bounds;
            if (bounds.Width < 200 || bounds.Height < 200) bounds = UIScreen.MainScreen.Bounds;
            controller.View.AddSubview(new UILabel(new CoreGraphics.CGRect(
                70, 60, bounds.Width - 140, bounds.Height - 120))
            {
                Text = message.Length > 2200 ? message[..1100] + "\n...\n" + message[^1050..] : message,
                Lines = 0,
                Font = UIFont.SystemFontOfSize(25),
                AdjustsFontSizeToFitWidth = true,
                MinimumScaleFactor = 0.5f,
                TextAlignment = UITextAlignment.Left,
                TextColor = UIColor.White
            });
            diagnosticWindow.RootViewController = controller;
            diagnosticWindow.WindowLevel = UIWindowLevel.Alert;
            diagnosticWindow.MakeKeyAndVisible();
        }
        catch (Exception displayError) { Console.Error.WriteLine(displayError); }
    }

    [Register("DiagnosticDelegate")]
    public class DiagnosticDelegate : UIApplicationDelegate
    {
        public override UIWindow? Window { get; set; }
        public override bool FinishedLaunching(UIApplication app, NSDictionary? options)
        {
            Window = new UIWindow(UIScreen.MainScreen.Bounds);
            var vc = new UIViewController();
            vc.View.BackgroundColor = UIColor.Black;
            vc.View.AddSubview(new UILabel(vc.View.Bounds) {
                Text = "Everest host setup failed.\n" + startupError,
                Lines = 0, TextAlignment = UITextAlignment.Center,
                TextColor = UIColor.White
            });
            Window.RootViewController = vc;
            Window.MakeKeyAndVisible();
            return true;
        }
    }
}
