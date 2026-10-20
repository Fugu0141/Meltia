// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Tip;

namespace Meltia.TipHost.Experimental;

/// <summary>
/// No-registration process lifecycle experiment. Does not install hooks, activate
/// any Windows IME, save settings or share the Meltype/Keyboard Preview mutex.
/// </summary>
internal static class OfflineHost
{
    internal static int Run(string readyMarker, string stopMarker, string? dropListenerMarker = null)
    {
        var dataDirectory = Environment.GetEnvironmentVariable("MELTYPE_DATA_DIR");
        // This binary is an explicit CI-only experiment. Never fall back to the
        // actual Meltype data directory when the test harness forgot its sandbox.
        if (string.IsNullOrWhiteSpace(dataDirectory) ||
            !UnderTemp(dataDirectory) || !UnderTemp(readyMarker) || !UnderTemp(stopMarker) ||
            (dropListenerMarker is not null && !UnderTemp(dropListenerMarker)))
        {
            Console.Error.WriteLine("T3e requires sandbox paths below the OS temporary directory.");
            return 2;
        }

        var user = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(user))
        {
            Console.Error.WriteLine("T3e cannot establish the current user SID.");
            return 2;
        }

        using var mutex = new Mutex(false, @"Local\Meltia.TipHost.Experimental." + user);
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex)
        {
            Console.Error.WriteLine("T3e host is already running for this user.");
            return 3;
        }

        try
        {
            Directory.CreateDirectory(dataDirectory);
            var settings = new Settings
            {
                Enabled = true,
                Mode = InputMode.Tsf, // Memory-only; no OS profile is registered or selected.
                AutoUpdate = false,
            };
            var detector = CompositionDetector.CreateDefault(Path.Combine(dataDirectory, "dictionaries"));
            var converter = new NullConverter(); // Lifecycle gate, not dictionary quality.
            var options = new CompositionOptions
            {
                LiveConversion = () => false,
                AutoCorrect = () => false,
                DirectMode = () => false,
            };

            using var invoker = new Control();
            _ = invoker.Handle; // Create on the STA before any pipe callbacks.
            using var server = new TipServer(invoker, () => settings,
                () => new MeltypeSession(detector, converter, options, () => settings),
                meltiaExperimental: true);
            server.Start();
            // Start() alone is not proof of readiness. Do not write the marker
            // until the real first pipe instance has been bound successfully.
            var listening = server.FirstListenerReady.WaitAsync(TimeSpan.FromSeconds(10))
                .GetAwaiter().GetResult();
            if (!listening)
            {
                Console.Error.WriteLine("T3e failed to bind the exclusive pipe listener.");
                return 4;
            }

            using var ticker = new System.Windows.Forms.Timer { Interval = 100 };
            var timedOut = false;
            var listenerDropped = false;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            ticker.Tick += (_, _) =>
            {
                if (File.Exists(stopMarker)) Application.ExitThread();
                else if (!listenerDropped && dropListenerMarker is not null && File.Exists(dropListenerMarker))
                {
                    // T3f fault injection only: leave the process alive but stop IPC.
                    // No actual OS IME or keyboard input is ever involved.
                    listenerDropped = true;
                    server.Dispose();
                }
                else if (elapsed.Elapsed >= TimeSpan.FromSeconds(60))
                {
                    timedOut = true;
                    Application.ExitThread();
                }
            };

            File.WriteAllText(readyMarker, "bound", Encoding.UTF8);
            ticker.Start();
            Application.Run(new ApplicationContext());
            ticker.Stop();

            if (timedOut)
            {
                Console.Error.WriteLine("T3e offline host exceeded its test lifetime.");
                return 5;
            }

            Console.WriteLine("PASS T3e host: stopped without registering TSF or installing hooks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL T3e offline host: " + ex);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    internal static bool UnderTemp(string path)
    {
        try
        {
            var root = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            // A marker / directory must be strictly beneath Temp (never Temp itself).
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                full.Length > root.Length;
        }
        catch { return false; }
    }

    private sealed class NullConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => null;
    }
}
