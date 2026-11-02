param(
    [string]$CompilerPath,
    [switch]$SkipPublish,
    [string]$SignToolCommand = $env:SPEZI_SIGNTOOL
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $CompilerPath = $command.Source }
    else {
        foreach ($candidate in @(
            (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
        )) {
            if (Test-Path -LiteralPath $candidate) { $CompilerPath = $candidate; break }
        }
    }
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) {
    throw 'Inno Setup 7.1 or later is required. Install the compiler or supply -CompilerPath with the full path to ISCC.exe.'
}
if (!$SkipPublish) {
    $running = @(Get-Process -Name Spezi -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count) { throw 'Exit Spezi from the system tray before rebuilding.' }
    Push-Location -LiteralPath $projectRoot
    try {
        dotnet publish src\Spezi.App\Spezi.App.csproj -c Release -r win-x64 --self-contained true -o dist -v minimal -m:1 -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Spezi could not be built.' }
    }
    finally { Pop-Location }
}
$payload = Join-Path $projectRoot 'dist'
$exe = Join-Path $payload 'Spezi.exe'
if (!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath (Join-Path $payload 'coreclr.dll'))) { throw 'The complete Spezi build is missing from dist.' }
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Unexpected Spezi version: $version" }
$signArgs = @()
if ($SignToolCommand) { $signArgs = @('/DSignToolName=spezisign', ('/Sspezisign=' + $SignToolCommand)) }
& $CompilerPath /Q @signArgs ('/DProjectRoot=' + $projectRoot) ('/DAppVersion=' + $version) ('/O' + $PSScriptRoot) (Join-Path $PSScriptRoot 'Spezi.iss')
if ($LASTEXITCODE -ne 0) { throw 'The installer could not be built.' }
$setup = Join-Path $PSScriptRoot 'Spezi-Setup.exe'
$checksum = Get-FileHash -LiteralPath $setup -Algorithm SHA256
($checksum.Hash.ToLowerInvariant() + '  Spezi-Setup.exe') | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Spezi-Setup.exe.sha256') -Encoding ascii
& (Join-Path $projectRoot 'tools\Verify-Checksums.ps1') -Files $setup
if ($LASTEXITCODE -ne 0) { throw 'Checksum verification failed.' }
Get-Item -LiteralPath $setup | Select-Object FullName, Length, LastWriteTime
