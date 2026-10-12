// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Reproducible diagnostic workload, not a Windows TSF latency claim.
// The CI runner is noisy: do not set a hard latency gate using this sample.
using System.Diagnostics;
using System.Text.Json;
using Meltia.Core.Boundary;

if (args.Length != 2 || args[0] != "--output" ||
    string.IsNullOrWhiteSpace(args[1]))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Meltia.BoundaryBenchmark -- --output output.json");
    return 64;
}
var scenarios = new (string Name, string Raw, int Samples)[]
{
    ("japanese-short", "watashihakyounihongowobenkyousuruyoteidesu", 28),
    ("english-middle", "kyouhaquaternionnohyperspectralwotukau", 28),
    ("three-english-islands", "watashihaquaternionnohyperspectraldeinteroperabilitysuru", 24),
    ("protected-url", "kyouhahttps://example.org/docsnipushsuru", 24),
    ("mixed-emoji", "kyouha😀compilerdefirmwarewotukau", 24),
    ("japanese-1024", string.Concat(Enumerable.Repeat("watashihakyounihongowobenkyousuru", 40))[..1024], 8),
    ("latin-96", new string('q', 96), 12),
    ("japanese-long-fully-readable",
        string.Concat(Enumerable.Repeat("nihongo", 22)), 8),
    ("long-single-english-middle",
        string.Concat(Enumerable.Repeat("nihongo", 16)) + "neuroscience" +
        string.Concat(Enumerable.Repeat("nihongo", 5)), 8),
    ("long-two-english-islands",
        string.Concat(Enumerable.Repeat("nihongo", 11)) +
        "cryptography" + "no" + "semiconductor" +
        string.Concat(Enumerable.Repeat("nihongo", 7)), 8),
    ("long-overloaded-repeated-mixed",
        string.Concat(Enumerable.Repeat(
            "nihongocryptographynosemiconductordephotonicsga", 12)), 8),
};
var engines = new (string Version, MeltiaBoundaryEngine Engine)[]
{
    ("v0.9-reference", new(new MeltiaBoundaryOptions(
        ExperimentalUnknownLatinCandidates: true,
        ExperimentalPhoneticLatticeDecoder: true,
        ExperimentalContextualIslandScoring: true))),
    ("v1.0-review", new(new MeltiaBoundaryOptions(
        ExperimentalUnknownLatinCandidates: true,
        ExperimentalPhoneticLatticeDecoder: true,
        ExperimentalContextualIslandScoring: true,
        ExperimentalUncertaintyReview: true))),
    ("v1.0-windowed", new(new MeltiaBoundaryOptions(
        ExperimentalUnknownLatinCandidates: true,
        ExperimentalPhoneticLatticeDecoder: true,
        ExperimentalContextualIslandScoring: true,
        ExperimentalUncertaintyReview: true,
        ExperimentalLongRunWindowing: true))),
    ("v1.0-diverse", new(new MeltiaBoundaryOptions(
        ExperimentalUnknownLatinCandidates: true,
        ExperimentalPhoneticLatticeDecoder: true,
        ExperimentalContextualIslandScoring: true,
        ExperimentalUncertaintyReview: true,
        ExperimentalLongRunWindowing: true,
        ExperimentalDiverseCandidatePruning: true))),
};
var results = new List<ReportRow>();
var count = 0;
foreach (var (name, raw, samples) in scenarios)
{
    // v1.0 adds review metadata but must not change the actual routes.
    // Check complete score/candidate/path parity before measuring performance.
    var oldResult = engines[0].Engine.Analyze(raw);
    var newResult = engines[1].Engine.Analyze(raw);
    if (oldResult.Review is not null || newResult.Review is null ||
        !oldResult.Candidates.SequenceEqual(newResult.Candidates) ||
        oldResult.Routes.Count != newResult.Routes.Count ||
        !oldResult.Routes.Zip(newResult.Routes, (left, right) =>
            left.Score.Equals(right.Score) &&
            left.EnglishSpans.SequenceEqual(right.EnglishSpans)).All(equal => equal))
        throw new InvalidOperationException(
            "v1.0 benchmark route or candidate regression: " + name);
    foreach (var (version, engine) in engines)
    {
        // Same immutable raw string for both modes, warm the JIT and caches.
        for (var i = 0; i < 4; i++)
        {
            var warm = engine.Analyze(raw);
            if (warm.Raw != raw || warm.AutoCommitAllowed)
                throw new InvalidOperationException("Warmup violated preservation contract");
        }
        var elapsed = new double[samples];
        var allocated = new long[samples];
        var candidates = new int[samples];
        var regions = new int[samples];
        int truncated = 0;
        for (var i = 0; i < samples; i++)
        {
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var result = engine.Analyze(raw);
            elapsed[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            candidates[i] = result.Candidates.Count;
            regions[i] = result.Candidates.Where(c =>
                c.Origin == "v0.7-unknown-latin-provisional")
                .Select(c => c.Span.Start / 24).Distinct().Count();
            if (result.Truncated) truncated++;
            if (result.Raw != raw || result.Candidates.Count > MeltiaBoundaryEngine.MaxCandidates ||
                result.Routes.Count is < 1 or > MeltiaBoundaryEngine.MaxRoutes ||
                result.AutoCommitAllowed ||
                (version != "v0.9-reference" && result.Review is null) ||
                (version == "v0.9-reference" && result.Review is not null))
                throw new InvalidOperationException("Benchmark violated bounded/no-commit contract: " + name);
            count += result.Candidates.Count + result.Routes.Count;
        }
        Array.Sort(elapsed);
        Array.Sort(allocated);
        Array.Sort(candidates);
        Array.Sort(regions);
        results.Add(new ReportRow(name, version, raw.Length, samples,
            Median(elapsed), Percentile(elapsed, .95), Percentile(elapsed, .99),
            Median(allocated), Percentile(allocated, .95),
            candidates[samples / 2], candidates[^1], truncated,
            regions[samples / 2]));
    }
}
var report = new
{
    Schema = "meltia/boundary-performance-v1",
    Version = "1.0.0-dev",
    Environment = new {
        Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ProcessorCount = Environment.ProcessorCount,
        Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    },
    Measurement = "Stopwatch elapsed time per offline Analyze call; GC.GetAllocatedBytesForCurrentThread; warmed process, non-TSF",
    Caveat = "Runner load, process startup and JIT effects mean p95/p99 are exploratory; cannot infer Windows IME keystroke tail latency.",
    Rows = results,
    Comparisons = results.Where(x => x.Model == "v1.0-review").Select(
        updated => new {
            updated.Scenario,
            P50LatencyRatio = updated.P50Ms /
                Math.Max(results.Single(x => x.Scenario == updated.Scenario &&
                    x.Model == "v0.9-reference").P50Ms, .000001),
            P95LatencyRatio = updated.P95Ms /
                Math.Max(results.Single(x => x.Scenario == updated.Scenario &&
                    x.Model == "v0.9-reference").P95Ms, .000001),
            P50AllocationRatio = (double)updated.P50AllocatedBytes /
                Math.Max(1L, results.Single(x => x.Scenario == updated.Scenario &&
                    x.Model == "v0.9-reference").P50AllocatedBytes),
        }),
    WindowedComparisons = results.Where(x => x.Model == "v1.0-windowed").Select(
        updated => new {
            updated.Scenario,
            updated.CandidateMedian,
            updated.TruncatedSamples,
            BaselineCandidateMedian = results.Single(x =>
                x.Scenario == updated.Scenario && x.Model == "v1.0-review").CandidateMedian,
            P50LatencyRatio = updated.P50Ms / Math.Max(.000001,
                results.Single(x => x.Scenario == updated.Scenario &&
                    x.Model == "v1.0-review").P50Ms),
            P95LatencyRatio = updated.P95Ms / Math.Max(.000001,
                results.Single(x => x.Scenario == updated.Scenario &&
                    x.Model == "v1.0-review").P95Ms),
            P50AllocationRatio = (double)updated.P50AllocatedBytes /
                Math.Max(1L, results.Single(x => x.Scenario == updated.Scenario &&
                    x.Model == "v1.0-review").P50AllocatedBytes),
        }),
    DiverseComparisons = results.Where(x => x.Model == "v1.0-diverse").Select(
        selected => new {
            selected.Scenario,
            selected.CandidateMedian,
            selected.TruncatedSamples,
            selected.SpeculativeStartRegionsMedian,
            BaselineStartRegionsMedian = results.Single(x =>
                x.Scenario == selected.Scenario &&
                x.Model == "v1.0-windowed").SpeculativeStartRegionsMedian,
            P50LatencyRatio = selected.P50Ms /
                Math.Max(.000001, results.Single(x =>
                    x.Scenario == selected.Scenario &&
                    x.Model == "v1.0-windowed").P50Ms),
            P95LatencyRatio = selected.P95Ms /
                Math.Max(.000001, results.Single(x =>
                    x.Scenario == selected.Scenario &&
                    x.Model == "v1.0-windowed").P95Ms),
            P50AllocationRatio = (double)selected.P50AllocatedBytes /
                Math.Max(1L, results.Single(x =>
                    x.Scenario == selected.Scenario &&
                    x.Model == "v1.0-windowed").P50AllocatedBytes),
        }),
    WorkSink = count
};
var file = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(file)!);
File.WriteAllText(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (var row in results)
    Console.WriteLine($"{row.Scenario} {row.Model} utf16={row.Utf16Length} " +
        $"p50={row.P50Ms:F3}ms p95={row.P95Ms:F3}ms p99={row.P99Ms:F3}ms " +
        $"alloc-p50={row.P50AllocatedBytes}B " +
        $"candidates={row.CandidateMedian} truncated={row.TruncatedSamples}/{row.Samples}");
Console.WriteLine("Wrote " + file);
return 0;

static double Median(double[] values) => Percentile(values, .50);
static long Median(long[] values) => Percentile(values, .50);
static double Percentile(double[] values, double p) =>
    values[(int)Math.Ceiling((values.Length - 1) * p)];
static long Percentile(long[] values, double p) =>
    values[(int)Math.Ceiling((values.Length - 1) * p)];

internal sealed record ReportRow(string Scenario, string Model, int Utf16Length,
    int Samples, double P50Ms, double P95Ms, double P99Ms,
    long P50AllocatedBytes, long P95AllocatedBytes,
    int CandidateMedian, int CandidateMax, int TruncatedSamples,
    int SpeculativeStartRegionsMedian);
