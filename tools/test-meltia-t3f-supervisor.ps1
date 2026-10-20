# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
# T3f: no-registration Windows supervisor watchdog + bounded recovery integration.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('meltia-t3f-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$previousDataDir = [Environment]::GetEnvironmentVariable('MELTYPE_DATA_DIR', 'Process')
$env:MELTYPE_DATA_DIR = Join-Path $root 'data'
$processes = [System.Collections.ArrayList]::new()
$q = [char]34
try {
    $project = Join-Path $repo 'src/Meltia.TipHost.Experimental/Meltia.TipHost.Experimental.csproj'
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'T3f experimental host build failed.' }
    $dll = Join-Path $repo 'src/Meltia.TipHost.Experimental/bin/Release/net10.0-windows/Meltia.TipHost.Experimental.dll'
    if (-not (Test-Path $dll)) { throw 'T3f host assembly not found.' }

    function Start-Supervisor([string]$name) {
        $sandbox = Join-Path $root $name
        New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
        $stdout = Join-Path $sandbox 'stdout'
        $stderr = Join-Path $sandbox 'stderr'
        # Child inherits this sandbox, never the real Meltype data directory.
        $env:MELTYPE_DATA_DIR = Join-Path $sandbox 'data'
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList @(
            ($q + $dll + $q), '--supervise', ($q + $sandbox + $q)
        ) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        [void]$processes.Add($proc)
        return [pscustomobject]@{ Process=$proc; Sandbox=$sandbox; Status=(Join-Path $sandbox 'supervisor.status'); Stop=(Join-Path $sandbox 'supervisor.stop'); Stdout=$stdout; Stderr=$stderr }
    }

    function Wait-Generation($info, [int]$expected) {
        for ($i = 0; $i -lt 240; $i++) {
            if (Test-Path -LiteralPath $info.Status) {
                $value = Get-Content -LiteralPath $info.Status -Raw
                if ($value -match '^launch=(\d+);pid=(\d+)$') {
                    if ([int]$Matches[1] -eq $expected) {
                        $workerId = [int]$Matches[2]
                        $worker = Get-Process -Id $workerId -ErrorAction SilentlyContinue
                        if ($null -ne $worker -and -not $worker.HasExited) { return $workerId }
                    }
                }
            }
            if ($info.Process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
        if (Test-Path -LiteralPath $info.Stdout) { Get-Content $info.Stdout }
        if (Test-Path -LiteralPath $info.Stderr) { Get-Content $info.Stderr }
        throw "T3f supervisor failed to reach generation $expected."
    }

    function Verify-RealIPC() {
        & dotnet $dll --probe
        if ($LASTEXITCODE -ne 0) { throw 'T3f expected real TipServer/Session transaction.' }
    }

    function Kill-Worker([int]$workerId) {
        $p = Get-Process -Id $workerId -ErrorAction Stop
        $p.Kill()
        if (-not $p.WaitForExit(10000)) { throw "T3f worker $workerId did not terminate." }
        $p.Dispose()
    }

    # Disabled by default; a non-sandbox path is not a valid supervisor target.
    & dotnet $dll --supervise $repo
    if ($LASTEXITCODE -ne 2) { throw 'T3f must reject repository checkout as supervisor sandbox.' }

    $a = Start-Supervisor 'watchdog'
    $a1 = Wait-Generation $a 1
    Verify-RealIPC

    # A second supervisor for the same SID must fail without disturbing A.
    $duplicate = Start-Supervisor 'duplicate'
    if (-not $duplicate.Process.WaitForExit(8000) -or $duplicate.Process.ExitCode -ne 3) {
        throw 'T3f did not reject the duplicate supervisor.'
    }
    Verify-RealIPC

    # The child process stays alive but its real pipe listener is cancelled.
    # A heartbeat-only probe must notice the loss and trigger a new generation.
    Set-Content -LiteralPath (Join-Path $a.Sandbox 'attempt-1/health.drop') -Value 'drop'
    $a2 = Wait-Generation $a 2
    if ($a2 -eq $a1) { throw 'T3f did not replace the unhealthy worker.' }
    if (Get-Process -Id $a1 -ErrorAction SilentlyContinue) { throw 'T3f left failed worker alive.' }
    Verify-RealIPC

    # Crash the supervised worker; restart with a fresh isolated session.
    Kill-Worker $a2
    $a3 = Wait-Generation $a 3
    if ($a3 -eq $a2) { throw 'T3f did not recover from worker crash.' }
    Verify-RealIPC

    # An intentional stop must never use the last worker as a reason to relaunch.
    Set-Content -LiteralPath $a.Stop -Value 'stop'
    if (-not $a.Process.WaitForExit(12000) -or $a.Process.ExitCode -ne 0) {
        if (Test-Path $a.Stderr) { Get-Content $a.Stderr }
        throw 'T3f intentional shutdown failed.'
    }
    if (Get-Process -Id $a3 -ErrorAction SilentlyContinue) { throw 'T3f did not clean up its child.' }
    & dotnet $dll --probe
    if ($LASTEXITCODE -eq 0) { throw 'T3f endpoint remained accessible after supervisor stop.' }

    # A separate fresh supervisor must NOT enter an infinite restart storm.
    $b = Start-Supervisor 'budget'
    foreach ($i in @(1, 2, 3)) {
        $workerId = Wait-Generation $b $i
        Kill-Worker $workerId
    }
    if (-not $b.Process.WaitForExit(12000) -or $b.Process.ExitCode -ne 6) {
        if (Test-Path $b.Stderr) { Get-Content $b.Stderr }
        throw 'T3f must terminate with code 6 after the third failed launch.'
    }
    Write-Host 'PASS T3f: real hello health, listener loss, process crash, duplicate refusal, deliberate stop, bounded recovery.'
}
finally {
    foreach ($proc in $processes) {
        if (-not $proc.HasExited) {
            $proc.Kill()
            $proc.WaitForExit()
        }
        $proc.Dispose()
    }
    [Environment]::SetEnvironmentVariable('MELTYPE_DATA_DIR', $previousDataDir, 'Process')
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
