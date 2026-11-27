param(
    [Parameter(Mandatory=$true)][string]$ModZip,
    [string]$Root = "C:\CelesteTV"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Root = (Resolve-Path -LiteralPath $Root).Path
$zipPath = (Resolve-Path -LiteralPath $ModZip).Path
$expected = '37eaa16b6b2d458a8ce27e08ea315b4cc3c2938d6b3c2292c61203c641ad7c95'
if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ine $expected) {
    throw "Wrong ZIP hash. This test requires the exact audited IAccidentallyFourCassetteBlocks 1.0.0 package. Do not bypass this check."
}
$oldSource = Join-Path $Root 'work4\patch-build\source-v10'
$newSource = Join-Path $Root 'work4\patch-build\source-everest-smoke'
$stage = Join-Path $Root 'work4\everest-content-smoke'
$baseContent = Join-Path $Root 'inputs\Content'
$baseManaged = Join-Path $Root 'work4\patched-v10'
$dotnet = Join-Path $Root 'toolchain\dotnet\dotnet.exe'
foreach ($path in @($oldSource,$baseContent,$baseManaged,$dotnet)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Missing $path" }
}
foreach ($path in @($newSource,$stage)) {
    if (Test-Path -LiteralPath $path) { throw "Existing test directory: $path. Refusing to overwrite it." }
}
function Find-Source($root, $name) {
    $items = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $name |
        Where-Object { $_.FullName.Substring($root.Length) -notmatch '[\\/](bin|obj)[\\/]' })
    if ($items.Count -ne 1) { throw "Expected one $name, found $($items.Count)" }
    return $items[0].FullName
}
function Replace-Once([string]$text,[string]$old,[string]$new) {
    $index = $text.IndexOf($old,[StringComparison]::Ordinal)
    if ($index -lt 0 -or $text.IndexOf($old,$index+$old.Length,[StringComparison]::Ordinal) -ge 0) {
        throw "Source anchor missing or nonunique: $old"
    }
    return $text.Substring(0,$index)+$new+$text.Substring($index+$old.Length)
}
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name -ne '' })
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        $name = $entry.FullName
        if ($name -match '\\|^/|:|(?:^|/)\.\.?(?:/|$)' -or !$seen.Add($name)) {
            throw "Unsafe or duplicate archive entry: $name"
        }
        if ($name -match '\.(dll|exe|dylib|so|lua|bank)$') { throw "Unexpected code/audio payload: $name" }
        if ($entry.Length -gt 16777216) { throw "Unexpectedly large entry: $name" }
    }
    $maps = @($entries | Where-Object { $_.FullName -cmatch '^Maps/.+\.bin$' })
    if ($maps.Count -ne 1) { throw "Expected exactly one map BIN, found $($maps.Count)" }
    $map = $maps[0]
    $memory = New-Object IO.MemoryStream
    $stream = $map.Open()
    try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
    $bytes = $memory.ToArray()
    $memory.Position = 0
    $reader = New-Object IO.BinaryReader($memory)
    try {
        if ($reader.ReadString() -cne 'CELESTE MAP') { throw "Unexpected BIN header" }
        $package = $reader.ReadString()
    } finally { $reader.Dispose() }
    if ($package -notmatch '^[A-Za-z0-9_ .-]+(?:/[A-Za-z0-9_ .-]+)*$' -or
        $package -match '(?:^|/)\.\.?(?:/|$)' -or $package.Length -gt 180) {
        throw "Unsupported embedded package name: $package"
    }
    $relativeMap = 'Maps\' + $package.Replace('/','\') + '.bin'
    if (Test-Path -LiteralPath (Join-Path $baseContent $relativeMap)) {
        throw "Custom map would collide with vanilla content"
    }
    Write-Host "Map archive entry: $($map.FullName)"
    Write-Host "Embedded package: $package"
    Write-Host "Only the map BIN is staged in this smoke test; metadata, custom dialog and GUI are not implemented."
    $entries | Select-Object FullName,Length | Format-Table -AutoSize
} finally { $archive.Dispose() }

