# Device result: isolated Everest interpreter preflight

User-provided physical Apple TV screenshot, build ccf8cad69e67ae508236a7c46fa064ce69bfc530 (host run 45936029175; portable run 45936029177).

- .NET 10.0.15 / Arm64
- Dynamic code supported=True, compiled=False
- PASS original and rewritten path load
- PASS rewritten stream load and delegates
- PASS mock mod dependency and typed hooks
- PASS simple DynamicMethod delegate

The tested hook is an ordinary typed managed event, not a native MonoMod detour. No actual Everest game, mod, arbitrary runtime IL rewrite, native detour, or mod save path was tested. Do not repeat this fixture. Preserve baseline/v10-playable. Use offline rewrites with typed dispatch, plus runtime managed module loading, as the initial architecture; investigate full Everest separately.
