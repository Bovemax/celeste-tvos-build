[CmdletBinding()]
param([string]$Root='C:\CelesteTV')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path -LiteralPath $Root).Path
$src=Join-Path $Root 'work4\everest-offline-1.6458.0'
$dst=Join-Path $Root 'work4\everest-boot-v1'
$dotnet=Join-Path $Root 'toolchain\dotnet\dotnet.exe'
$cecil=Join-Path $src 'Mono.Cecil.dll'
$project=Join-Path $PSScriptRoot 'BootPatcher.csproj'
foreach ($p in @($dotnet,$cecil,$project)) {
    if (!(Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing $p" }
}
& $dotnet run --project $project -c Release "-p:CecilPath=$cecil" -- $src $dst
if ($LASTEXITCODE -ne 0) { throw 'Boot patch failed; do not package' }
Write-Host "Prepared: $dst"
