using EverestFixture;
namespace EverestMockMod;
public static class Entry
{
    public static int DependencyValue() => Sample.Value();
    private static int First(HookTarget.Orig orig, int v) => orig(v * 2) + 10;
    private static int Second(HookTarget.Orig orig, int v) => orig(v + 3) * 2;
    public static void Load() { HookTarget.Hooks += First; HookTarget.Hooks += Second; }
    public static void Unload() { HookTarget.Hooks -= Second; HookTarget.Hooks -= First; }
}
