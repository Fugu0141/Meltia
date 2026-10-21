# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T4-B1 only builds and executes --selftest with fabricated profiles.
# Real TSF enumeration is for a separate isolated VM operator; never CI.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'VS Build Tools not found' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC x64/x86 is required' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t4b1-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
$q = [char]34
try {
    foreach ($arch in @('x64', 'x86')) {
        $dir = Join-Path $temp $arch
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $exe = Join-Path $dir 'MeltiaT4bProfileInventory.exe'
        $source = Join-Path $repo 'tools/meltia_t4b_profile_inventory.cpp'
        $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
        $cmd = @(
            ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
            ('cd /d ' + $q + $dir + $q),
            ('cl /nologo /std:c++17 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE ' +
             '/Fe:' + $q + $exe + $q + ' ' + $q + $source + $q +
             ' /link ole32.lib uuid.lib')
        ) -join ' && '
        cmd /c $cmd
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe)) {
            throw "T4-B1 read-only native audit compiler failed: $arch"
        }
        & $exe --selftest
        if ($LASTEXITCODE -ne 0) { throw "T4-B1 synthetic native test failed: $arch" }
        Write-Host "PASS T4-B1 $arch (synthetic; no TSF COM/registry access)"
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
