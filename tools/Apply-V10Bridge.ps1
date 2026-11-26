param(
    [Parameter(Mandatory=$true)][string]$Source
)
$ErrorActionPreference = 'Stop'
$Source = (Resolve-Path -LiteralPath $Source).Path
$dir = Join-Path $Source 'Celeste'
$bridge = Join-Path $dir 'TvosBridge.cs'
if (Test-Path -LiteralPath $bridge) { throw "Bridge already exists. Use a fresh copy of the v9 source." }
function Replace-One([string]$Text,[string]$Needle,[string]$Replacement) {
    $index = $Text.IndexOf($Needle,[StringComparison]::Ordinal)
    if ($index -lt 0 -or $Text.IndexOf($Needle,$index+$Needle.Length,[StringComparison]::Ordinal) -ge 0) {
        throw "Expected exactly one source anchor: $Needle"
    }
    return $Text.Substring(0,$index) + $Replacement + $Text.Substring($index+$Needle.Length)
}
$changes = @{}
$userIO = Join-Path $dir 'UserIO.cs'
$menu = Join-Path $dir 'OuiMainMenu.cs'
$main = Join-Path $dir 'Celeste.cs'
foreach ($p in @($userIO,$menu,$main)) {
    if (Test-Path -LiteralPath "$p.v10-original") { throw "Backup exists: $p.v10-original" }
    $changes[$p] = [IO.File]::ReadAllText($p)
}
$changes[$userIO] = Replace-One $changes[$userIO] 'File.Copy(backupHandle, handle, overwrite: true);' ('File.Copy(backupHandle, handle, overwrite: true);' + [Environment]::NewLine + '                global::Celeste.TvosBridge.Saved?.Invoke(path, data);')
$changes[$userIO] = Replace-One $changes[$userIO] 'File.Delete(handle);' ('if (global::Celeste.TvosBridge.Deleting != null && !global::Celeste.TvosBridge.Deleting(path)) return false;' + [Environment]::NewLine + '            File.Delete(handle);')
$changes[$menu] = Replace-One $changes[$menu] 'Engine.Instance.Exit();' ('global::Celeste.TvosBridge.QuitRequested = true;' + [Environment]::NewLine + '            Engine.Instance.Exit();')
$changes[$main] = Replace-One $changes[$main] 'Audio.Unload();' ('Audio.Unload();' + [Environment]::NewLine + '        global::Celeste.TvosBridge.QuitCompleted = true;')
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($p in $changes.Keys) { Copy-Item -LiteralPath $p -Destination "$p.v10-original" }
foreach ($p in $changes.Keys) { [IO.File]::WriteAllText($p,$changes[$p],$utf8) }
[IO.File]::WriteAllText($bridge,@'
using System;
namespace Celeste
{
    public static class TvosBridge
    {
        public static Action<string, byte[]> Saved;
        public static Func<string, bool> Deleting;
        public static bool QuitRequested;
        public static bool QuitCompleted;
    }
}
'@,$utf8)
Write-Host "v10 bridge applied to $Source. Original files retained as .v10-original."
