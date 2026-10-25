// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Meltype.Composition;

namespace Meltype;

/// <summary>
/// Opt-in, test-process-only observation of the real keyboard Preview.
/// No typed text is recorded during normal use.
/// </summary>
internal static class MeltiaE2eTrace
{
    private static readonly object Sync = new();
    private static StreamWriter? _writer;
    private static bool _initialized;
    private static string? _lastControlToken;
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("MELTIA_E2E_CHILD") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MELTIA_E2E_TRACE"));

    /// <summary>Reset controller state between synthetic cases on the Preview UI thread.</summary>
    public static void ProcessControl(CompositionController controller)
    {
        if (!Enabled) return;
        var control = Environment.GetEnvironmentVariable("MELTIA_E2E_CONTROL");
        var ack = Environment.GetEnvironmentVariable("MELTIA_E2E_ACK");
        if (string.IsNullOrWhiteSpace(control) || string.IsNullOrWhiteSpace(ack)) return;
        try
        {
            if (!File.Exists(control)) return;
            var token = File.ReadAllText(control, Encoding.UTF8);
            if (token.Length == 0 || token == _lastControlToken) return;
            controller.Reset();
            controller.ResetContext();
            controller.ForgetLastCommit();
            controller.Pump(); // Release any stale capture before the next case.
            File.WriteAllText(ack, token, Encoding.UTF8);
            _lastControlToken = token;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("E2E case reset failed: " + ex.GetType().Name);
        }
    }

    public static void Ready()
    {
        if (!Enabled) return;
        var ready = Environment.GetEnvironmentVariable("MELTIA_E2E_READY");
        if (string.IsNullOrWhiteSpace(ready)) return;
        File.WriteAllText(ready, $"{Environment.ProcessId}\n", Encoding.UTF8);
    }

    public static void Record(string kind, CompositionController controller, string? visibleText)
    {
        if (!Enabled) return;
        try
        {
            var snapshot = controller.DiagnosticSnapshot();
            int position = 0;
            var spans = snapshot.Segments.Select(segment =>
            {
                int start = position;
                position += segment.Raw.Length;
                return new
                {
                    start, end = position, raw = segment.Raw,
                    kana = segment.Kana, language = segment.IsEnglish ? "en" : "ja",
                    protectedSpan = segment.IsProtected
                };
            }).ToArray();
            var item = new
            {
                type = kind,
                caseId = _lastControlToken,
                qpc = Stopwatch.GetTimestamp(),
                utc = DateTimeOffset.UtcNow,
                raw = snapshot.Raw,
                visible = visibleText,
                spans,
                coversRaw = position == snapshot.Raw.Length
            };
            lock (Sync)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    var path = Path.GetFullPath(Environment.GetEnvironmentVariable("MELTIA_E2E_TRACE")!);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite),
                        new UTF8Encoding(false)) { AutoFlush = true };
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => _writer?.Dispose();
                }
                _writer?.WriteLine(JsonSerializer.Serialize(item));
            }
        }
        catch (Exception ex)
        {
            // The observer must never interfere with the IME itself.
            Diagnostics.Log.Warn("E2E trace failed: " + ex.GetType().Name);
        }
    }
}
