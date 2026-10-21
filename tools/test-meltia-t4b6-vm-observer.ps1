# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T4-B6 builds BOTH architectures but exercises only --selftest (no COM/TSF).
# The --vm-readonly code path is never run in CI or by this script.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'VS Build Tools not found' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC x64/x86 required' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t4b6-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
$q = [char]34
try {
    $snapshot = Join-Path $temp 'MeltiaT4b6IdentitySnapshot.h'
    python (Join-Path $repo 'tools/generate_meltia_t4b6_snapshot.py') --output $snapshot
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $snapshot)) {
        throw 'T4-B6 cannot compile without a validated, disabled T2 snapshot'
    }
    foreach ($arch in @('x64', 'x86')) {
        $dir = Join-Path $temp $arch
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $exe = Join-Path $dir 'MeltiaT4b6VmTsfObserver.exe'
        $source = Join-Path $repo 'tools/meltia_t4b6_vm_tsf_observer.cpp'
        $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
        $cmd = @(
            ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
            ('cd /d ' + $q + $dir + $q),
            ('cl /nologo /std:c++17 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE ' +
             '/I' + $q + $temp + $q + ' /Fe:' + $q + $exe + $q + ' ' + $q + $source + $q +
             ' /link ole32.lib uuid.lib')
        ) -join ' && '
        cmd /c $cmd
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe)) {
            throw "T4-B6 native observer compile failed: $arch"
        }
        & $exe --selftest
        if ($LASTEXITCODE -ne 0) { throw "T4-B6 no-COM selftest failed: $arch" }
        # Default/invalid argument paths must not invoke OS probing.
        & $exe --incorrect-option | Out-Null
        if ($LASTEXITCODE -ne 2) { throw "T4-B6 unexpected live execution path: $arch" }
        Write-Host "PASS T4-B6 $arch (synthetic only; VM API is never invoked)"
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# Invalid-CLI rejection intentionally returned 2; do not leak it as the script exit.
exit 0
