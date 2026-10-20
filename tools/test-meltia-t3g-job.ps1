# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T3g: real Windows Job Object supervisor-death containment (NO TSF registration).
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t3g-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$previousDataDir = [Environment]::GetEnvironmentVariable('MELTYPE_DATA_DIR', 'Process')
$processes = [System.Collections.ArrayList]::new()
$workers = [System.Collections.ArrayList]::new()
$q = [char]34
try {
    $project = Join-Path $repo 'src/Meltia.TipHost.Experimental/Meltia.TipHost.Experimental.csproj'
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'T3g experimental host build failed.' }
    $dll = Join-Path $repo 'src/Meltia.TipHost.Experimental/bin/Release/net10.0-windows/Meltia.TipHost.Experimental.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'T3g test host assembly missing.' }

    function Start-Supervisor([string]$name, [switch]$InjectAssignFailure) {
        $sandbox = Join-Path $root $name
        New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
        $env:MELTYPE_DATA_DIR = Join-Path $sandbox 'data'
        $stdout = Join-Path $sandbox 'stdout'
        $stderr = Join-Path $sandbox 'stderr'
        $mode = if ($InjectAssignFailure) { '--supervise-test-assign-failure' } else { '--supervise' }
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList @(
            ($q + $dll + $q), $mode, ($q + $sandbox + $q)
        ) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        [void]$processes.Add($proc)
        return [pscustomobject]@{ Process=$proc; Sandbox=$sandbox; Status=(Join-Path $sandbox 'supervisor.status'); Stop=(Join-Path $sandbox 'supervisor.stop'); Stdout=$stdout; Stderr=$stderr }
    }

    function Wait-Worker($info) {
        for ($i = 0; $i -lt 240; $i++) {
            if (Test-Path -LiteralPath $info.Status) {
                $value = Get-Content -LiteralPath $info.Status -Raw
                if ($value -match '^launch=(\d+);pid=(\d+)$') {
                    $workerId = [int]$Matches[2]
                    $worker = Get-Process -Id $workerId -ErrorAction SilentlyContinue
                    if ($null -ne $worker -and -not $worker.HasExited) {
                        [void]$workers.Add($worker)
                        return $worker
                    }
                }
            }
            if ($info.Process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
        if (Test-Path -LiteralPath $info.Stdout) { Get-Content -LiteralPath $info.Stdout }
        if (Test-Path -LiteralPath $info.Stderr) { Get-Content -LiteralPath $info.Stderr }
        throw 'T3g supervisor did not publish a healthy, job-assigned worker.'
    }

    function Verify-RealIPC {
        & dotnet $dll --probe
        if ($LASTEXITCODE -ne 0) { throw 'T3g real v1 TipServer / Session probe failed.' }
    }

    $first = Start-Supervisor 'parent-killed'
    $ownedWorker = Wait-Worker $first
    Verify-RealIPC

    # Kill only the supervisor; DO NOT kill the worker from the harness.
    # Windows must close the parent's per-launch named Job handle and reap its worker.
    $first.Process.Kill()
    if (-not $first.Process.WaitForExit(10000)) {
        throw 'T3g killed supervisor did not exit.'
    }
    if (-not $ownedWorker.WaitForExit(10000)) {
        throw "T3g orphan worker remained alive after supervisor kill (PID $($ownedWorker.Id))."
    }
    & dotnet $dll --probe 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'T3g endpoint is still reachable after supervisor death.' }

    # The OS released the worker mutex and named pipe; no adoption of the old PID.
    $second = Start-Supervisor 'after-parent-kill'
    $replacement = Wait-Worker $second
    # PID reuse by the OS is legal; the old Process handle has already exited.
    Verify-RealIPC
    Set-Content -LiteralPath $second.Stop -Value 'stop'
    if (-not $second.Process.WaitForExit(12000) -or $second.Process.ExitCode -ne 0) {
        if (Test-Path $second.Stderr) { Get-Content $second.Stderr }
        throw 'T3g normal supervisor stop failed.'
    }
    if (-not $replacement.WaitForExit(10000)) {
        throw 'T3g normal stop left the worker alive.'
    }

    # A supervisor-only worker with no post-assignment marker must time out
    # before opening its listener (exit code 7).
    $ungatedRoot = Join-Path $root 'ungated'
    New-Item -ItemType Directory -Path $ungatedRoot -Force | Out-Null
    $env:MELTYPE_DATA_DIR = Join-Path $ungatedRoot 'data'
    $ready = Join-Path $ungatedRoot 'ready'
    $stop = Join-Path $ungatedRoot 'stop'
    $drop = Join-Path $ungatedRoot 'drop'
    $gate = Join-Path $ungatedRoot 'start.allowed'
    $ungated = Start-Process -FilePath 'dotnet' -ArgumentList @(
        ($q + $dll + $q), '--offline-host', ($q + $ready + $q),
        ($q + $stop + $q), ($q + $drop + $q), ($q + $gate + $q),
        ($q + 'Local\Meltia.TipHost.Experimental.Worker.fakejob' + $q)
    ) -PassThru -RedirectStandardOutput (Join-Path $ungatedRoot 'stdout') -RedirectStandardError (Join-Path $ungatedRoot 'stderr')
    [void]$processes.Add($ungated)
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $ready) {
        throw 'T3g ungated worker announced ready before Job assignment.'
    }
    if (-not $ungated.WaitForExit(16000) -or $ungated.ExitCode -ne 7 -or
        (Test-Path -LiteralPath $ready)) {
        throw "T3g ungated worker did not time out without binding: exit=$($ungated.ExitCode)."
    }
    # P2 regression: a pre-created start gate is not proof of containment.
    # The requested Job Object does not exist, and the worker must reject it.
    $spoofRoot = Join-Path $root 'spoofed-gate'
    New-Item -ItemType Directory -Path $spoofRoot -Force | Out-Null
    $env:MELTYPE_DATA_DIR = Join-Path $spoofRoot 'data'
    $spoofReady = Join-Path $spoofRoot 'ready'
    $spoofStop = Join-Path $spoofRoot 'stop'
    $spoofDrop = Join-Path $spoofRoot 'drop'
    $spoofGate = Join-Path $spoofRoot 'start.allowed'
    Set-Content -LiteralPath $spoofGate -Value 'assigned'
    $spoofJob = 'Local\Meltia.TipHost.Experimental.Worker.' + [Guid]::NewGuid().ToString('N')
    $spoof = Start-Process -FilePath 'dotnet' -ArgumentList @(
        ($q + $dll + $q), '--offline-host', ($q + $spoofReady + $q),
        ($q + $spoofStop + $q), ($q + $spoofDrop + $q), ($q + $spoofGate + $q),
        ($q + $spoofJob + $q)
    ) -PassThru -RedirectStandardOutput (Join-Path $spoofRoot 'stdout') -RedirectStandardError (Join-Path $spoofRoot 'stderr')
    [void]$processes.Add($spoof)
    if (-not $spoof.WaitForExit(8000) -or $spoof.ExitCode -ne 7 -or
        (Test-Path -LiteralPath $spoofReady)) {
        throw 'T3g accepted a spoofed gate without the expected Job Object membership.'
    }
    & dotnet $dll --probe 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'T3g ungated worker unexpectedly opened the real pipe.' }

    # P2 regression: inject failure after Process.Start but before Assign.
    # Supervisor must fail closed, reap that *specific* worker and never bind IPC.
    $failed = Start-Supervisor 'assign-failed' -InjectAssignFailure
    if (-not $failed.Process.WaitForExit(12000) -or $failed.Process.ExitCode -eq 0) {
        throw 'T3g supervisor did not report injected assignment failure.'
    }
    $pidMarker = Join-Path $failed.Sandbox 'attempt-1/worker.started'
    if (-not (Test-Path -LiteralPath $pidMarker)) {
        throw 'T3g failure injection did not launch and record its worker.'
    }
    $failedWorkerId = [int](Get-Content -LiteralPath $pidMarker -Raw)
    $failedWorker = Get-Process -Id $failedWorkerId -ErrorAction SilentlyContinue
    if ($null -ne $failedWorker -and -not $failedWorker.HasExited) {
        throw "T3g failed assignment left a worker alive: PID $failedWorkerId."
    }
    if (Test-Path -LiteralPath (Join-Path $failed.Sandbox 'attempt-1/ready')) {
        throw 'T3g unassigned worker advertised its listener as ready.'
    }
    if (Test-Path -LiteralPath $failed.Status) {
        throw 'T3g failed assignment incorrectly published healthy status.'
    }
    & dotnet $dll --probe 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'T3g assignment failure left the Pipe reachable.' }
    $global:LASTEXITCODE = 0
    Write-Host 'PASS T3g: supervisor kill reaps worker; spoofed gate and assignment failure fail closed.'
}
finally {
    foreach ($proc in $processes) {
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit() }
        $proc.Dispose()
    }
    foreach ($worker in $workers) {
        if (-not $worker.HasExited) { $worker.Kill(); $worker.WaitForExit() }
        $worker.Dispose()
    }
    [Environment]::SetEnvironmentVariable('MELTYPE_DATA_DIR', $previousDataDir, 'Process')
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
