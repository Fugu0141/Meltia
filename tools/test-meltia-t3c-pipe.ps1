# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T3c-A: real Win32 PipeClient <-> .NET NamedPipeServerStream. NO TSF registration.
param([ValidateSet('Both', 'x64', 'x86')][string]$Architecture = 'Both')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t3c-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$stage = Join-Path $temp 'stage'
$server = $null
try {
    python (Join-Path $repo 'tools/prepare_meltia_tip_stage.py') --output $stage
    if ($LASTEXITCODE -ne 0) { throw 'T3c isolated native source staging failed.' }
    if (-not (Test-Path (Join-Path $stage 'STAGING_NOTICE.txt'))) {
        throw 'Missing no-registration stage marker.'
    }
    dotnet build (Join-Path $repo 'src/Meltia.TipIpcFixture/Meltia.TipIpcFixture.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'T3c managed fixture build failed.' }
    $managedDll = Join-Path $repo 'src/Meltia.TipIpcFixture/bin/Release/net10.0-windows/Meltia.TipIpcFixture.dll'
    if (-not (Test-Path $managedDll)) { throw 'T3c managed fixture DLL missing.' }

    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Visual Studio Build Tools not found.' }
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual C++ x64/x86 toolchain not found.' }
    $vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
    $targets = if ($Architecture -eq 'Both') { @('x64', 'x86') } else { @($Architecture) }
    $q = [char]34

    foreach ($arch in $targets) {
        $bin = Join-Path $temp $arch
        New-Item -ItemType Directory -Path $bin | Out-Null
        $clientExe = Join-Path $bin 'MeltiaT3cPipeClient.exe'
        $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
        $fixture = Join-Path $repo 'tools/test_meltia_t3c_native_pipe.cpp'
        $pipeCpp = Join-Path $stage 'Pipe.cpp'
        $compile = @(
            ($q + $vcvars + $q + ' ' + $vcarch + ' >nul'),
            ('cd /d ' + $q + $stage + $q),
            ('cl /nologo /std:c++17 /EHsc /W4 /utf-8 /DUNICODE /D_UNICODE /I' + $q + $stage + $q +
             ' /Fe:' + $q + $clientExe + $q + ' ' +
             $q + $fixture + $q + ' ' + $q + $pipeCpp + $q + ' /link advapi32.lib')
        ) -join ' && '
        cmd /c $compile
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $clientExe)) {
            throw "T3c Win32 PipeClient compile failed: $arch"
        }

        $ready = Join-Path $bin 'ready.txt'
        $stdout = Join-Path $bin 'server.stdout.txt'
        $stderr = Join-Path $bin 'server.stderr.txt'
        $server = Start-Process -FilePath 'dotnet' -ArgumentList @(
            ($q + $managedDll + $q), ($q + $ready + $q)
        ) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $didStart = $false
        for ($i = 0; $i -lt 120; $i++) {
            if (Test-Path $ready) { $didStart = $true; break }
            if ($server.HasExited) { break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $didStart) {
            if (Test-Path $stderr) { Get-Content $stderr }
            throw "T3c C# Named Pipe host did not bind: $arch"
        }

        try {
            & $clientExe
            if ($LASTEXITCODE -ne 0) { throw "T3c native client failed: $arch" }
            if (-not $server.WaitForExit(15000)) {
                throw "T3c managed server did not finish: $arch"
            }
            if ($server.ExitCode -ne 0) {
                if (Test-Path $stderr) { Get-Content $stderr }
                throw "T3c managed verification failed: $arch"
            }
            Get-Content $stdout
            Write-Host "PASS T3c-A cross-process IPC: $arch (no registration)"
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
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
