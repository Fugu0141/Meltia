# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# Build an isolated, non-TSF keyboard preview from reusable Meltype code.
# Run from a Windows PowerShell/PowerShell 7 terminal with .NET SDK 10.
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$SkipSmokeTest
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src/Meltia.Windows/Meltia.Windows.csproj"
$out = Join-Path $root "artifacts/meltia-windows-preview-$Runtime"
$zip = Join-Path $root "artifacts/meltia-windows-preview-$Runtime.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 10 is required. No publish or validation was performed."
}
$sdks = @(dotnet --list-sdks)
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw ".NET 10 SDK not found. Available SDKs: $($sdks -join ', ')"
}
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
# Never reuse the previous published folder: old Meltype files must not be
# accidentally included in the renamed Meltia package.
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed; no package created." }
$exe = Join-Path $out "Meltia.exe"
if (-not (Test-Path $exe)) { throw "Published Meltia.exe missing." }
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE") -Force
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") (Join-Path $out "THIRD-PARTY-NOTICES.md") -Force
Copy-Item (Join-Path $root "docs/MELTIA_WINDOWS_PREVIEW.md") (Join-Path $out "MELTIA_WINDOWS_PREVIEW.md") -Force

if (-not $SkipSmokeTest) {
    $testPath = Join-Path $out "meltia-preview-selftest.json"
    $process = Start-Process -FilePath $exe -PassThru -Wait -ArgumentList @("--meltia-selftest", ('"' + $testPath + '"'))
    if ($process.ExitCode -ne 0) {
        throw "Meltia self-test failed (exit $($process.ExitCode)); package not created."
    }
    if (-not (Test-Path $testPath)) {
        throw "Meltia self-test did not save its JSON report."
    }
    $result = Get-Content -Raw -LiteralPath $testPath | ConvertFrom-Json
    if (-not $result.Passed) {
        throw "Meltia self-test JSON reports failure; no package created."
    }
}
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Meltia preview package: $zip"
Write-Host "Keyboard preview ONLY. This is not a TSF-registered IME release."
