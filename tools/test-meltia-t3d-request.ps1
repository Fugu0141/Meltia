# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T3d: actual staged TextService::Request against the test-only managed host.
# The Windows registry/TSF/keyboard hooks are NEVER touched.
param([ValidateSet('Both', 'x64', 'x86')][string]$Architecture = 'Both')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t3d-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$previousDataDir = [Environment]::GetEnvironmentVariable('MELTYPE_DATA_DIR', 'Process')
$env:MELTYPE_DATA_DIR = Join-Path $temp 'isolated-data'
$server = $null
try {
    $stage = Join-Path $temp 'stage'
    python (Join-Path $repo 'tools/prepare_meltia_tip_stage.py') --output $stage
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $stage 'STAGING_NOTICE.txt'))) {
        throw 'T3d isolated native staging failed.'
    }
    $proj = Join-Path $repo 'src/Meltia.TipHost.Tests/Meltia.TipHost.Tests.csproj'
    dotnet build $proj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'T3d managed test host build failed.' }
    $hostDll = Join-Path $repo 'src/Meltia.TipHost.Tests/bin/Release/net10.0-windows/Meltia.TipHost.Tests.dll'
    if (-not (Test-Path $hostDll)) { throw 'T3d managed test host DLL missing.' }

    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio Build Tools not found.' }
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual C++ x64/x86 toolchain not found.' }
    $vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
    $targets = if ($Architecture -eq 'Both') { @('x64', 'x86') } else { @($Architecture) }
    $q = [char]34
    $sources = @('TextService.cpp', 'CandidateWindow.cpp', 'DisplayAttributes.cpp',
                 'LangBar.cpp', 'Pipe.cpp', 'Json.cpp')
    $libraries = 'ole32.lib oleaut32.lib uuid.lib user32.lib gdi32.lib advapi32.lib d2d1.lib dwrite.lib dwmapi.lib'

    foreach ($arch in $targets) {
        $bin = Join-Path $temp $arch
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        $exe = Join-Path $bin 'MeltiaT3dRequest.exe'
        $fixture = Join-Path $repo 'tools/test_meltia_t3d_textservice_request.cpp'
        $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
        $compile = @(
            ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
            ('cd /d ' + $q + $bin + $q),
            ('cl /nologo /std:c++17 /EHsc /W4 /wd4100 /utf-8 /DUNICODE /D_UNICODE ' +
             '/DMELTIA_T3D_REQUEST_TEST /I' + $q + $stage + $q +
             ' /Fe:' + $q + $exe + $q + ' ' + $q + $fixture + $q + ' ' +
             (($sources | ForEach-Object { $q + (Join-Path $stage $_) + $q }) -join ' ') +
             ' /link ' + $libraries)
        ) -join ' && '
        cmd /c $compile
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) {
            throw "T3d actual native TextService compile failed: $arch"
        }

        $ready = Join-Path $bin 'ready.txt'
        $done = Join-Path $bin 'done.txt'
        $stdout = Join-Path $bin 'host.stdout.txt'
        $stderr = Join-Path $bin 'host.stderr.txt'
        $server = Start-Process -FilePath 'dotnet' -ArgumentList @(
            ($q + $hostDll + $q), ($q + $ready + $q), ($q + $done + $q), '2'
        ) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $started = $false
        for ($i = 0; $i -lt 120; $i++) {
            if (Test-Path -LiteralPath $ready) { $started = $true; break }
            if ($server.HasExited) { break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $started) {
            if (Test-Path $stderr) { Get-Content $stderr }
            throw "T3d managed host did not start: $arch"
        }
        try {
            & $exe
            if ($LASTEXITCODE -ne 0) { throw "T3d native Request E2E failed: $arch" }
            Set-Content -LiteralPath $done -Value done -Encoding utf8
            if (-not $server.WaitForExit(15000)) { throw "T3d managed host did not exit: $arch" }
            if ($server.ExitCode -ne 0) {
                if (Test-Path $stderr) { Get-Content $stderr }
                throw "T3d server session count mismatch: $arch"
            }
            Get-Content $stdout
            Write-Host "PASS T3d native TextService::Request (no TSF registration): $arch"
        }
        finally {
            if ($null -ne $server -and -not $server.HasExited) {
                $server.Kill()
                $server.WaitForExit()
            }
            $server.Dispose()
            $server = $null
        }
    }
}
finally {
    if ($null -ne $server) {
        if (-not $server.HasExited) { $server.Kill(); $server.WaitForExit() }
        $server.Dispose()
    }
    [Environment]::SetEnvironmentVariable('MELTYPE_DATA_DIR', $previousDataDir, 'Process')
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
