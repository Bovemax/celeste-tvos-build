param([Parameter(Mandatory=$true)][string]$Source)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Source).Path
$checks = [ordered]@{
    'GameLoader.cs' = 'AreaData\.Load\(|Dialog\.Load\('
    'AreaData.cs' = 'public static.*Load\(|List<AreaData>|new AreaData|new ModeProperties|MapData\s*=|Path\s*=|Get\(AreaKey'
    'MapData.cs' = 'public MapData\(|public .*Load\(|BinaryPacker|AreaData.Get|StartLevel\(|ModeData|StrawberriesByCheckpoint'
    'AreaKey.cs' = 'public AreaKey\(|ChapterIndex|public .* ID|public .*Mode'
    'ModeProperties.cs' = 'public .*Path|public .*MapData|public .*Checkpoints'
    'SaveData.cs' = 'public .*AfterInitialize\(|public .*BeforeSave\(|Areas\.Count|public .*MaxArea|public static .*Start\('
    'Session.cs' = 'public Session\(|public .*MapData|public .*LevelData|public .*Area'
    'LevelLoader.cs' = 'public LevelLoader\(|Engine\.Scene|SaveData'
    'OuiMainMenu.cs' = 'public .*CreateButtons\(|public .*Enter\(|new MenuButton|OnPlay|OnExit'
    'Dialog.cs' = 'public static .*Load\(|TitleContainer|Content[\\/]Dialog'
}
foreach ($name in $checks.Keys) {
    $found = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $name | Where-Object {
        $_.FullName.Substring($root.Length) -notmatch '[\\/](bin|obj)[\\/]'
    })
    if ($found.Count -ne 1) { Write-Output "=== $name : $($found.Count) candidates ==="; continue }
    $file = $found[0]
    Write-Output "=== $name ==="
    Select-String -LiteralPath $file.FullName -Pattern $checks[$name] | ForEach-Object {
        Write-Output ("{0}: {1}" -f $_.LineNumber, $_.Line.Trim())
    }
}
