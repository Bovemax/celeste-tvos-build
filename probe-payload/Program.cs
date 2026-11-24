using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

// Original managed payload, built separately and never referenced by the host.
internal static class ExternalProbe
{
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SDL_WasInit(uint flags);

    public static int Main(string[] args)
    {
        int result = 0;
        void Test(int bit, Func<bool> test)
        {
            try { if (test()) result |= bit; }
            catch (Exception e) { Console.Error.WriteLine("External probe " + bit + ": " + e); }
        }
        Test(1, () => new Vector2(3, 4).Length() == 5);
        Test(2, () => SDL_WasInit(0) != 0);
        Test(4, () => File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "Content", "probe.txt")) == "Original FNA content probe");
        Test(8, () => {
            var marker = Path.Combine(Directory.GetCurrentDirectory(), "external-probe.txt");
            File.WriteAllText(marker, "external assembly write");
            return File.ReadAllText(marker) == "external assembly write";
        });
        return result;
    }
}
