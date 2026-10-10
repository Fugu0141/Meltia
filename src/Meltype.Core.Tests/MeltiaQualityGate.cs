// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// Deterministic, OS-neutral QA. Fuzz checks properties, not language accuracy.
/// </summary>
internal static class MeltiaQualityGate
{
    private const uint Seed = 0xC0FFEE42;
    private const int MaxFailures = 60;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly (string Group, string Input)[] Focused =
    [
        ("japanese", "kyouhaiitenkidesune"),
        ("japanese", "nihongowobenkyoushiteimasu"),
        ("english", "hello"),
        ("english", "seeyouagain"),
        ("mixed", "kyouhagoogledekensaku"),
        ("mixed", "githubdeissue"),
        ("ambiguous", "sushi"),
        ("ambiguous", "oreha"),
        ("names", "Tokyo"),
        ("names", "OpenAI"),
        ("acronyms", "IME"),
        ("acronyms", "API"),
        ("url", "https://example.com"),
        ("url", "github.com"),
        ("email", "alice@example.com"),
        ("email", "test.user@example.co.jp"),
        ("code", "node.js"),
        ("code", "foo_bar"),
        ("symbols", "C++"),
        ("symbols", "foo-bar"),
        ("digits", "v2.0"),
        ("digits", "abc123"),
        ("typos", "japaneseno"),
        ("typos", "tokyo"),
        ("boundaries", "soredemedalsha"),
        ("boundaries", "kinouhasnaredha"),
        ("long", "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo"),
        ("long", "orehajapanesenotokyotoomoimasu"),
    ];

    public static int Run(string[] args)
    {
        var path = args.ElementAtOrDefault(0) ?? "meltia-quality-report.json";
        if (!int.TryParse(args.ElementAtOrDefault(1), out var fuzzCount) ||
            fuzzCount < 1 || fuzzCount > 15000)
        {
            Console.Error.WriteLine("Usage: --meltia-qa <report.json> <fuzz-count 1..15000>");
            return 2;
        }

        var failures = new List<string>();
        var failureCount = 0;
        var random = new XorShift32(Seed);
        var uniqueInputs = new HashSet<string>(StringComparer.Ordinal);
        var categoryCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var latencies = new List<double>();
        var processedFrames = 0L;
        var determinismRuns = 0;
        var safetyCases = 0;
        var guardRefusals = 0;
        var jsonSamples = 0;
        var quality = new Quality.Result(new(), new(), new());

        void Fail(string message)
        {
            failureCount++;
            if (failures.Count < MaxFailures) failures.Add(message);
        }

        void Check(string input, string category, bool repeated)
        {
            var timer = Stopwatch.StartNew();
            var first = Execute(input);
            timer.Stop();
            safetyCases++;
            latencies.Add(timer.Elapsed.TotalMilliseconds);
            uniqueInputs.Add(input);
            categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + 1;
            processedFrames += first.Frames;
            guardRefusals += first.GuardRefusals;
            if (first.Failure is not null) Fail($"{category}: '{input}': {first.Failure}");

            if (repeated)
            {
                determinismRuns++;
                var second = Execute(input);
                if (second.Failure is not null || first.Signature != second.Signature)
                    Fail($"determinism: '{input}': {second.Failure ?? "different session trace"}");
            }

            if (jsonSamples < 200 && safetyCases % 11 == 1)
            {
                jsonSamples++;
                var detector = CompositionDetector.CreateDefault();
                detector.SpellChecker = BuiltInWordChecker.Shared;
                var session = new MeltypeSession(detector, new CompositionTests.FakeConverter(),
                    new CompositionOptions(), () => new Settings());
                var firstChar = input[0];
                var result = session.HandleKey(KeyCode(firstChar), firstChar,
                    char.IsAsciiLetterUpper(firstChar), false, false, false);
                try
                {
                    using var json = JsonDocument.Parse(result.ToJson());
                    if (!json.RootElement.TryGetProperty("consumed", out _))
                        Fail($"serialization: '{input}': missing consumed");
                }
                catch (Exception e)
                {
                    Fail($"serialization: '{input}': {e.GetType().Name}: {e.Message}");
                }
            }
        }

        try
        {
            foreach (var (group, input) in Focused) Check(input, group, repeated: true);
            var samples = new List<string>();
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-+#@/: ";
            for (var i = 0; i < fuzzCount; i++)
            {
                var length = 1 + (int)(random.Next() % 32);
                var chars = new char[length];
                for (var j = 0; j < chars.Length; j++)
                    chars[j] = alphabet[(int)(random.Next() % (uint)alphabet.Length)];
                var input = new string(chars);
                Check(input, "seeded-fuzz", repeated: false);
                if (i < 250) samples.Add(input);
            }

            foreach (var input in samples)
            {
                determinismRuns++;
                var one = Execute(input);
                var two = Execute(input);
                if (one.Failure is not null || two.Failure is not null || one.Signature != two.Signature)
                    Fail($"repeat-replay: '{input}'");
            }

            quality = Quality.Run();
            if (quality.Rate < 0.95)
                Fail($"gold: overall rate {quality.Rate:P2} < 95%");
            foreach (var (category, (pass, total)) in quality.ByCategory)
                if (total > 0 && (double)pass / total < 0.80)
                    Fail($"gold: {category}: {pass}/{total} < 80%");
        }
        catch (Exception ex)
        {
            Fail($"harness: {ex.GetType().Name}: {ex.Message}");
        }

        latencies.Sort();
        static double Percentile(List<double> values, double fraction) =>
            values.Count == 0 ? 0 : values[(int)Math.Ceiling((values.Count - 1) * fraction)];

        var report = new
        {
            schema = "meltia/quality-gate-v1",
            gitSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            os = Environment.OSVersion.ToString(),
            configuration = new { seed = Seed, fuzzCount, maxInputLength = 32,
                normalization = "ASCII key events / built-in word checker",
                verdict = "Safety plus existing gold; randomized fuzz has no gold labels." },
            evidence = new
            {
                safetyCases, uniqueInputs = uniqueInputs.Count,
                distinctGoldKeys = quality.Cases.Select(c => c.Key)
                    .Distinct(StringComparer.Ordinal).Count(),
                goldCases = quality.Total, manuallyReviewedNewGoldCases = 0,
                categories = categoryCounts, processedFrames, determinismRuns,
                jsonSamples, guardRefusals
            },
            gold = new
            {
                pass = quality.Pass, total = quality.Total, rate = quality.Rate,
                categories = quality.ByCategory.ToDictionary(x => x.Key,
                    x => new { pass = x.Value.Pass, total = x.Value.Total }),
                failingCases = quality.Cases.Where(c => !c.Ok)
                    .Select(c => new { c.Key, c.Detail }).Take(MaxFailures).ToArray()
            },
            safety = new { failures = failureCount, firstFailures = failures },
            latency = new
            {
                units = "milliseconds per input, simulated end-to-end session",
                p50 = Percentile(latencies, 0.50),
                p95 = Percentile(latencies, 0.95),
                p99 = Percentile(latencies, 0.99),
                gating = false,
                reason = "Shared CI runners require matched baseline calibration."
            }
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Report writing failed: {ex.Message}");
            return 2;
        }
        Console.WriteLine($"Meltia QA: safety {safetyCases} ({uniqueInputs.Count} distinct), " +
                          $"gold {quality.Pass}/{quality.Total}, failures {failureCount}");
        Console.WriteLine($"Replay checks: {determinismRuns}; frames {processedFrames:N0}; " +
                          $"latency p95 {Percentile(latencies, 0.95):F3} ms/input");
        foreach (var message in failures) Console.Error.WriteLine("  FAIL " + message);
        Console.WriteLine("JSON: " + path);
        return failureCount == 0 ? 0 : 1;
    }

