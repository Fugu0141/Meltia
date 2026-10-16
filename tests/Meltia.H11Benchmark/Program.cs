// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Offline, deterministic synthetic workloads only. No user keystrokes, no gold labels.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meltia.Core.Boundary;

internal sealed record Fixture(string Family, string Operation, string Raw,
    BoundaryEvidence? Evidence = null);

internal sealed record Measurement(
    string Model, string Family, string Operation, int Utf16Length,
    string RawSha256, string Evidence, int RouteCount,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds,
    double P50AllocatedBytes, double P95AllocatedBytes, double P99AllocatedBytes,
    int Gen0Collections, int Gen1Collections, int Gen2Collections,
    double[] ElapsedMilliseconds, long[] AllocatedBytes);

internal static class Program
{
    private static readonly string[] ModelNames = ["v1.0-flank-independent",
        "H10-dual-channel", "H11-word-phonetic"];
    private static readonly MeltiaBoundaryEngine Old = new(new MeltiaBoundaryOptions(
        ExperimentalUnknownLatinCandidates: true,
        ExperimentalPhoneticLatticeDecoder: true,
        ExperimentalContextualIslandScoring: true,
        ExperimentalUncertaintyReview: true,
        ExperimentalLongRunWindowing: true,
        ExperimentalDiverseCandidatePruning: true,
        ExperimentalJapaneseFlankScoring: true));
    private static readonly H10BoundaryPrototype H10 = new();
    private static readonly H11BoundaryPrototype H11 = new();

    public static int Main(string[] args)
    {
        string? mode = null;
        string? output = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--self-test" or "--smoke" or "--full")
            {
                if (mode is not null)
                    throw new ArgumentException("Specify exactly one benchmark mode");
                mode = args[i];
            }
            else if (args[i] == "--output" && i + 1 < args.Length)
                output = args[++i];
            else
                throw new ArgumentException(
                    "Usage: --self-test|--smoke|--full [--output report.json]");
        }
        if (mode is null)
            throw new ArgumentException("A benchmark mode is required");
        if (mode == "--self-test" && output is not null)
            throw new ArgumentException("Self-test does not generate a report");

        int[] lengths = mode == "--smoke" ? [32, 128] :
            [32, 128, 256, 512, 1024];
        var fixtures = BuildFixtures(lengths);
        CheckFixtures(fixtures, lengths);
        TestEngineLimits();
        if (mode == "--self-test")
        {
            Console.WriteLine("H11_BENCH_FIXTURES_PASS: UTF-16, lengths, edit scenarios and maximum input");
            return 0;
        }

        int warmups = mode == "--smoke" ? 1 : 2;
        int repetitions = mode == "--smoke" ? 3 : 7;
        var measurements = new List<Measurement>();
        foreach (var fixture in fixtures)
        {
            foreach (string model in ModelNames)
            {
                var result = Measure(model, fixture, warmups, repetitions);
                measurements.Add(result);
                Console.WriteLine($"BENCH {model} {fixture.Family}/{fixture.Operation} " +
                    $"n={fixture.Raw.Length}: p50={result.P50Milliseconds:F3}ms " +
                    $"p95={result.P95Milliseconds:F3}ms " +
                    $"p99={result.P99Milliseconds:F3}ms " +
                    $"alloc-p50={result.P50AllocatedBytes:F0}B");
            }
        }

