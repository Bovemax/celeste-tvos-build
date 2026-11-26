using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Foundation;
using UIKit;
using GameController;
using Microsoft.Xna.Framework;

namespace LocalGameHost;

internal static class Program
{
    private static readonly string Bundle = NSBundle.MainBundle.BundlePath;
    private static readonly string Payload = Bundle; // One root for FNA and assembly-relative content.
    private static IntPtr nativeHandle, fmodHandle;
    private static string startupError = "";
    private static string stage = "Host setup";
    private static UIWindow? diagnosticWindow;
    private static string? gameLogPath, initialLogDigest;
    private static bool logBaselineKnown;
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
            NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, Resolve);
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

    private static void PrepareWritableLocation()
    {
        var cache = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // tvOS has no durable general-purpose writable Documents directory.
        // Use the platform caches directory for transient files only.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
            cache = Path.Combine(home, "Library", "Caches");
        if (string.IsNullOrEmpty(cache))
            throw new IOException("No writable cache directory");
        var working = Path.Combine(cache, "CelesteLocal");
        Directory.CreateDirectory(working);
        Directory.SetCurrentDirectory(working);
        var marker = Path.Combine(working, "host-probe.txt");
        File.WriteAllText(marker, "write probe");
        Console.WriteLine("Cache write/read: " + (File.ReadAllText(marker) == "write probe"));
        var defaults = NSUserDefaults.StandardUserDefaults;
        var prior = defaults.IntForKey("LocalHostLaunchCount");
        defaults.SetInt(prior + 1, "LocalHostLaunchCount");
        Console.WriteLine("Previous host launches in preferences: " + prior);
    }

    [ObjCRuntime.MonoPInvokeCallback(typeof(SdlMain))]
    private static int Run(int argc, IntPtr argv)
    {
        try
        {
            stage = "Isolated save preferences test";
            var installedId = NSBundle.MainBundle.BundleIdentifier;
            if (!RuntimeTestIdentity.IsAllowed(installedId))
                throw new InvalidOperationException("Test application identity rejected: " + (installedId ?? "(missing)"));
            Console.WriteLine("Isolated test identity: " + installedId);
            ShowMessage(SavePreferencesProbe.Run());
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            ShowDiagnostic("Startup exception", e);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunProbe()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            name.Name == "FNA" ? typeof(Game).Assembly : null;
        using var game = new FnaProbe();
        game.Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunGame()
    {
        stage = "Verify packaged game files";
        if (!File.Exists(Path.Combine(Payload, "Celeste.Content.dll")))
            throw new FileNotFoundException("Missing Celeste.Content.dll");
        var content = Path.Combine(Payload, "Content");
        if (!File.Exists(Path.Combine(content, "Monocle", "MonocleDefault.xnb")))
            throw new FileNotFoundException("Missing startup XNB at bundle root");
        if (!Directory.Exists(Path.Combine(content, "Tutorials")))
            throw new DirectoryNotFoundException("Missing Tutorials at bundle root");
        // Explicit load context preserves assembly Location for the game's
        // assembly-relative asset lookup. No downloading or remote code loading.
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == "FNA") return typeof(Game).Assembly;
            if (name.Name != "Celeste.Content") return null;
            var a = context.LoadFromAssemblyPath(Path.Combine(Payload, "Celeste.Content.dll"));
            NativeLibrary.SetDllImportResolver(a, Resolve);
            return a;
        };
        stage = "Load and invoke Celeste";
        InvokePayload("Celeste.dll", true);
    }

    internal static object? InvokePayload(string file, bool game = false)
    {
        if (game) stage = "Load Celeste.dll";
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Payload, file));
        NativeLibrary.SetDllImportResolver(assembly, Resolve);
        if (game) stage = "Find game entry point";
        var entry = assembly.EntryPoint;
        if (entry == null && game)
            entry = assembly.GetType("Celeste.Celeste", true)!
                .GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (entry == null || !entry.IsStatic)
            throw new MissingMethodException("No static game entry point");
        if (game) stage = "Invoke " + entry.DeclaringType?.FullName + "." + entry.Name;
        var parameters = entry.GetParameters();
        if (parameters.Length == 0)
            return entry.Invoke(null, null);
        else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            return entry.Invoke(null, new object[] { Array.Empty<string>() });
        else
            throw new NotSupportedException("Unexpected game entry point signature");
    }

    // Match the game's tvOS log location through the same SDL API, without
    // moving, deleting, or rewriting its existing logs or saves.
    private static void CaptureGameLogBaseline()
    {
        try
        {
            var ptr = SDL_GetPrefPath(IntPtr.Zero, "Celeste");
            if (ptr == IntPtr.Zero) throw new IOException("SDL_GetPrefPath returned null");
            string? directory;
            try { directory = Marshal.PtrToStringUTF8(ptr); }
            finally { SDL_free(ptr); }
            if (string.IsNullOrEmpty(directory)) throw new IOException("SDL preference path is empty");
            gameLogPath = Path.Combine(directory, "errorLog.txt");
            initialLogDigest = LogDigest(gameLogPath);
            logBaselineKnown = true;
        }
        catch (Exception e) { Console.Error.WriteLine("Log baseline unavailable: " + e.Message); }
    }

    private static string? LogDigest(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ReadGameLogDiagnostic()
    {
        if (gameLogPath == null) return "\nGame log location unavailable.";
        try
        {
            var digest = LogDigest(gameLogPath);
            if (digest == null) return "\nNo game error log found at SDL preference path.";
            if (logBaselineKnown && digest == initialLogDigest)
                return "\nGame error log unchanged since launch (not a new error).";
            using var reader = new StreamReader(File.Open(gameLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var buffer = new char[3500];
            int count = reader.ReadBlock(buffer, 0, buffer.Length);
            return (logBaselineKnown ? "\nNew game error log:\n" : "\nGame error log (freshness unknown):\n")
                + new string(buffer, 0, count);
        }
        catch (Exception e) { return "\nCould not read game error log: " + e.Message; }
    }

    // Deliberately never starts the game, creates sessions, or invokes save APIs.
    private static string RunRuntimePreflight()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == "FNA") return typeof(Game).Assembly;
            if (name.Name != "Celeste.Content") return null;
            var a = context.LoadFromAssemblyPath(Path.Combine(Payload, "Celeste.Content.dll"));
            NativeLibrary.SetDllImportResolver(a, Resolve);
            return a;
        };
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Payload, "Celeste.dll"));
        NativeLibrary.SetDllImportResolver(game, Resolve);
        var results = new List<string> { "Celeste isolated runtime preflight" };
        try
        {
            var tile = game.GetType("Celeste.AnimatedTiles+Tile", true)!;
            var listType = typeof(List<>).MakeGenericType(tile);
            var value = Activator.CreateInstance(listType)!;
            var mapType = game.GetType("Monocle.VirtualMap`1", true)!.MakeGenericType(listType);
            var map = Activator.CreateInstance(mapType, new object?[] { 100, 100, null })!;
            var indexer = mapType.GetProperty("Item")!;
            foreach (var point in new[] { new[] { 0, 0 }, new[] { 51, 51 } })
            {
                var coords = new object[] { point[0], point[1] };
                indexer.SetValue(map, value, coords);
                if (!ReferenceEquals(value, indexer.GetValue(map, coords)))
                    throw new InvalidOperationException("VirtualMap read-back mismatch");
            }
            results.Add("PASS: actual AnimatedTiles VirtualMap setter/getter");
        }
        catch (Exception e)
        {
            var actual = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
            results.Add("FAIL: AnimatedTiles VirtualMap: " + actual.GetType().Name + ": " + actual.Message);
            Console.Error.WriteLine(e);
        }
        var decoder = game.GetType("Celeste.BinaryPacker", true)!.GetMethod("FromBinary",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null)
            ?? throw new MissingMethodException("BinaryPacker.FromBinary(string)");
        var maps = Directory.GetFiles(Path.Combine(Payload, "Content", "Maps"), "*.bin", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (maps.Length == 0) throw new IOException("No maps found");
        int passed = 0;
        var failures = new List<string>();
        foreach (var file in maps)
        {
            try
            {
                if (decoder.Invoke(null, new object[] { file }) == null)
                    throw new InvalidDataException("Decoder returned null");
                passed++;
                Console.WriteLine("PASS map " + Path.GetFileName(file));
            }
            catch (Exception e)
            {
                var actual = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                failures.Add(Path.GetFileName(file) + ": " + actual.GetType().Name + ": " + actual.Message);
                Console.Error.WriteLine("FAIL map " + file + ": " + e);
            }
        }
        results.Add($"Maps decoded on device: {passed}/{maps.Length}");
        results.AddRange(failures.Take(3));
        if (failures.Count > 3) results.Add("Additional failures: " + (failures.Count - 3));
        results.Add("No gameplay or save APIs invoked. Not a room-load test.");
        return string.Join("\n", results);
    }

    private static void ShowDiagnostic(string summary, Exception? error)
    {
        // SDL's UIKit delegate continues the event loop after its main callback
        // returns. Keep a strong reference to a separate window, above SDL's
        // launch window, instead of trying to start UIApplication a second time.
        var detail = error?.ToString() ?? "";
        if (error is TargetInvocationException && error.InnerException != null)
            detail = error.InnerException.ToString();
        detail += ReadGameLogDiagnostic();
        var message = summary + "\nStage: " + stage + "\n" + detail;
        ShowMessage(message);
    }

    private static void ShowMessage(string message)
    {
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
                Text = "Native framework not loaded.\n" + startupError,
                Lines = 0, TextAlignment = UITextAlignment.Center,
                TextColor = UIColor.White
            });
            Window.RootViewController = vc;
            Window.MakeKeyAndVisible();
            return true;
        }
    }
}
