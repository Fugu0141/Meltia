# SPDX-License-Identifier: GPL-3.0-or-later
# Run the same redistribution and self-test checks in PR CI and manual release.
[CmdletBinding()]
param(
    [string]$PackageZip = 'artifacts/meltia-windows-preview-win-x64-framework-dependent.zip',
    [string]$PublishedDir = 'artifacts/meltia-windows-preview-win-x64-framework-dependent',
    [string]$ExpectedVersion = '1.0.0-beta.1'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$zipPath = Join-Path $root $PackageZip
$folderPath = Join-Path $root $PublishedDir
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "No preview ZIP: failing rather than publishing or passing CI ($zipPath)."
}
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $zipPath).Path)
try {
    $files = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $required = @('Meltia.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
        'MELTIA_LICENSE_POLICY.md', 'PREVIEW_RELEASE_NOTES.md',
        'MELTIA_WINDOWS_PREVIEW.md', 'meltia-preview-selftest.json')
    foreach ($name in $required) {
        if ($files -notcontains $name) { throw "Missing required preview file: $name" }
    }
    $forbidden = @('Meltype.exe', 'Register-Tip.ps1', 'meltype-ime.ps1',
        'hostfxr.dll', 'coreclr.dll', 'hostpolicy.dll')
    foreach ($name in $forbidden) {
        if ($files -contains $name) { throw "Forbidden inherited/runtime file: $name" }
    }
    foreach ($file in $files) {
        if ($file -match '(?i)\.(ico|png|jpg|jpeg|svg|iss)$') {
            throw "Unreviewed image or installer asset in ZIP: $file"
        }
        if ($file -match '(?i)(^|/)(native/tip|tip/|mozc/|mac/|linux/)') {
            throw "Non-preview component bundled: $file"
        }
    }
} finally {
    $archive.Dispose()
}
$reportPath = Join-Path $folderPath 'meltia-preview-selftest.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw 'Meltia Preview self-test report is missing.'
}
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if (-not $report.Passed -or $report.Version -ne $ExpectedVersion) {
    throw "Self-test must pass and describe version $ExpectedVersion."
}
if (@($report.Cases | Where-Object { -not $_.passed }).Count -gt 0) {
    throw 'Some self-test cases failed.'
}
$notice = Get-Content -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Raw
$license = Get-Content -LiteralPath (Join-Path $root 'LICENSE') -Raw
if (-not $license.Contains('GNU GENERAL PUBLIC LICENSE') -or
    -not $notice.Contains('CC BY-SA 4.0') -or
    -not $notice.Contains('SCOWL') -or
    -not $notice.Contains('Unicode')) {
    throw 'Required inherited GPL/data notices not present.'
}
Write-Host "PASS: Meltia Preview executable, archive, notices and $ExpectedVersion self-test."
Write-Host 'Scope: hook-free smoke. This is NOT Windows keyboard E2E or TSF IME certification.'
