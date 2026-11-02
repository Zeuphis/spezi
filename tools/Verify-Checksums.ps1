# Verifies <file>.sha256 next to each installer/updater (format: "<hash>  <name>").
# Default: installer\Spezi-Setup.exe and updater\Spezi-Updater.exe. Exit 1 on mismatch.
param([string[]]$Files)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Files) { $Files = @((Join-Path $root 'installer\Spezi-Setup.exe'), (Join-Path $root 'updater\Spezi-Updater.exe')) | Where-Object { Test-Path -LiteralPath $_ } }
$failed = $false
foreach ($file in $Files) {
    $sumFile = $file + '.sha256'
    if (!(Test-Path -LiteralPath $sumFile)) { Write-Error "Missing checksum: $sumFile" -ErrorAction Continue; $failed = $true; continue }
    $expected = ((Get-Content -LiteralPath $sumFile -TotalCount 1) -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { Write-Error "SHA-256 mismatch: $file" -ErrorAction Continue; $failed = $true }
    else { Write-Host "SHA-256 OK: $([IO.Path]::GetFileName($file))" }
}
if ($failed) { exit 1 }