        string root = FindRepoRoot();
        var modelSourceHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v1.0-flank-independent"] = FileSha(Path.Combine(root,
                "src/Meltia.Core/Boundary/MeltiaBoundaryEngine.cs")),
            ["H10-dual-channel"] = FileSha(Path.Combine(root,
                "src/Meltia.Core/Boundary/H10BoundaryPrototype.cs")),
            ["H11-word-phonetic"] = FileSha(Path.Combine(root,
                "src/Meltia.Core/Boundary/H11BoundaryPrototype.cs"))
        };
        var report = new
        {
            Schema = "meltia-h11-synthetic-performance-v1",
            DataKind = "Synthetic unlabelled benchmark, NOT a natural-language holdout or keyboard E2E",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Mode = mode[2..],
            RepositoryCommit = Environment.GetEnvironmentVariable("MELTIA_SOURCE_SHA")
                ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-unspecified",
            SdkVersion = DotnetSdkVersion(),
            Runtime = RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessorCount = Environment.ProcessorCount,
            CpuModel = CpuModel(),
            GCMode = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
            Stopwatch.Frequency,
            WarmupsPerFixtureAndModel = warmups,
            RepetitionsPerFixtureAndModel = repetitions,
            InputUtf16Max = H11BoundaryPrototype.MaxInputLength,
            FixtureGenerator = "repeat-and-safe-utf16-fill-v1 + H10 reference-session-edits-v1",
            FixtureCount = fixtures.Count,
            FixtureSetSha256 = Hash(string.Join("\n", fixtures.Select(x =>
                $"{x.Family}|{x.Operation}|{x.Raw.Length}|{x.Raw}|{EvidenceName(x.Evidence)}"))),
            DictionaryFileSha256 = FileSha(Path.Combine(root, "dictionaries/english.txt")),
            EmbeddedDictionarySha256 = EmbeddedDictionarySha(),
            ModelSourceSha256 = modelSourceHashes,
            Percentiles = "nearest-rank, sorted samples, ceil(p*n)-1",
            Allocations = "GC.GetAllocatedBytesForCurrentThread delta around each Analyze call",
            CollectionCounts = "per-fixture per-model GC.CollectionCount deltas excluding forced pre-measurement GC",
            MeasurementLimitations = "Offline full Analyze only; no host event, IME/TSF, user keystroke or precision SLA",
            Measurements = measurements
        };
        if (report.DictionaryFileSha256 != report.EmbeddedDictionarySha256)
            throw new InvalidDataException("Source dictionary differs from embedded H11 dictionary");
        string path = output ?? "artifacts/h11-benchmark/report.json";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"H11_BENCH_PASS: {fixtures.Count} fixtures x {ModelNames.Length} models, " +
            $"warmups={warmups}, repetitions={repetitions}; {Path.GetFullPath(path)}");
        return 0;
    }

    private static List<Fixture> BuildFixtures(int[] lengths)
    {
        var patterns = new (string Name, string Pattern)[]
        {
            ("romaji-ja", "kyouhanihongonobennkyouwoshimasu"),
            ("english", "the quick brown fox jumps over a lazy dog "),
            ("mixed", "kyouhaGitHubdecodewokakuto🌸releasegaokurimasu "),
            ("url", "https://example.com/docs/test?q=demo "),
            ("symbols", "code-42! [test] #file_9 ++ ~~ 123 "),
            ("emoji", "🌸nihongo😀GitHubde💻test "),
            ("explicit-en", "tokyoandnihongono"),
            ("explicit-ja", "tokyoandnihongono")
        };
        var fixtures = new List<Fixture>();
        foreach (int length in lengths)
        {
            foreach (var (name, pattern) in patterns)
            {
                BoundaryEvidence? evidence = name switch
                {
                    "explicit-en" => new BoundaryEvidence(
                        ExplicitEnglish: [new Utf16Span(0, 5)]),
                    "explicit-ja" => new BoundaryEvidence(
                        ExplicitJapanese: [new Utf16Span(0, 5)]),
                    _ => null
                };
                fixtures.Add(new Fixture(name, "full-decode", RepeatUtf16(pattern, length), evidence));
            }
        }

        // Deliberately use the existing UTF-16-safe reference editing session.
        // Timing below is ONLY the full Analyze after each edit, not edit latency.
        var session = new H10PrototypeSession();
        session.Insert(RepeatUtf16("kyouha🌸GitHubdecodewokakimasu ", 128));
        session.MoveCaret(SafeCaret(session.Raw, session.Raw.Length / 2));
        fixtures.Add(new Fixture("editing", "cursor-move", session.Raw));
        session.Insert("pasted🌸GitHub");
        fixtures.Add(new Fixture("editing", "paste", session.Raw));
        session.Backspace();
        fixtures.Add(new Fixture("editing", "backspace", session.Raw));
        session.MoveCaret(SafeCaret(session.Raw, session.Raw.Length / 3));
        session.DeleteForward();
        fixtures.Add(new Fixture("editing", "delete-forward", session.Raw));
        return fixtures;
    }

    private static int SafeCaret(string raw, int offset)
    {
        if (offset > 0 && offset < raw.Length &&
            char.IsHighSurrogate(raw[offset - 1]) && char.IsLowSurrogate(raw[offset]))
            return offset + 1;
        return offset;
    }

    private static string RepeatUtf16(string pattern, int length)
    {
        if (length < 1 || length > H11BoundaryPrototype.MaxInputLength)
            throw new ArgumentOutOfRangeException(nameof(length));
        var result = new StringBuilder(length);
        while (result.Length + pattern.Length <= length)
            result.Append(pattern);
        int left = length - result.Length;
        if (left > 0)
        {
            string tail = pattern[..left];
            if (char.IsHighSurrogate(tail[^1]))
                tail = tail[..^1];
            result.Append(tail);
            while (result.Length < length)
                result.Append('a');
        }
        return result.ToString();
    }

    private static void CheckFixtures(List<Fixture> fixtures, int[] lengths)
    {
        const int categoryCount = 8;
        if (fixtures.Count != lengths.Length * categoryCount + 4 ||
            fixtures.Select(x => (x.Family, x.Operation, x.Raw.Length)).Distinct().Count()
                != fixtures.Count)
            throw new InvalidDataException("Fixture missing, unexpected or duplicated");
        foreach (var x in fixtures)
        {
            if (x.Raw.Length == 0 || x.Raw.Length > H11BoundaryPrototype.MaxInputLength ||
                !WellFormedUtf16(x.Raw))
                throw new InvalidDataException("Invalid synthetic raw: " + x.Family);
            if (x.Operation == "full-decode" && !lengths.Contains(x.Raw.Length))
                throw new InvalidDataException("Unexpected requested raw length");
            if (x.Evidence is not null &&
                (x.Evidence.ExplicitEnglish?.Count ?? 0) +
                (x.Evidence.ExplicitJapanese?.Count ?? 0) != 1)
                throw new InvalidDataException("Unexpected explicit evidence");
        }
        if (fixtures.Count(x => x.Family == "editing") != 4 ||
            !fixtures.Where(x => x.Family == "editing")
                .Select(x => x.Operation).SequenceEqual(
                    ["cursor-move", "paste", "backspace", "delete-forward"]))
            throw new InvalidDataException("Incomplete edit sequence");
        if (fixtures.Where(x => x.Family == "editing")
            .Select(x => x.Raw).Distinct().Count() != 4)
        {
            // Cursor movement must preserve input and edit operations should
            // not all collapse to the same raw string.
            throw new InvalidDataException("Edit sequence did not mutate raw");
        }
    }

    private static bool WellFormedUtf16(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLowSurrogate(value[i]))
                return false;
            if (!char.IsHighSurrogate(value[i]))
                continue;
            if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                return false;
        }
        return true;
    }

    private static void TestEngineLimits()
    {
        if (H11BoundaryPrototype.MaxInputLength != 1024 ||
            H10BoundaryPrototype.MaxInputLength != H11BoundaryPrototype.MaxInputLength ||
            MeltiaBoundaryEngine.MaxInputLength != H11BoundaryPrototype.MaxInputLength)
            throw new InvalidDataException("Inconsistent engine input bounds");
        string overlong = new('x', 1025);
        foreach (string model in ModelNames)
        {
            try { Evaluate(model, overlong, null); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidDataException("Engine accepted overlong input: " + model);
        }
    }

    private static int Evaluate(string model, string raw, BoundaryEvidence? evidence)
    {
        string returned;
        int count;
        bool commits;
        switch (model)
        {
            case "v1.0-flank-independent":
                var old = Old.Analyze(raw, evidence);
                returned = old.Raw;
                count = old.Routes.Count;
                commits = old.AutoCommitAllowed;
                break;
            case "H10-dual-channel":
                var h10 = H10.Analyze(raw, evidence);
                returned = h10.Raw;
                count = h10.Routes.Count;
                commits = h10.AutoCommitAllowed;
                break;
            case "H11-word-phonetic":
                var h11 = H11.Analyze(raw, evidence);
                returned = h11.Raw;
                count = h11.Routes.Count;
                commits = h11.AutoCommitAllowed;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(model));
        }
        if (returned != raw || count is < 1 or > 8 || commits)
            throw new InvalidDataException("Engine invariant failed: " + model);
        return count;
    }

    private static Measurement Measure(
        string model, Fixture fixture, int warmups, int repetitions)
    {
        for (int i = 0; i < warmups; i++)
            _ = Evaluate(model, fixture.Raw, fixture.Evidence);
        // Apply collection outside the timed section; report collections
        // triggered by the engine but not the forced reset.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int[] gcbefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        var times = new double[repetitions];
        var allocated = new long[repetitions];
        int routeCount = 0;
        for (int i = 0; i < repetitions; i++)
        {
            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            long before = Stopwatch.GetTimestamp();
            routeCount = Evaluate(model, fixture.Raw, fixture.Evidence);
            long after = Stopwatch.GetTimestamp();
            long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
            times[i] = (after - before) * 1000.0 / Stopwatch.Frequency;
            allocated[i] = afterAlloc - beforeAlloc;
        }
        int[] collections =
        [
            GC.CollectionCount(0) - gcbefore[0],
            GC.CollectionCount(1) - gcbefore[1],
            GC.CollectionCount(2) - gcbefore[2]
        ];
        return new Measurement(
            model, fixture.Family, fixture.Operation, fixture.Raw.Length,
            Hash(fixture.Raw), EvidenceName(fixture.Evidence), routeCount,
            Percentile(times, .50), Percentile(times, .95), Percentile(times, .99),
            Percentile(allocated, .50), Percentile(allocated, .95),
            Percentile(allocated, .99), collections[0], collections[1], collections[2],
            times, allocated);
    }

    private static double Percentile(IEnumerable<double> samples, double fraction)
    {
        var sorted = samples.Order().ToArray();
        return sorted[(int)Math.Ceiling(fraction * sorted.Length) - 1];
    }

    private static double Percentile(IEnumerable<long> samples, double fraction) =>
        Percentile(samples.Select(x => (double)x), fraction);

    private static string EvidenceName(BoundaryEvidence? evidence)
    {
        if (evidence?.ExplicitEnglish is { Count: > 0 })
            return "explicit-en:0:5";
        if (evidence?.ExplicitJapanese is { Count: > 0 })
            return "explicit-ja:0:5";
        return "none";
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string FileSha(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string EmbeddedDictionarySha()
    {
        using var resource = typeof(H11BoundaryPrototype).Assembly
            .GetManifestResourceStream("Meltia.H11EnglishWords") ??
                throw new InvalidDataException("Embedded English dictionary missing");
        return Convert.ToHexString(SHA256.HashData(resource)).ToLowerInvariant();
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName,
                "src/Meltia.Core/Meltia.Core.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Run the benchmark within the Meltia repository");
    }

    private static string DotnetSdkVersion()
    {
        try
        {
            var info = new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(info);
            if (process is null) return "unknown";
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return "timeout";
            }
            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return "unavailable";
        }
    }

    private static string CpuModel()
    {
        if (File.Exists("/proc/cpuinfo"))
        {
            string? row = File.ReadLines("/proc/cpuinfo")
                .FirstOrDefault(x => x.StartsWith("model name", StringComparison.Ordinal));
            if (row is not null) return row[(row.IndexOf(':') + 1)..].Trim();
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unavailable";
    }
}
