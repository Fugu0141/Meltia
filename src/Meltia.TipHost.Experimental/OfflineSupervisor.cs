// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T3f: explicit, short-lived Windows TEST supervisor. Never install or activate TSF.
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace Meltia.TipHost.Experimental;

/// <summary>
/// Supervises only child processes that this instance created. No Windows service,
/// auto-start, hooks, persisted state, or attempt to adopt an unrelated process.
/// </summary>
internal static class OfflineSupervisor
{
    private const int MaxLaunches = 3;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(300);

    internal static int Run(string sandbox)
    {
        var data = Environment.GetEnvironmentVariable("MELTYPE_DATA_DIR");
        if (!Directory.Exists(sandbox) || !OfflineHost.UnderTemp(sandbox) ||
            string.IsNullOrWhiteSpace(data) || !OfflineHost.UnderTemp(data) ||
            !Path.GetFullPath(data).StartsWith(
                Path.GetFullPath(sandbox).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("T3f requires an existing temporary sandbox and nested MELTYPE_DATA_DIR.");
            return 2;
        }

        var user = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(user)) return 2;
        using var exclusive = new Mutex(false, @"Local\Meltia.TipHost.Experimental.Supervisor." + user);
        bool owns;
        try { owns = exclusive.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            Console.Error.WriteLine("T3f supervisor already running for this SID.");
            return 3;
        }

        try { return RunOwned(sandbox); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL T3f supervisor: " + ex);
            return 1;
        }
        finally { exclusive.ReleaseMutex(); }
    }

    private static int RunOwned(string sandbox)
    {
        var requestedStop = Path.Combine(sandbox, "supervisor.stop");
        var status = Path.Combine(sandbox, "supervisor.status");
        if (File.Exists(requestedStop) || File.Exists(status))
        {
            // The caller must prepare a fresh directory. Never inherit stale
            // stop/ready/PID state from an earlier supervision run.
            Console.Error.WriteLine("T3f refuses a sandbox with existing supervisor markers.");
            return 2;
        }

        for (var launch = 1; launch <= MaxLaunches; launch++)
        {
            if (File.Exists(requestedStop)) return 0;
            var attemptDir = Path.Combine(sandbox, "attempt-" + launch);
            Directory.CreateDirectory(attemptDir);
            var ready = Path.Combine(attemptDir, "ready");
            var stop = Path.Combine(attemptDir, "stop");
            var drop = Path.Combine(attemptDir, "health.drop");
            using var child = Launch(ready, stop, drop);
            try
            {
                var deadline = Stopwatch.StartNew();
                bool bound = false;
                while (deadline.Elapsed < StartupTimeout && !File.Exists(requestedStop))
                {
                    if (child.HasExited) break;
                    if (File.Exists(ready))
                    {
                        // A ready marker alone is not sufficient: prove that the
                        // actual v1 server answers a new independently checked hello.
                        if (OfflineProbe.IsHealthy())
                        {
                            bound = true;
                            break;
                        }
                    }
                    Thread.Sleep(100);
                }

                if (File.Exists(requestedStop)) return 0;
                if (!bound)
                {
                    Console.Error.WriteLine($"T3f worker did not become healthy (launch {launch}).");
                }
                else
                {
                    // A temp-file + rename prevents readers seeing a partial PID.
                    PublishStatus(status, launch, child.Id);
                    Console.WriteLine($"T3f healthy worker launch={launch} pid={child.Id}.");
                    var healthFailures = 0;
                    while (!File.Exists(requestedStop) && !child.HasExited)
                    {
                        Thread.Sleep(MonitorInterval);
                        if (File.Exists(requestedStop) || child.HasExited) break;
                        if (OfflineProbe.IsHealthy()) healthFailures = 0;
                        else if (++healthFailures >= 2)
                        {
                            Console.Error.WriteLine("T3f worker failed consecutive v1 health checks.");
                            break;
                        }
                    }
                }

                if (File.Exists(requestedStop))
                {
                    StopChild(child, stop);
                    Console.WriteLine("PASS T3f supervisor: intentional stop; no restart.");
                    return 0;
                }
            }
            finally
            {
                StopChild(child, stop);
            }

            // All retries are deliberately bounded. Never run indefinitely
            // after a corrupt/stuck worker or an endpoint ownership conflict.
            if (launch == MaxLaunches)
            {
                Console.Error.WriteLine("T3f recovery budget exhausted after three launches.");
                return 6;
            }
            if (File.Exists(requestedStop)) return 0;
            // Allow terminated Windows pipe handles to drain before rebinding.
            Thread.Sleep(TimeSpan.FromMilliseconds(750 * launch));
        }
        return 6;
    }

    private static Process Launch(string ready, string stop, string drop)
    {
        var runtime = Environment.ProcessPath ??
            throw new InvalidOperationException("Missing .NET runtime executable.");
        var assembly = typeof(OfflineSupervisor).Assembly.Location;
        if (string.IsNullOrEmpty(assembly))
            throw new InvalidOperationException("The T3f experiment requires a file-backed assembly.");

        var start = new ProcessStartInfo(runtime) { UseShellExecute = false };
        // CI invokes this project through 'dotnet <assembly>'. It is not a
        // self-contained product executable.
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--offline-host");
        start.ArgumentList.Add(ready);
        start.ArgumentList.Add(stop);
        start.ArgumentList.Add(drop);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start T3f worker.");
    }

    private static void PublishStatus(string file, int launch, int pid)
    {
        var temp = file + ".new";
        File.WriteAllText(temp, $"launch={launch};pid={pid}", Encoding.ASCII);
        File.Move(temp, file, overwrite: true);
    }

    private static void StopChild(Process child, string stopMarker)
    {
        if (child.HasExited) return;
        File.WriteAllText(stopMarker, "stop", Encoding.ASCII);
        if (child.WaitForExit(4000)) return;
        child.Kill();
        child.WaitForExit(4000);
    }
}