# Validate local source anchors before creating any test files.
$areaFile = Find-Source $oldSource 'AreaData.cs'
$menuFile = Find-Source $oldSource 'OuiMainMenu.cs'
$area = [IO.File]::ReadAllText($areaFile)
$menu = [IO.File]::ReadAllText($menuFile)
$areaAnchor = 'int num = Enum.GetNames(typeof(AreaMode)).Length;'
$areaInsert = @"
var tvosMap = (AreaData)Areas[1].MemberwiseClone();
        tvosMap.Interlude = false;
        tvosMap.OnLevelBegin = null;
        tvosMap.CompleteScreenName = null;
        tvosMap.Mode = new ModeProperties[3] {
            new ModeProperties {
                Path = "$package",
                PoemID = null,
                Checkpoints = null,
                Inventory = Areas[1].Mode[0].Inventory,
                AudioState = new AudioState("event:/music/lvl0/intro", "event:/env/amb/00_prologue")
            },
            null, null
        };
        Areas.Add(tvosMap);
        $areaAnchor
"@
$area = Replace-Once $area $areaAnchor $areaInsert
$menuAnchor = 'climbButton = new MainMenuClimb(this, vector, vector + vector2, OnBegin);'
$menu = Replace-Once $menu $menuAnchor 'climbButton = new MainMenuClimb(this, vector, vector + vector2, TvosOpenContentSmoke);'
$menuInsert = @'
private void TvosOpenContentSmoke()
    {
        Focused = false;
        new FadeWipe(base.Scene, wipeIn: false, delegate {
            SaveData.InitializeDebugMode(false);
            var key = new AreaKey(AreaData.Areas.Count - 1);
            var map = AreaData.Areas[key.ID].Mode[0].MapData;
            if (map == null || map.Levels == null || map.Levels.Count == 0)
                throw new System.InvalidOperationException("Custom smoke map did not load");
            global::Monocle.Engine.Scene = new LevelLoader(new Session(key));
        });
    }

    public void CreateButtons()
'@
$menu = Replace-Once $menu 'public void CreateButtons()' $menuInsert

# Copy source to a sibling so relative project references remain valid.
# No game source or assets are uploaded.
& robocopy $oldSource $newSource /E /XD bin obj .git /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Source copy failed ($LASTEXITCODE)" }
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Find-Source $newSource 'AreaData.cs'),$area,$utf8)
[IO.File]::WriteAllText((Find-Source $newSource 'OuiMainMenu.cs'),$menu,$utf8)
$project = Join-Path $newSource 'Celeste.csproj'
& $dotnet build $project -c Release -p:Platform=AnyCPU -p:PlatformTarget=AnyCPU -p:Prefer32Bit=false --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw "Managed compile failed" }

$managed = Join-Path $stage 'Managed'
$content = Join-Path $stage 'Content'
[IO.Directory]::CreateDirectory($managed) | Out-Null
[IO.Directory]::CreateDirectory($content) | Out-Null
Copy-Item -LiteralPath (Join-Path $newSource 'bin\Release\net452\Celeste.exe') -Destination (Join-Path $managed 'Celeste.dll')
Copy-Item -LiteralPath (Join-Path $baseManaged 'Celeste.Content.dll') -Destination $managed
# Hardlink original content for read-only packaging, avoiding another 1 GB copy.
# Never edit these hardlinked files.
$prefix = $baseContent.TrimEnd('\') + '\'
foreach ($file in Get-ChildItem -LiteralPath $baseContent -Recurse -File) {
    $relative = $file.FullName.Substring($prefix.Length)
    $dest = Join-Path $content $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
    New-Item -ItemType HardLink -Path $dest -Target $file.FullName | Out-Null
}
$mapDest = Join-Path $content $relativeMap
if (Test-Path -LiteralPath $mapDest) { throw "Map destination unexpectedly exists" }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($mapDest)) | Out-Null
[IO.File]::WriteAllBytes($mapDest,$bytes)
Write-Host "Prepared isolated managed/content smoke candidate at $stage"
Write-Host "Use ONLY the isolated v10 integration host, never the normal production host."
