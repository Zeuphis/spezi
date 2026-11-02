param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [string]$SignToolCommand = $env:SPEZI_SIGNTOOL
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!(Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup compiler was not found.' }
$exe = Join-Path $projectRoot 'dist\Spezi.exe'
if (!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath (Join-Path $projectRoot 'dist\coreclr.dll'))) { throw 'Build the full self-contained Spezi payload first.' }
$version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'The published Spezi version is invalid.' }
$signArgs = @()
if ($SignToolCommand) { $signArgs = @('/DSignToolName=spezisign', ('/Sspezisign=' + $SignToolCommand)) }
& $CompilerPath /Q @signArgs ('/DProjectRoot=' + $projectRoot) ('/DAppVersion=' + $version) ('/O' + $PSScriptRoot) (Join-Path $PSScriptRoot 'Spezi-Updater.iss')
if ($LASTEXITCODE -ne 0) { throw 'The updater could not be built.' }
$updater = Join-Path $PSScriptRoot 'Spezi-Updater.exe'
((Get-FileHash -LiteralPath $updater).Hash.ToLowerInvariant() + '  Spezi-Updater.exe') | Set-Content -LiteralPath ($updater + '.sha256') -Encoding ascii
& (Join-Path $projectRoot 'tools\Verify-Checksums.ps1') -Files $updater
if ($LASTEXITCODE -ne 0) { throw 'Checksum verification failed.' }
Get-Item -LiteralPath $updater | Select-Object FullName,Length,LastWriteTime
