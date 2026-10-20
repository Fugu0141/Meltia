// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Runs real TipServer.Serve / Handle with real MeltypeSession, no IME registration.
// The process is exclusively a Windows test fixture, never a production host.
using System.Text;
using System.Windows.Forms;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Tip;

namespace Meltia.TipHost.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || (args.Length != 2 && args.Length != 3) ||
            Environment.GetEnvironmentVariable("MELTYPE_DATA_DIR") is not { Length: > 0 } isolatedDir)
        {
            Console.Error.WriteLine("T3c-B/T3d requires <ready> <done> [expected-sessions] and isolated MELTYPE_DATA_DIR.");
            return 2;
        }

        try
        {
            // Keep T3c-B's 3-session expectation; T3d uses 2 across reconnect.
            var expectedSessions = args.Length == 3 ? int.Parse(args[2]) : 3;
            if (expectedSessions is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(args));
            // Deliberately memory-only. Tsf is required to exercise TipServer.Active,
            // but the fixture does not register/activate TSF, install hooks, or persist settings.
            var settings = new Settings { Enabled = true, Mode = InputMode.Tsf, AutoUpdate = false };
            var detector = CompositionDetector.CreateDefault(Path.Combine(isolatedDir, "dictionaries"));
            var converter = new NullConverter();
            var options = new CompositionOptions
            {
                LiveConversion = () => false,
                AutoCorrect = () => false,
                DirectMode = () => false,
            };
            var sessionCount = 0;

            using var invoker = new Control();
            _ = invoker.Handle; // Create on this STA before starting workers.
            using var server = new TipServer(invoker, () => settings, () =>
            {
                sessionCount++;
                return new MeltypeSession(detector, converter, options, () => settings);
            }, meltiaExperimental: true);

            using var ticker = new System.Windows.Forms.Timer { Interval = 100 };
            var timedOut = false;
            var started = System.Diagnostics.Stopwatch.StartNew();
            ticker.Tick += (_, _) =>
            {
                if (File.Exists(args[1])) Application.ExitThread();
                else if (started.Elapsed > TimeSpan.FromSeconds(45))
                {
                    timedOut = true;
                    Application.ExitThread();
                }
            };
            server.Start();
            // Client retries until the real listener is available.
            File.WriteAllText(args[0], "ready", Encoding.UTF8);
            ticker.Start();
            Application.Run(new ApplicationContext());
            ticker.Stop();

            if (timedOut || sessionCount != expectedSessions)
            {
                Console.Error.WriteLine($"FAIL native TIP fixture: timedOut={timedOut}; real session count={sessionCount}; expected {expectedSessions}.");
                return 1;
            }
            Console.WriteLine($"PASS native TIP fixture: real TipServer/Session, {sessionCount} sessions across 2 connections.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL T3c-B: " + ex);
            return 1;
        }
    }

    private sealed class NullConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => null;
    }
}
