# External port references

Discovered by the project user. Keep production v10 unchanged; these are research references, not a replacement build or proof of compatibility with our host.

## Apple native static-AOT port

[hmcneill46/celeste-ios](https://github.com/hmcneill46/celeste-ios/tree/b65bedd20016dc3482d7702d7f0a9707bc2b1479), tvos-port at b65bedd20016dc3482d7702d7f0a9707bc2b1479 (2026-09-09).
- [Architecture](https://github.com/hmcneill46/celeste-ios/blob/b65bedd20016dc3482d7702d7f0a9707bc2b1479/docs/APPLE_EVEREST_STATIC_AOT.md)
- [Compatibility evidence](https://github.com/hmcneill46/celeste-ios/blob/b65bedd20016dc3482d7702d7f0a9707bc2b1479/docs/APPLE_EVEREST_COMPATIBILITY.md)
- [License](https://github.com/hmcneill46/celeste-ios/blob/b65bedd20016dc3482d7702d7f0a9707bc2b1479/LICENSE): MIT for covered repository source; retain notices and independently verify third-party terms.

Native full AOT (not our interpreter architecture). Exact pinned Everest stable 1.6458.0 at 4bbde91b8dbaaddef2ceec75ca0cd6d59b3b8d00, same MonoMod submodule dfc30a1506d37fb88a2c2be004f525205f46a24c. Host-generated static hooks with cached typed orig chains, bounded direct Hook lowering, offline IL freezing, source-free binary transformations, exact custom entity registries, content/dependency gates, mod save serialization, selected custom audio. Current Stage 25K-L host preflight passed, exact physical product acceptance pending. Selected Strawberry Jam Beginner lobby and Bing only, not full campaign; some earlier products have reported device passes. No on-device runtime mod loader in this design. Study existing code and tests before duplicating; consider adapting typed dispatcher and content analysis to interpreter host, compare version/API surfaces, preserve attribution. No claims of arbitrary mod support.

## WebAssembly mobile port

[Unlim8ted-Studios/Celeste-Mobile-Port](https://github.com/Unlim8ted-Studios/Celeste-Mobile-Port/tree/5cc3844ee08931d198d4af77ba69733a698e0309) at 5cc3844ee08931d198d4af77ba69733a698e0309. Android GeckoView with Everest and in-app mod management; based on MercuryWorkshop/celeste-wasm. Experimental iOS WKWebView wrapper has not been compiled or tested per [wrapper README](https://github.com/Unlim8ted-Studios/Celeste-Mobile-Port/blob/5cc3844ee08931d198d4af77ba69733a698e0309/IOSWrapper/README.md); not evidence of tvOS compatibility. Study UI, dependency/download behavior and WASM runtime constraints as reference only; do not adopt binaries or assets or assume license to copy code.

## Development decision

Retain independently validated v10, keep existing cheap desktop fixture as control. Before new device builds, compare reference hook and loader mechanisms against our interpreter, identify reusable licensed components and select minimal representative tests. Offline static transformations are a useful fallback even if managed runtime loading works; runtime detours remain unproved. No proprietary uploads, private paid cloud builds, or unnecessary macOS jobs.
