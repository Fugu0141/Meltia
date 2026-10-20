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

    function Start-Supervisor([string]$name) {
        $sandbox = Join-Path $root $name
        New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
        $env:MELTYPE_DATA_DIR = Join-Path $sandbox 'data'
        $stdout = Join-Path $sandbox 'stdout'
        $stderr = Join-Path $sandbox 'stderr'
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList @(
            ($q + $dll + $q), '--supervise', ($q + $sandbox + $q)
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
    # Windows must close the parent's unnamed Job handle and reap its worker.
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
    if ($replacement.Id -eq $ownedWorker.Id) {
        throw 'T3g reused previous worker PID unexpectedly.'
    }
    Verify-RealIPC
    Set-Content -LiteralPath $second.Stop -Value 'stop'
    if (-not $second.Process.WaitForExit(12000) -or $second.Process.ExitCode -ne 0) {
        if (Test-Path $second.Stderr) { Get-Content $second.Stderr }
        throw 'T3g normal supervisor stop failed.'
    }
    if (-not $replacement.WaitForExit(10000)) {
        throw 'T3g normal stop left the worker alive.'
    }

    # A 5-argument worker started without a parent Job assignment must never
    # bind a listener or announce ready. Its gate expires in 10s (exit code 7).
    $ungatedRoot = Join-Path $root 'ungated'
    New-Item -ItemType Directory -Path $ungatedRoot -Force | Out-Null
    $env:MELTYPE_DATA_DIR = Join-Path $ungatedRoot 'data'
    $ready = Join-Path $ungatedRoot 'ready'
    $stop = Join-Path $ungatedRoot 'stop'
    $drop = Join-Path $ungatedRoot 'drop'
    $gate = Join-Path $ungatedRoot 'start.allowed'
    $ungated = Start-Process -FilePath 'dotnet' -ArgumentList @(
        ($q + $dll + $q), '--offline-host', ($q + $ready + $q),
        ($q + $stop + $q), ($q + $drop + $q), ($q + $gate + $q)
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
    $global:LASTEXITCODE = 0
    Write-Host 'PASS T3g: real supervisor kill reaped owned worker, new host recovered, ungated worker fail-closed.'
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
