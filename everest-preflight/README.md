# Everest compatibility preflight

Baseline: production v10 `cd893aa7421dd82c3a9a245742c712d81df0e81e`, device-confirmed save migration.
Upstream source: Everest [2bb355f](https://github.com/EverestAPI/Everest/tree/2bb355f6b1d9abb35b4ef07c6094102c5f8243e6);
pinned MonoMod [dfc30a1](https://github.com/MonoMod/MonoMod/tree/dfc30a1506d37fb88a2c2be004f525205f46a24c).

## Source findings
- Everest targets net8.0 and references Mono.Cecil 0.11.6, MonoMod Patcher, Utils, RuntimeDetour, HookGen, NLua, and additional managed libraries.
- MiniInstaller runs MonoMod on FNA and Celeste then generates and patches MMHOOK. Desktop patching is distinct from runtime detours.
- BOOT.cs has desktop-specific platform assumptions. EverestModuleAssemblyContext uses custom load contexts, relinking and native library lookup.
- MonoMod PlatformTriple.CreateCurrentSystem lacks an iOS implementation. MonoRuntime uses GetFunctionPointer for compilation and entry points. A platform-name workaround does not prove interpreter detours safe or functional.
- No direct download/execution of mods or native detours on Apple TV in this fixture.

## First portable fixture
Build a synthetic DLL, rewrite a method with the same Cecil version as Everest, preserve the original, and load both path and stream forms into separate contexts; verify reflection and delegate invocation. Report dynamic-code capabilities and test a simple DynamicMethod only when advertised.
This is a desktop control, not a MonoMod or tvOS pass. It does not test actual Everest patches or hooks, mod compatibility, Apple code signing, native dependencies or game assets. Next: compare the same fixture on an isolated interpreter host, then investigate precise MonoMod runtime and build-time transformation requirements. Keep production v10 untouched.
