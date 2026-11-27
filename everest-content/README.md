# First playable content milestone

Start from production v10 source and host, NOT the synthetic preflight host. Create separate isolated game package. No original save import, no changes to production app.

The reference Apple project ContentCompiler.cs reads Maps/*.bin, adjacent metadata, embedded presentation data, and maps into a stable identity. ClosureGenerator.cs wires content after AreaData.Load, patches MapData and progression. Its MapDataCompatibilityPatch.cs addresses checkpoint/berry index normalization. These are references, not directly applicable without validating our exact source. See ../everest-preflight/EXTERNAL_REFERENCES.md.

Initial target: a single content-only map with only vanilla entities, no DLL, native plugin, Lua, helpers, custom audio, or unreviewed assets. Candidate I Accidentally Four Cassette Blocks 1.0.0 is documented in reference compatibility matrix; verify actual package metadata/content locally before choosing. No mod bytes in public Git. A temporary isolated entry path must not silently replace existing vanilla areas or write custom progression under a vanilla save identity. Prefer explicit isolated launch and no persistent custom progression until identity mapping is implemented.

Get-ContentIntegrationAnchors.ps1 is a read-only local source inventory. It locates exact relevant declarations and call sites, omits bin/obj, prints no save data. Review its output before generating version-locked source patches. Do not claim this script is a loader.
