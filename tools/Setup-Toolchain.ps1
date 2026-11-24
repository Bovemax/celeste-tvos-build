# Download exact upstream distributions locally; no administrator access or PATH edits.
[CmdletBinding()]
param([switch]$SkipLLVM)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot 'toolchain'
New-Item -ItemType Directory -Force $root | Out-Null
function Download-Checked($url, $file, $expected, $algorithm = 'SHA256') {
    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
    if ((Get-FileHash $file -Algorithm $algorithm).Hash -ne $expected) {
        throw "Checksum mismatch: $url"
    }
}
function Run-Checked($exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Failed: $exe ($LASTEXITCODE)" }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ("CelesteTools-" + [guid]::NewGuid())
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
        Run-Checked (Join-Path $dotnet 'dotnet.exe') @('tool','install','ilspycmd','--version','8.0.0.7246-preview3','--tool-path',$ilspy,'--add-source','https://api.nuget.org/v3/index.json')
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
            # Official archive (large download); extract only bin and license files.
            $archive = Join-Path $temp 'llvm.tar.xz'
            Download-Checked 'https://github.com/llvm/llvm-project/releases/download/llvmorg-20.1.8/clang%2Bllvm-20.1.8-x86_64-pc-windows-msvc.tar.xz' $archive 'f229769f11d6a6edc8ada599c0cda964b7dee6ab1a08c6cf9dd7f513e85b107f'
            New-Item -ItemType Directory -Force $llvm | Out-Null
            $members = @(tar.exe -tf $archive)
            if ($LASTEXITCODE -ne 0) { throw 'Could not list LLVM archive' }
            $selected = @($members | Where-Object { $_ -match '^[^/]+/bin/[^/]+\.(dll|exe)$' -and $_ -match '/(lld|ld64\.lld|[^/]+\.dll)(\.exe)?$' })
            $selected += @($members | Where-Object { $_ -match '^[^/]+/(LICENSE[^/]*|share/doc/llvm/LICENSE[^/]*)$' })
            if (-not $selected) { throw 'LLVM archive did not contain expected files' }
            & tar.exe -xf $archive -C $llvm --strip-components 1 @selected
            if ($LASTEXITCODE -ne 0) { throw 'LLVM extraction failed' }
        }
        if (Test-Path $ld64) { Run-Checked $ld64 @('--version') }
        elseif (Test-Path $lld) { Run-Checked $lld @('-flavor','darwin','--version') }
        else { throw 'LLVM archive lacks a usable Mach-O linker' }
    }
    Write-Host 'Portable toolchain ready. No system PATH changes.'
}
finally { Remove-Item -Recurse -Force $temp }