    private sealed record ReplayResult(string Signature, int Frames, int GuardRefusals, string? Failure);

    private static ReplayResult Execute(string input)
    {
        try
        {
            var detector = CompositionDetector.CreateDefault();
            detector.SpellChecker = BuiltInWordChecker.Shared;
            var session = new MeltypeSession(detector, new CompositionTests.FakeConverter(),
                new CompositionOptions(), () => new Settings());
            var signature = new StringBuilder();
            var document = "";
            var guardRefusals = 0;
            var frames = 0;
            foreach (var c in input + "\n")
            {
                var character = c is '\n' or ' ' ? (char?)null : c;
                var result = session.HandleKey(KeyCode(c), character,
                    char.IsAsciiLetterUpper(c), false, false, false);
                frames++;
                signature.Append(result.Consumed ? 'Y' : 'N')
                    .Append('{').Append(result.View?.Text ?? "(none)").Append('}');
                foreach (var edit in result.Commits)
                {
                    signature.Append('[').Append(edit.DeleteBefore).Append(':')
                        .Append(edit.Expect).Append("=>").Append(edit.Text).Append(']');
                    if (edit.DeleteBefore < 0)
                        return new ReplayResult(signature.ToString(), frames, guardRefusals,
                            "negative deletion length");
                    if (edit.DeleteBefore > 0)
                    {
                        // Simulate the host-side Expect guard: never delete unrelated text.
                        if (edit.Expect is not null &&
                            (document.Length < edit.DeleteBefore ||
                             !document.EndsWith(edit.Expect, StringComparison.Ordinal)))
                        {
                            guardRefusals++;
                            continue;
                        }
                        if (edit.DeleteBefore > document.Length)
                            return new ReplayResult(signature.ToString(), frames, guardRefusals,
                                "deletion exceeds simulated document");
                        document = document[..^edit.DeleteBefore];
                    }
                    document += edit.Text;
                }
                if (!result.Consumed) document += c;
            }
            return new ReplayResult(signature.Append('|').Append(document).ToString(), frames,
                guardRefusals, null);
        }
        catch (Exception ex)
        {
            return new ReplayResult("", 0, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int KeyCode(char c) => c switch
    {
        '\n' => VirtualKeys.Return,
        ' ' => VirtualKeys.Space,
        ',' => VirtualKeys.OemComma,
        '.' => VirtualKeys.OemPeriod,
        '-' => VirtualKeys.OemMinus,
        '/' => VirtualKeys.Oem2,
        '#' => '3',
        _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c),
        _ => c,
    };

    private sealed class XorShift32(uint seed)
    {
        private uint _value = seed;
        public uint Next()
        {
            var n = _value;
            n ^= n << 13;
            n ^= n >> 17;
            n ^= n << 5;
            return _value = n;
        }
    }
}
