// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meltype.Config;

namespace Meltype;

/// <summary>
/// Sends Win32 keyboard events to an actual focused WinForms edit control
/// while a separate Meltia Preview process owns the real keyboard hook.
/// Windows interactive desktop only; not a Core-only simulation.
/// </summary>
internal static class MeltiaE2eHarness
{
    private sealed record Case(string Id, string Category, string Raw, string? Expected, JsonElement? Spans);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const int MaxRawLength = 512;
    private const int DefaultKeyDelayMs = 12;
    private const int DefaultSettleMs = 140;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char character);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    public static int Run(string[] args)
    {
        if (args.Length is < 3 or > 5)
        {
            Console.Error.WriteLine("Usage: Meltia.exe --meltia-e2e <cases.jsonl> <output-parent-dir> [key-delay-ms] [settle-ms]");
            return 2;
        }

        IReadOnlyList<Case> cases;
        int keyDelay = DefaultKeyDelayMs, settle = DefaultSettleMs;
        try
        {
            if (args.Length > 3) keyDelay = int.Parse(args[3], CultureInfo.InvariantCulture);
            if (args.Length > 4) settle = int.Parse(args[4], CultureInfo.InvariantCulture);
            if (keyDelay is < 1 or > 1000 || settle is < 50 or > 10000)
                throw new ArgumentException("Invalid key-delay (1..1000) or settle-ms (50..10000).");
            cases = LoadCases(Path.GetFullPath(args[1]));
            if (cases.Count == 0 || cases.Count > 100000)
                throw new ArgumentException("Corpus must contain 1..100000 cases.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var runDir = Path.Combine(Path.GetFullPath(args[2]), "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDir);
        var state = Path.Combine(runDir, "profile");
        Directory.CreateDirectory(state); // New, empty; no user files are removed or migrated.
        var ready = Path.Combine(runDir, "ready.txt");
        var control = Path.Combine(runDir, "control.txt");
        var ack = Path.Combine(runDir, "ack.txt");
        var trace = Path.Combine(runDir, "composition.jsonl");
        var results = Path.Combine(runDir, "cases.jsonl");
        var summary = Path.Combine(runDir, "summary.json");
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        var datasetHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(args[1])))).ToLowerInvariant();
        string? executableHash = File.Exists(exe)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))).ToLowerInvariant() : null;
        var settings = new Settings
        {
            Enabled = true,
            Mode = InputMode.Keyboard,
            AllowInjectedInput = true, // Only in the disposable, isolated child.
            LearningEnabled = false,
            LiveConversion = false,
            PredictiveCandidates = false,
            AutoCorrectAfterCommit = false,
            CorrectTypos = false,
            AutoUpdate = false,
            ConversionEngine = ConversionEngine.System,
            TranslationCandidates = false,
            ShowCandidateMeanings = false,
            TabConversion = false,
            PasteApps = "",
            NoPasteApps = "",
            UseTsf = false,
            FileLog = false,
            LogTypedText = false,
            ShowNotifications = false,
            ShowModeIndicator = false,
            ShowModeIndicatorOnFocus = false,
            WelcomeShown = true,
            DirectModeAutoDetect = false
        };
        settings.Save(Path.Combine(state, "config.json"));

        // Start the isolated child before displaying the target edit box.
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.Environment["MELTIA_E2E_CHILD"] = "1";
        start.Environment["MELTIA_E2E_TRACE"] = trace;
        start.Environment["MELTIA_E2E_READY"] = ready;
        start.Environment["MELTIA_E2E_CONTROL"] = control;
        start.Environment["MELTIA_E2E_ACK"] = ack;
        start.Environment["MELTIA_DATA_DIR"] = state;
        start.Environment["MELTYPE_DATA_DIR"] = state;
        // Do not enable the experimental model implicitly: inherit caller selection.
        using var child = Process.Start(start);
        if (child is null) throw new InvalidOperationException("Cannot launch Meltia Preview child.");

        int returnCode = 1;
        int processed = 0;
        int matches = 0;
        int noTraces = 0;
        int boundaryFailures = 0;
        int boundariesCompared = 0, boundariesMatched = 0;
        string? fatal = null;
        var durations = new List<double>(cases.Count);
        var perCategory = new Dictionary<string, (int Total, int Matches, int BoundariesCompared, int BoundariesMatched)>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();
        using var output = new StreamWriter(results, false, new UTF8Encoding(false)) { AutoFlush = true };
        var traceOffset = 0L;
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "Meltia E2E: synthetic input target — do not type or switch windows",
            Width = 900, Height = 350,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true
        };
        using var field = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 16),
            AccessibleName = "Meltia real key input test target"
        };
        form.Controls.Add(field);
        form.Shown += async (_, _) =>
        {
            try
            {
                var startupDeadline = Stopwatch.StartNew();
                while (!File.Exists(ready) && startupDeadline.Elapsed < TimeSpan.FromSeconds(20))
                {
                    if (child.HasExited)
                        throw new InvalidOperationException("Meltia child exited before becoming ready.");
                    await Task.Delay(100);
                }
                if (!File.Exists(ready)) throw new TimeoutException("Preview hook startup timed out.");
                form.Activate();
                field.Focus();
                await Task.Delay(250);
                foreach (var test in cases)
                {
                    if (child.HasExited) throw new InvalidOperationException("Preview child exited during the run.");
                    form.Activate();
                    field.Focus();
                    await Task.Delay(20);
                    if (Native.GetForegroundWindow() != form.Handle || !field.Focused)
                        throw new InvalidOperationException("The input target lost keyboard focus. Aborting to avoid typing into another application.");

                    // Cancel any pending composition before clearing the controlled field.
                    SendVirtualKey(0x1B); // Escape
                    await Task.Delay(30);
                    field.Clear();
                    string token = test.Id + ":" + processed.ToString(CultureInfo.InvariantCulture);
                    File.WriteAllText(control, token, Encoding.UTF8);
                    var resetDeadline = Stopwatch.StartNew();
                    while ((!File.Exists(ack) || File.ReadAllText(ack, Encoding.UTF8) != token) &&
                           resetDeadline.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        if (child.HasExited)
                            throw new InvalidOperationException("Preview exited during case reset.");
                        await Task.Delay(25);
                    }
                    if (!File.Exists(ack) || File.ReadAllText(ack, Encoding.UTF8) != token)
                        throw new TimeoutException("Preview did not acknowledge clean per-case composition reset.");
                    ReadTrace(trace, ref traceOffset); // discard inter-case reset observations
                    var caseClock = Stopwatch.StartNew();
                    double sendMs = 0;
                    var keyQpc = new List<long>(test.Raw.Length);
                    foreach (char character in test.Raw)
                    {
                        if (Native.GetForegroundWindow() != form.Handle || !field.Focused)
                            throw new InvalidOperationException("Foreground changed during synthetic typing. Input stopped.");
                        keyQpc.Add(Stopwatch.GetTimestamp());
                        var sent = Stopwatch.StartNew();
                        SendCharacter(character);
                        sent.Stop();
                        sendMs += sent.Elapsed.TotalMilliseconds;
                        await Task.Delay(keyDelay);
                    }
                    long enterQpc = Stopwatch.GetTimestamp();
                    SendVirtualKey(0x0D); // Enter: commits current preview composition
                    await Task.Delay(settle);
                    var observed = field.Text;
                    caseClock.Stop();

                    var events = ReadTrace(trace, ref traceOffset);
                    JsonElement? lastSpanEvent = null, lastCommitEvent = null;
                    foreach (var item in events)
                    {
                        if (!item.TryGetProperty("type", out var type)) continue;
                        if (type.GetString() == "commit") lastCommitEvent = item;
                        if (item.TryGetProperty("raw", out var raw) &&
                            raw.GetString() == test.Raw) lastSpanEvent = item;
                    }
                    // First live-view observation of each exact raw prefix after its SendInput.
                    // These timings include keyboard-hook dispatch and UI scheduling, but
                    // do not claim the target edit control has painted the character.
                    var keyViewLatencies = new List<double>();
                    var observedPrefixCount = 0;
                    for (int i = 0; i < test.Raw.Length; ++i)
                    {
                        var rawPrefix = test.Raw[..(i + 1)];
                        foreach (var ev in events)
                        {
                            if (!ev.TryGetProperty("type", out var et) ||
                                et.GetString() != "view" ||
                                !ev.TryGetProperty("raw", out var er) ||
                                er.GetString() != rawPrefix ||
                                !ev.TryGetProperty("qpc", out var tqpc)) continue;
                            long tick = tqpc.GetInt64();
                            if (tick < keyQpc[i]) continue;
                            observedPrefixCount++;
                            keyViewLatencies.Add((tick - keyQpc[i]) * 1000.0 / Stopwatch.Frequency);
                            break;
                        }
                    }
                    var spanEvent = lastSpanEvent ?? lastCommitEvent;
                    var actualSpans = spanEvent is { } record && record.TryGetProperty("spans", out var spans)
                        ? (JsonElement?)spans : null;
                    bool covered = spanEvent is { } item2 &&
                        item2.TryGetProperty("coversRaw", out var covers) && covers.GetBoolean();
                    bool? boundaryMatch = MatchBoundaryLanguages(test.Spans, actualSpans, test.Raw.Length);
                    if (boundaryMatch is { } isMatch)
                    {
                        boundariesCompared++;
                        if (isMatch) boundariesMatched++;
                    }
                    bool matched = test.Expected is null || string.Equals(observed, test.Expected, StringComparison.Ordinal);
                    if (test.Expected is not null && matched) matches++;
                    if (events.Count == 0) noTraces++;
                    if (spanEvent is not null && !covered) boundaryFailures++;
                    durations.Add(caseClock.Elapsed.TotalMilliseconds);
                    var current = perCategory.GetValueOrDefault(test.Category);
                    perCategory[test.Category] = (
                        current.Total + 1, current.Matches + (matched ? 1 : 0),
                        current.BoundariesCompared + (boundaryMatch.HasValue ? 1 : 0),
                        current.BoundariesMatched + (boundaryMatch == true ? 1 : 0));
                    processed++;
                    output.WriteLine(JsonSerializer.Serialize(new
                    {
                        id = test.Id, category = test.Category,
                        raw = test.Raw, expected = test.Expected, actual = observed,
                        expectedSpans = test.Spans, observedSpans = actualSpans,
                        previewText = spanEvent is { } e && e.TryGetProperty("visible", out var txt) ? txt.GetString() : null,
                        reportedCommit = lastCommitEvent is { } c && c.TryGetProperty("visible", out var committed) ? committed.GetString() : null,
                        result = test.Expected is null ? "unlabeled" : matched ? "match" : "mismatch",
                        boundaryCovered = spanEvent is null ? (bool?)null : covered,
                        boundaryMatch,
                        traceEvents = events.Count,
                        durationMs = caseClock.Elapsed.TotalMilliseconds,
                        sendInputApiMs = sendMs,
                        keyViewObserved = observedPrefixCount,
                        keyViewLatencyMsP50 = Percentile(keyViewLatencies, .50),
                        keyViewLatencyMsP95 = Percentile(keyViewLatencies, .95),
                        keyViewLatencyMsP99 = Percentile(keyViewLatencies, .99),
                        enterQpc // correlate input and child trace QPC; not a processing-latency measurement
                    }));
                    if (processed % 100 == 0)
                        form.Text = $"Meltia E2E — {processed}/{cases.Count} complete, mismatches {processed - matches}";
                }
                returnCode = noTraces == 0 && boundaryFailures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                fatal = ex.ToString();
                returnCode = 1;
            }
            finally
            {
                clock.Stop();
                try
                {
                    var stateFiles = Directory.GetFiles(state, "*", SearchOption.AllDirectories)
                        .Select(p => Path.GetRelativePath(state, p)).OrderBy(p => p).ToArray();
                    File.WriteAllText(summary, JsonSerializer.Serialize(new
                    {
                        schema = "meltia-e2e-v1", windowsRealKeyboardHook = true,
                        mode = "keyboard-preview-not-tsf",
                        environmentIsolation = "fresh-meltia-profile-only",
                        osImeUserDictionaryResetVerified = false,
                        learning = "disabled; new private profile, no inherited user dictionaries",
                        runUtc = DateTimeOffset.UtcNow,
                        os = Environment.OSVersion.ToString(),
                        runtime = Environment.Version.ToString(),
                        layout = InputLanguage.CurrentInputLanguage.LayoutName,
                        experimentalBoundary = Environment.GetEnvironmentVariable("MELTIA_BOUNDARY_PREVIEW") == "1",
                        exe, executableSha256 = executableHash, corpusSha256 = datasetHash,
                        keyDelayMs = keyDelay, settleMs = settle,
                        requested = cases.Count, completed = processed,
                        expectedMatches = matches, noTraceCases = noTraces,
                        boundaryCoverageFailures = boundaryFailures,
                        boundariesCompared, boundariesMatched,
                        durationSeconds = clock.Elapsed.TotalSeconds,
                        durationMsP50 = Percentile(durations, .50),
                        durationMsP95 = Percentile(durations, .95),
                        durationMsP99 = Percentile(durations, .99),
                        categories = perCategory.ToDictionary(p => p.Key,
                            p => new {
                                completed = p.Value.Total, matches = p.Value.Matches,
                                boundariesCompared = p.Value.BoundariesCompared,
                                boundariesMatched = p.Value.BoundariesMatched }),
                        profileFiles = stateFiles, fatal, interrupted = processed < cases.Count,
                        result = returnCode == 0 ? "completed-with-observations" : "incomplete-or-observation-failure"
                    }, JsonOptions));
                }
                catch (Exception ex) { fatal ??= "Unable to write summary: " + ex.Message; }
                form.Close();
            }
        };
        try { Application.Run(form); }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(5000);
            }
        }
        Console.WriteLine($"Meltia E2E: {processed}/{cases.Count}; output: {runDir}");
        if (fatal is not null) Console.Error.WriteLine(fatal);
        return returnCode;
    }

    private static IReadOnlyList<Case> LoadCases(string path)
    {
        var result = new List<Case>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, index) in File.ReadLines(path).Select((s, i) => (s, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var item = doc.RootElement;
            var id = item.GetProperty("id").GetString() ?? "";
            var category = item.GetProperty("category").GetString() ?? "";
            var raw = item.GetProperty("raw").GetString() ?? "";
            string? expected = item.TryGetProperty("expected", out var exp) &&
                exp.ValueKind != JsonValueKind.Null ? exp.GetString() : null;
            var spans = item.TryGetProperty("spans", out var s) ? (JsonElement?)s.Clone() : null;
            if (id.Length is < 1 or > 128 || !identifiers.Add(id) ||
                category.Length is < 1 or > 64 ||
                category.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
                raw.Length is < 1 or > MaxRawLength ||
                raw.Any(c => c < 0x20 || c > 0x7E))
                throw new InvalidDataException($"Invalid or duplicate E2E case at line {index}.");
            // A test is exactly one string (word or sentence); newline/keyboard shortcuts are excluded.
            result.Add(new Case(id, category, raw, expected, spans));
        }
        return result;
    }

    private static List<JsonElement> ReadTrace(string path, ref long offset)
    {
        var records = new List<JsonElement>();
        if (!File.Exists(path)) return records;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offset > stream.Length) throw new InvalidDataException("Trace was replaced during the run.");
        stream.Seek(offset, SeekOrigin.Begin);
        using (var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true))
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var record = JsonDocument.Parse(line);
                records.Add(record.RootElement.Clone());
            }
        }
        offset = stream.Position;
        return records;
    }

    /// <summary>Compare labels per UTF-16 unit, not the arbitrary number of same-language spans.</summary>
    private static bool? MatchBoundaryLanguages(JsonElement? expected, JsonElement? actual, int rawLength)
    {
        if (expected is null || actual is null ||
            expected.Value.ValueKind != JsonValueKind.Array ||
            actual.Value.ValueKind != JsonValueKind.Array) return null;
        static string[]? ToLanguages(JsonElement spans, int length)
        {
            var languages = new string[length];
            var position = 0;
            foreach (var span in spans.EnumerateArray())
            {
                if (!span.TryGetProperty("start", out var a) ||
                    !span.TryGetProperty("end", out var b) ||
                    !span.TryGetProperty("language", out var l)) return null;
                int start = a.GetInt32(), end = b.GetInt32();
                if (start != position || end <= start || end > length) return null;
                var language = l.GetString();
                if (language is not ("ja" or "en")) return null;
                for (int i = start; i < end; ++i) languages[i] = language;
                position = end;
            }
            return position == length ? languages : null;
        }
        var left = ToLanguages(expected.Value, rawLength);
        var right = ToLanguages(actual.Value, rawLength);
        return left is null || right is null ? null : left.SequenceEqual(right);
    }

    private static double? Percentile(List<double> values, double proportion)
    {
        if (values.Count == 0) return null;
        var ordered = values.Order().ToArray();
        var index = (int)Math.Ceiling(proportion * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static void SendCharacter(char c)
    {
        short mapped = VkKeyScanW(c);
        if (mapped == -1 || ((mapped >> 8) & ~1) != 0)
            throw new InvalidDataException($"Keyboard layout cannot type ASCII U+{(int)c:X4}; use a US/JIS ROMAJI layout.");
        int vk = mapped & 0xFF;
        bool shift = ((mapped >> 8) & 1) != 0;
        var events = new List<Native.INPUT>(shift ? 4 : 2);
        if (shift) events.Add(Key(0x10, false));
        events.Add(Key(vk, false));
        events.Add(Key(vk, true));
        if (shift) events.Add(Key(0x10, true));
        Send(events);
    }

    private static void SendVirtualKey(int virtualKey) => Send([Key(virtualKey, false), Key(virtualKey, true)]);

    private static Native.INPUT Key(int virtualKey, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = (ushort)virtualKey,
                wScan = (ushort)MapVirtualKeyW((uint)virtualKey, 0),
                dwFlags = up ? Native.KEYEVENTF_KEYUP : 0,
                // Do NOT use KeyboardMonitor.InjectedMarker: that means Meltia's OWN replay.
                dwExtraInfo = UIntPtr.Zero
            }
        }
    };

    private static void Send(IReadOnlyList<Native.INPUT> events)
    {
        var inputs = events.ToArray();
        uint done = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        if (done != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput delivered {done}/{inputs.Length} events");
    }
}
