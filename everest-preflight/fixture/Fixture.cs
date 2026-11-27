namespace EverestFixture;
public static class Sample
{
    public static int Value() => 7;
}

public static class HookTarget
{
    public delegate int Orig(int value);
    public delegate int Hook(Orig orig, int value);
    public static event Hook? Hooks;

    public static int Calculate(int value)
    {
        Orig chain = static v => v + 1;
        if (Hooks != null)
            foreach (Hook hook in Hooks.GetInvocationList())
            {
                var inner = chain;
                chain = v => hook(inner, v);
            }
        return chain(value);
    }
}
