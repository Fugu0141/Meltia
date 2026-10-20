# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T3e: Windows process lifecycle with real experimental TipServer, never TSF-register or hook.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t3e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$priorData = [Environment]::GetEnvironmentVariable('MELTYPE_DATA_DIR', 'Process')
$env:MELTYPE_DATA_DIR = Join-Path $temp 'isolated-data'
$processes = [System.Collections.ArrayList]::new()
$q = [char]34
try {
    $project = Join-Path $repo 'src/Meltia.TipHost.Experimental/Meltia.TipHost.Experimental.csproj'
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'T3e experimental host failed to build.' }
    $dll = Join-Path $repo 'src/Meltia.TipHost.Experimental/bin/Release/net10.0-windows/Meltia.TipHost.Experimental.dll'
    if (-not (Test-Path $dll)) { throw 'T3e experimental host DLL is missing.' }

    function Start-OfflineHost([string]$name) {
        $ready = Join-Path $temp ($name + '.ready')
        $stop = Join-Path $temp ($name + '.stop')
        $stdout = Join-Path $temp ($name + '.stdout')
        $stderr = Join-Path $temp ($name + '.stderr')
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList @(
            ($q + $dll + $q), '--offline-host', ($q + $ready + $q), ($q + $stop + $q)
        ) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        [void]$processes.Add($proc)
        return [pscustomobject]@{ Name = $name; Process = $proc; Ready = $ready; Stop = $stop; Error = $stderr }
    }

    function Wait-Ready($hostInfo) {
        for ($i = 0; $i -lt 80; $i++) {
            if (Test-Path -LiteralPath $hostInfo.Ready) {
                if ($hostInfo.Process.HasExited) { throw 'T3e ready marker exists but process exited.' }
                return
            }
            if ($hostInfo.Process.HasExited) { break }
            Start-Sleep -Milliseconds 125
        }
        if (Test-Path $hostInfo.Error) { Get-Content $hostInfo.Error }
        throw "T3e $($hostInfo.Name) did not bind the actual listener."
    }

    function Probe-Host() {
        & dotnet $dll --probe
        if ($LASTEXITCODE -ne 0) { throw 'T3e real TipServer IPC probe failed.' }
    }

    function Stop-OfflineHost($hostInfo) {
        Set-Content -LiteralPath $hostInfo.Stop -Value 'stop' -Encoding utf8
        if (-not $hostInfo.Process.WaitForExit(10000)) {
            throw "T3e $($hostInfo.Name) did not stop gracefully."
        }
        if ($hostInfo.Process.ExitCode -ne 0) {
            if (Test-Path $hostInfo.Error) { Get-Content $hostInfo.Error }
            throw "T3e $($hostInfo.Name) returned exit code $($hostInfo.Process.ExitCode)."
        }
    }

    # Unspecified launches must never start an IME or create a pipe.
    & dotnet $dll
    if ($LASTEXITCODE -ne 2) { throw 'T3e ordinary execution must be refused.' }

    $a = Start-OfflineHost 'first'
    Wait-Ready $a
    Probe-Host

    # Separate process, same user: must reject duplicate startup without ready marker.
    $b = Start-OfflineHost 'duplicate'
    if (-not $b.Process.WaitForExit(7000)) { throw 'T3e duplicate host failed to reject promptly.' }
    if ($b.Process.ExitCode -ne 3 -or (Test-Path $b.Ready)) {
        throw "T3e duplicate host unexpectedly started: exit=$($b.Process.ExitCode)."
    }
    Probe-Host  # First host was not disturbed by the rejected process.
    Stop-OfflineHost $a

    # Check the binding is truly gone before normal restart (probe must fail).
    & dotnet $dll --probe
    if ($LASTEXITCODE -eq 0) { throw 'T3e listener remained alive after shutdown.' }

    $c = Start-OfflineHost 'before-crash'
    Wait-Ready $c
    Probe-Host
    $c.Process.Kill()
    if (-not $c.Process.WaitForExit(10000)) { throw 'T3e simulated crash did not terminate.' }

    # Windows releases both the per-user mutex and the pipe upon process death.
    # Restart uses the SAME pipe identity, but creates a NEW server/session state.
    $d = Start-OfflineHost 'after-crash'
    Wait-Ready $d
    Probe-Host
    Stop-OfflineHost $d

    Write-Host 'PASS T3e: start, real IPC, duplicate rejected, graceful stop, crash and restart.'
}
finally {
    foreach ($proc in $processes) {
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit() }
        $proc.Dispose()
    }
    [Environment]::SetEnvironmentVariable('MELTYPE_DATA_DIR', $priorData, 'Process')
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
