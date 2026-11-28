[CmdletBinding()]
param(
    [string]$Root = 'C:\CelesteTV',
    [string]$Managed,
    [string]$ReleaseZip
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path
if (!$Managed) { $Managed = Join-Path $Root 'inputs\managed' }
$Managed = (Resolve-Path -LiteralPath $Managed).Path
$dotnet = Join-Path $Root 'toolchain\dotnet\dotnet.exe'
$stage = Join-Path $Root 'work4\everest-offline-1.6458.0'
$releaseHash = 'd1f3ab569c3417a926d24604953ce73d93160c1f748bf5827124ee7c8df41720'
$url = 'https://github.com/EverestAPI/Everest/releases/download/stable-1.6458.0/main.zip'
if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "Missing portable dotnet: $dotnet" }
if (Test-Path -LiteralPath $stage) { throw "Stage exists; preserve it for diagnosis: $stage" }
foreach ($name in @('Celeste.exe','FNA.dll','Celeste.Content.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $Managed $name) -PathType Leaf)) { throw "Missing original managed input $name in $Managed" }
}
# The upstream installer targets .NET 8 desktop CoreCLR; do not force it to Mono/tvOS.
$runtimes = & $dotnet --list-runtimes
if ($LASTEXITCODE -ne 0 -or !($runtimes -match '^Microsoft.NETCore.App 8\.')) {
    throw 'The portable dotnet installation needs a .NET 8 runtime to run the official installer.'
}
if (!$ReleaseZip) {
    $ReleaseZip = Join-Path $Root 'Everest-stable-1.6458.0.zip'
    if (!(Test-Path -LiteralPath $ReleaseZip)) {
        Invoke-WebRequest -Uri $url -OutFile $ReleaseZip
    }
}
$ReleaseZip = (Resolve-Path -LiteralPath $ReleaseZip).Path
if ((Get-FileHash -LiteralPath $ReleaseZip -Algorithm SHA256).Hash -ine $releaseHash) {
    throw 'Official Everest release ZIP digest mismatch.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($ReleaseZip)
try {
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($entry in $zip.Entries) {
        $path = $entry.FullName
        if ($path -match '\\|^/|:|(?:^|/)\.\.?(?:/|$)' -or !$seen.Add($path.TrimEnd('/'))) {
            throw "Unsafe or duplicate archive path: $path"
        }
        $total += $entry.Length
        if ($total -gt 1GB -or $seen.Count -gt 20000) { throw 'Unexpected archive size' }
    }
} finally { $zip.Dispose() }

# Only copy managed inputs. No hardlinks to originals, game content or saves.
$original = Join-Path $stage 'orig'
[IO.Directory]::CreateDirectory($original) | Out-Null
$inputFiles = @(Get-ChildItem -LiteralPath $Managed -File | Where-Object {
    $_.Extension -in @('.exe','.dll','.xml','.config')
})
$manifest = @($inputFiles | ForEach-Object {
    [pscustomobject]@{Name=$_.Name;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
foreach ($file in $inputFiles) {
    Copy-Item -LiteralPath $file.FullName -Destination $original
    Copy-Item -LiteralPath $file.FullName -Destination $stage
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $stage 'input-hashes.json') -Encoding UTF8
# Expand the verified official distribution into a separate temporary tree.
$release = Join-Path $stage '_release'
[IO.Compression.ZipFile]::ExtractToDirectory($ReleaseZip,$release)
$installer = @(Get-ChildItem -LiteralPath $release -Recurse -File -Filter 'MiniInstaller.dll')
if ($installer.Count -ne 1) { throw "Expected exactly one MiniInstaller.dll, found $($installer.Count)" }
$releaseRoot = $installer[0].Directory.FullName
foreach ($name in @('MiniInstaller.runtimeconfig.json','Celeste.Mod.mm.dll','NETCoreifier.dll','MonoMod.Patcher.dll','MonoMod.RuntimeDetour.HookGen.dll','everest-lib\FNA.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $releaseRoot $name) -PathType Leaf)) { throw "Missing release file $name" }
}
Get-ChildItem -LiteralPath $releaseRoot -Force | ForEach-Object {
    if ($_.Name -in @('orig','Content','Saves','Mods','_release','input-hashes.json')) { throw "Unexpected release entry: $($_.Name)" }
    Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse -Force
}
# FastMode explicitly requires orig; it does not launch the game or install native desktop libraries.
# Mirror the pinned release's maingame, fna, hookgen switches; no apphost or updater.
[IO.Directory]::CreateDirectory((Join-Path $stage 'MiniInstallerWorkspace')) | Out-Null
Push-Location $stage
try {
    & $dotnet .\MiniInstaller.dll --fastmode maingame fna hookgen 2>&1 | Tee-Object -FilePath .\offline-patch.log
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "Official offline patching failed ($exitCode); see offline-patch.log" }
    foreach ($name in @('Celeste.dll','FNA.dll','MMHOOK_Celeste.dll')) {
        $file = Get-Item -LiteralPath $name
        if ($file.Length -le 0) { throw "Empty output: $name" }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        Write-Host "$name | $($identity.FullName) | $($file.Length) bytes | SHA256 $((Get-FileHash -LiteralPath $file.FullName).Hash)"
    }
    foreach ($entry in $manifest) {
        if ((Get-FileHash -LiteralPath (Join-Path $Managed $entry.Name)).Hash -ne $entry.SHA256) {
            throw "Original input changed: $($entry.Name)"
        }
    }
    Write-Host 'PASS: desktop Everest patch outputs present; original managed inputs unchanged.'
    Write-Host 'Do not package these assemblies yet: tvOS boot, FNA, native bindings and saves still require integration.'
} finally { Pop-Location }
