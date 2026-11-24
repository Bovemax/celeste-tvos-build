using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Foundation;
using UIKit;
using GameController;
using Microsoft.Xna.Framework;

namespace LocalGameHost;

internal static class Program
{
    private static readonly string Bundle = NSBundle.MainBundle.BundlePath;
    private static readonly string Payload = Path.Combine(Bundle, "GameAssemblies");
    private static IntPtr nativeHandle, fmodHandle;
    private static string startupError = "";
    private static readonly SdlMain Callback = Run;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlMain(int argc, IntPtr argv);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_UIKitRunApp(int argc, IntPtr argv, SdlMain callback);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    private static string Framework(string name) =>
        Path.Combine(Bundle, "Frameworks", name + ".framework", name);

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? search)
    {
        switch (name.ToLowerInvariant())
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
            PrepareWritableLocation();
            Console.WriteLine("Apple controllers: " + GCController.Controllers.Length);
            if (!File.Exists(Path.Combine(Payload, "Celeste.dll")))
                RunProbe();
            else
                RunGame();
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunProbe()
    {
        using var game = new FnaProbe();
        game.Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunGame()
    {
        if (!File.Exists(Path.Combine(Payload, "Celeste.Content.dll")))
            throw new FileNotFoundException("Missing Celeste.Content.dll");
        if (!Directory.Exists(Path.Combine(Payload, "Content")))
            throw new DirectoryNotFoundException("Missing game content");
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
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Payload, "Celeste.dll"));
        NativeLibrary.SetDllImportResolver(assembly, Resolve);
        var entry = assembly.EntryPoint ?? assembly.GetType("Celeste.Celeste", true)!
            .GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (entry == null || !entry.IsStatic)
            throw new MissingMethodException("No static game entry point");
        var parameters = entry.GetParameters();
        if (parameters.Length == 0)
            entry.Invoke(null, null);
        else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            entry.Invoke(null, new object[] { Array.Empty<string>() });
        else
            throw new NotSupportedException("Unexpected game entry point signature");
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
