# Download exact upstream distributions locally; no administrator access or PATH edits.
[CmdletBinding()]
param([switch]$SkipLLVM)
$ErrorActionPreference = 'Stop'
$toolkit = $PSScriptRoot
if (Test-Path (Join-Path $PSScriptRoot 'toolchain.py')) {
    $toolkit = Split-Path -Parent $PSScriptRoot
}
$root = Join-Path $toolkit 'toolchain'
New-Item -ItemType Directory -Force $root | Out-Null
function Download-Checked($url, $file, $expected, $algorithm = 'SHA256') {
    if ((Test-Path -LiteralPath $file) -and ((Get-FileHash -LiteralPath $file -Algorithm $algorithm).Hash -eq $expected)) {
        Write-Host "Reusing verified download: $file"
        return
    }
    $partial = "$file.part"
    try {
        Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing
        if ((Get-FileHash -LiteralPath $partial -Algorithm $algorithm).Hash -ne $expected) {
            throw "Checksum mismatch: $url"
        }
        Move-Item -LiteralPath $partial -Destination $file -Force
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
}
function Run-Checked($exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Failed: $exe ($LASTEXITCODE)" }
}
$cache = Join-Path $root 'cache'
$locations = @{
    DOTNET_CLI_HOME = 'dotnet-home'
    NUGET_PACKAGES = 'nuget/packages'
    NUGET_HTTP_CACHE_PATH = 'nuget/http'
    NUGET_PLUGINS_CACHE_PATH = 'nuget/plugins'
    NUGET_SCRATCH = 'nuget/scratch'
    TEMP = 'temp'
    TMP = 'temp'
    TMPDIR = 'temp'
    HOME = 'home'
    USERPROFILE = 'home'
    APPDATA = 'home/AppData/Roaming'
    LOCALAPPDATA = 'home/AppData/Local'
    XDG_CONFIG_HOME = 'config'
    XDG_CACHE_HOME = 'xdg'
}
foreach ($name in $locations.Keys) {
    $value = Join-Path $cache $locations[$name]
    New-Item -ItemType Directory -Force $value | Out-Null
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
}
$env:GIT_CONFIG_NOSYSTEM = '1'
$env:GIT_CONFIG_GLOBAL = Join-Path $cache 'home/.gitconfig'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$nugetConfig = Join-Path $cache 'NuGet.Config'
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
'@ | Set-Content -Encoding UTF8 $nugetConfig
$temp = Join-Path $env:TEMP ("CelesteTools-" + [guid]::NewGuid())
New-Item -ItemType Directory $temp | Out-Null
try {
    # Microsoft release metadata supplies the SHA512 for the exact pinned SDK.
    $sdk = '8.0.419'
    $dotnet = Join-Path $root 'dotnet'
    if (-not (Test-Path (Join-Path $dotnet "sdk/$sdk/dotnet.dll"))) {
        $releases = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json'
        $entry = @($releases.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdk } | ForEach-Object { $_.files } | Where-Object { $_.rid -eq 'win-x64' -and $_.url -like '*.zip' }) | Select-Object -First 1
        if (-not $entry -or -not $entry.hash) { throw "Pinned .NET SDK not found in official metadata" }
        $zip = Join-Path $temp 'dotnet.zip'
        Download-Checked $entry.url $zip $entry.hash 'SHA512'
        Expand-Archive $zip $dotnet -Force
    }
    $env:DOTNET_ROOT = $dotnet
    $env:DOTNET_ROOT_X64 = $dotnet
    $env:DOTNET_ROLL_FORWARD = 'Major'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:PATH = "$dotnet;$env:PATH"
    $ilspy = Join-Path $root 'ilspy'
    if (-not (Test-Path (Join-Path $ilspy 'ilspycmd.exe'))) {
        Run-Checked (Join-Path $dotnet 'dotnet.exe') @('tool','install','ilspycmd','--version','8.0.0.7246-preview3','--tool-path',$ilspy,'--configfile',$nugetConfig)
    }
    $git = Join-Path $root 'git'
    if (-not (Test-Path (Join-Path $git 'cmd/git.exe'))) {
        $zip = Join-Path $temp 'git.zip'
        Download-Checked 'https://github.com/git-for-windows/git/releases/download/v2.51.2.windows.1/MinGit-2.51.2-64-bit.zip' $zip '8f0a7bc389c0bccc9daf6107cff4efb176348e34b8d787f02a36679a5588e072'
        Expand-Archive $zip $git -Force
    }
    Run-Checked (Join-Path $dotnet 'dotnet.exe') @('--version')
    Run-Checked (Join-Path $ilspy 'ilspycmd.exe') @('--version')
    Run-Checked (Join-Path $git 'cmd/git.exe') @('--version')
    if (-not $SkipLLVM) {
        $llvm = Join-Path $root 'llvm'
        $lld = Join-Path $llvm 'bin/lld.exe'
        $ld64 = Join-Path $llvm 'bin/ld64.lld.exe'
        if (-not (Test-Path $lld) -and -not (Test-Path $ld64)) {
            # Download is retained after an extraction failure, unlike temporary files.
            $extractor = Join-Path $toolkit 'CelesteExtractLLVM.exe'
            if (-not (Test-Path -LiteralPath $extractor)) {
                throw 'Missing CelesteExtractLLVM.exe; extract the toolchain repair ZIP into this folder first.'
            }
            $downloads = Join-Path $cache 'downloads'
            New-Item -ItemType Directory -Force $downloads | Out-Null
            $archive = Join-Path $downloads 'llvm-20.1.8-win64.tar.xz'
            Download-Checked 'https://github.com/llvm/llvm-project/releases/download/llvmorg-20.1.8/clang%2Bllvm-20.1.8-x86_64-pc-windows-msvc.tar.xz' $archive 'f229769f11d6a6edc8ada599c0cda964b7dee6ab1a08c6cf9dd7f513e85b107f'
            $staging = Join-Path $temp 'llvm'
            Run-Checked $extractor @($archive, $staging)
            $stagedLd = Join-Path $staging 'bin/ld64.lld.exe'
            $stagedLld = Join-Path $staging 'bin/lld.exe'
            if (Test-Path $stagedLd) { Run-Checked $stagedLd @('--version') }
            elseif (Test-Path $stagedLld) { Run-Checked $stagedLld @('-flavor','darwin','--version') }
            else { throw 'Extracted archive lacks a usable Mach-O linker' }
            New-Item -ItemType Directory -Force $llvm | Out-Null
            Copy-Item -Path (Join-Path $staging '*') -Destination $llvm -Recurse -Force
            # Keep the verified archive for retry/reinstall; it lives entirely in the toolkit cache.

        }
        if (Test-Path $ld64) { Run-Checked $ld64 @('--version') }
        elseif (Test-Path $lld) { Run-Checked $lld @('-flavor','darwin','--version') }
        else { throw 'LLVM archive lacks a usable Mach-O linker' }
    }
    Write-Host 'Portable toolchain ready. No system PATH changes.'
}
finally { Remove-Item -Recurse -Force $temp }
