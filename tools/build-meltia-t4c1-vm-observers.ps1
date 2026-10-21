# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T4-C1: compile the two read-only observers for x64/x86.
# DOES NOT run the real TSF/registry probes, install TIP, or register IME.
param([Parameter(Mandatory = $true)][string]$Output)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = [IO.Path]::GetFullPath($Output)
if ((Test-Path -LiteralPath $out) -or
    $out.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $out -eq $repo) {
    throw 'T4-C1 build output must be a fresh folder outside the checkout'
}

$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'MSVC Build Tools not found' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC x64/x86 not found' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$q = [char]34
try {
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $header = Join-Path $out 'MeltiaT4b6IdentitySnapshot.h'
    python (Join-Path $repo 'tools/generate_meltia_t4b6_snapshot.py') --output $header
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $header)) {
        throw 'T2 disabled identity snapshot failed'
    }
    foreach ($arch in @('x64', 'x86')) {
        $dir = Join-Path $out $arch
        New-Item -ItemType Directory -Path $dir | Out-Null
        $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
        foreach ($spec in @(
            @{Name='MeltiaT4bProfileInventory'; Path='tools/meltia_t4b_profile_inventory.cpp'; Flags=''},
            @{Name='MeltiaT4b6VmTsfObserver'; Path='tools/meltia_t4b6_vm_tsf_observer.cpp'; Flags=('/I' + $q + $out + $q)}
        )) {
            $exe = Join-Path $dir ($spec.Name + '.exe')
            $src = Join-Path $repo $spec.Path
            $command = @(
                ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
                ('cd /d ' + $q + $dir + $q),
                ('cl /nologo /std:c++17 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE ' +
                 $spec.Flags + ' /Fe:' + $q + $exe + $q + ' ' + $q + $src + $q +
                 ' /link ole32.lib uuid.lib')
            ) -join ' && '
            cmd /c $command
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe)) {
                throw "T4-C1 $($spec.Name) build failed: $arch"
            }
            & $exe --selftest
            if ($LASTEXITCODE -ne 0) { throw "T4-C1 synthetic no-OS selftest failed: $arch" }
            Write-Host "PASS T4-C1 $arch $($spec.Name) (synthetic/build only)"
        }
    }
    Write-Host "Build-only: observers in $out (NEVER registered or executed against live TSF)."
}
catch {
    # A partial build should not become a reusable operator artifact.
    Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction SilentlyContinue
    throw
}
exit 0
