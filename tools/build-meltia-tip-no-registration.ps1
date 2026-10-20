# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# Offline x64/x86 compiler verification ONLY; no regsvr32, no registration.
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [ValidateSet('Both', 'x64', 'x86')][string]$Architecture = 'Both'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$staging = Join-Path ([IO.Path]::GetTempPath()) ("meltia-t3a-" + [Guid]::NewGuid().ToString('N'))
python (Join-Path $repo 'tools/prepare_meltia_tip_stage.py') --output $staging
if ($LASTEXITCODE -ne 0) { throw 'T3a source staging failed' }
if (-not (Test-Path -LiteralPath (Join-Path $staging 'STAGING_NOTICE.txt'))) { throw 'Missing T3a no-registration marker' }
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Build Tools not found' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual C++ x64/x86 toolchain not found' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$env:PATH = (Split-Path $vswhere) + ';' + $env:PATH
$sources = 'Dll.cpp', 'TextService.cpp', 'CandidateWindow.cpp', 'DisplayAttributes.cpp', 'LangBar.cpp', 'Pipe.cpp', 'Json.cpp'
$optimize = if ($Configuration -eq 'Release') { '/O2 /MT /DNDEBUG' } else { '/Od /MTd /Zi' }
$libs = 'ole32.lib oleaut32.lib uuid.lib user32.lib gdi32.lib advapi32.lib d2d1.lib dwrite.lib dwmapi.lib'
$targets = if ($Architecture -eq 'Both') { @('x64', 'x86') } else { @($Architecture) }
$q = [char]34
foreach ($arch in $targets) {
    $out = Join-Path $staging "bin\$arch"
    $obj = Join-Path $staging "obj\$arch"
    New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null
    $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
    # cl.exe interprets a trailing backslash before a closing quote as an
    # escape, merging /Fo and /Fd. A forward-slash folder suffix is safe.
    $objForCl = $obj.Replace('\', '/') + '/'
    $cmd = @(
        ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
        ('cd /d ' + $q + $staging + $q),
        ('rc /nologo /fo ' + $q + (Join-Path $obj 'MeltiaTip.res') + $q + ' MeltiaTip.rc'),
        ('cl /nologo /Brepro /std:c++17 /EHsc /W4 /wd4100 /utf-8 /DUNICODE /D_UNICODE ' +
         $optimize + ' /Fo' + $q + $objForCl + $q + ' /Fd' + $q + $objForCl + $q +
         ' /LD ' + ($sources -join ' ') + ' ' + $q + (Join-Path $obj 'MeltiaTip.res') + $q +
         ' /link /Brepro /DEF:MeltiaTip.def /IMPLIB:' + $q + (Join-Path $obj 'MeltiaTip.lib') + $q +
         ' /OUT:' + $q + (Join-Path $out 'MeltiaTip.dll') + $q + ' ' + $libs)
    ) -join ' && '
    Write-Host "T3a compiling $($arch): staged MeltiaTip.dll (NO REGISTRATION)"
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "T3a native build failed: $arch" }
    $dll = Join-Path $out 'MeltiaTip.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw "T3a DLL missing: $arch" }
    Write-Host "$arch SHA256=$((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)"
    # Run the real C++ epoch policy against reconnect / first-key scenarios.
    # This executable has no Windows APIs and is never shipped with the DLL.
    $fixtureSource = Join-Path $repo 'tools/test_meltia_t3b_reconnect.cpp'
    $fixtureExe = Join-Path $out 'MeltiaT3bReconnectTest.exe'
    $fixtureCmd = @(
        ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
        ('cd /d ' + $q + $staging + $q),
        ('cl /nologo /std:c++17 /EHsc /W4 /utf-8 /I' + $q + $staging + $q +
         ' /Fo' + $q + (Join-Path $obj 'MeltiaT3bReconnectTest.obj') + $q +
         ' /Fe' + $q + $fixtureExe + $q + ' ' + $q + $fixtureSource + $q)
    ) -join ' && '
    cmd /c $fixtureCmd
    if ($LASTEXITCODE -ne 0) { throw "T3b native reconnect fixture build failed: $arch" }
    & $fixtureExe
    if ($LASTEXITCODE -ne 0) { throw "T3b native reconnect fixture failed: $arch" }
    Write-Host "T3b reconnect policy PASS ($arch)"
}
Write-Host 'T3a no-registration native compilation succeeded; NOT INSTALLABLE.'
