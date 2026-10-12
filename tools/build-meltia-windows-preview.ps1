# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# Build an isolated, non-TSF keyboard preview from reusable Meltype code.
# Run from a Windows PowerShell/PowerShell 7 terminal with .NET SDK 10.
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$SkipSmokeTest
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src/Meltia.Windows/Meltia.Windows.csproj"
$restoreConfig = Join-Path $root "tools/meltia-publish.nuget.config"
$flavor = if ($FrameworkDependent) { "-framework-dependent" } else { "" }
$out = Join-Path $root "artifacts/meltia-windows-preview-$Runtime$flavor"
$zip = Join-Path $root "artifacts/meltia-windows-preview-$Runtime$flavor.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK 10 is required. No publish or validation was performed."
}
$sdks = @(dotnet --list-sdks)
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw ".NET 10 SDK not found. Available SDKs: $($sdks -join ', ')"
}
if (-not (Test-Path -LiteralPath $restoreConfig)) {
    throw "Missing tools/meltia-publish.nuget.config. Update to the latest preview branch."
}
# The repository-level nuget.config explicitly clears online sources.
# Restore runtime packs via a publishing-only config, not by editing the
# developer's global NuGet sources or weakening the offline core test setup.
# Self-contained requires Microsoft.NETCore.App.Runtime.win-* and other
# Windows runtime packages from nuget.org. The framework-dependent fallback
# uses the matching .NET 10 desktop runtime installed on the user's PC.
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
Write-Host "Meltia restore: $Runtime (self-contained=$selfContained)"
Write-Host "NuGet sources: tools/meltia-publish.nuget.config (nuget.org)"
dotnet restore $project -r $Runtime --configfile $restoreConfig -p:SelfContained=$selfContained -p:PublishSingleFile=false --nologo
if ($LASTEXITCODE -ne 0) {
    if ($FrameworkDependent) {
        throw "NuGet restore failed. Check the local .NET SDK 10 and Windows desktop reference packs."
    }
    throw "NuGet runtime-pack restore failed. Check network access to https://api.nuget.org/v3/index.json; for an installed .NET 10 Desktop runtime, try -FrameworkDependent."
}

New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
# Never reuse the previous published folder: old Meltype files must not be
# accidentally included in the renamed Meltia package.
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish $project -c Release -r $Runtime --self-contained $selfContained --no-restore -p:PublishSingleFile=false -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed after restore; no package created." }
$exe = Join-Path $out "Meltia.exe"
if (-not (Test-Path $exe)) { throw "Published Meltia.exe missing." }
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE") -Force
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") (Join-Path $out "THIRD-PARTY-NOTICES.md") -Force
Copy-Item (Join-Path $root "docs/MELTIA_LICENSE_POLICY.md") (Join-Path $out "MELTIA_LICENSE_POLICY.md") -Force
Copy-Item (Join-Path $root "docs/MELTIA_WINDOWS_PREVIEW.md") (Join-Path $out "MELTIA_WINDOWS_PREVIEW.md") -Force
$releaseNotes = Join-Path $root "docs/MELTIA_PREVIEW_RELEASE_NOTES_v1.0.0-beta.1.md"
if (Test-Path -LiteralPath $releaseNotes) {
    Copy-Item $releaseNotes (Join-Path $out "PREVIEW_RELEASE_NOTES.md") -Force
}

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
if ($FrameworkDependent) {
    Write-Host "Framework-dependent: the target PC MUST have .NET 10 Windows Desktop Runtime installed."
}
