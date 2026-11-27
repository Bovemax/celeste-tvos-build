#!/usr/bin/env bash
set -euo pipefail
root="$PWD/everest-fixtures-out"
test ! -e "$root"
mkdir -p "$root/original" "$root/patched" "$root/module"
dotnet build everest-preflight/fixture/Fixture.csproj -c Release -o "$root/original"
dotnet run --project everest-preflight/patch/Patch.csproj -c Release -- "$root/original/EverestFixture.dll" "$root/patched/EverestFixture.dll"
dotnet build everest-preflight/module/Module.csproj -c Release -o "$root/module"
# Only the independently built mod belongs in the mod directory.
rm -f "$root/module/EverestFixture.dll" "$root/module/EverestFixture.pdb"
dotnet run --project everest-preflight/runner/Runner.csproj -c Release -- "$root"
