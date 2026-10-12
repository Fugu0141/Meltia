// SPDX-License-Identifier: GPL-3.0-or-later
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
};
var results = new List<ReportRow>();
var count = 0;
foreach (var (name, raw, samples) in scenarios)
{
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
        for (var i = 0; i < samples; i++)
        {
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var result = engine.Analyze(raw);
            elapsed[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            if (result.Raw != raw || result.Candidates.Count > MeltiaBoundaryEngine.MaxCandidates ||
                result.Routes.Count is < 1 or > MeltiaBoundaryEngine.MaxRoutes ||
                result.AutoCommitAllowed ||
                (version == "v1.0-review" && result.Review is null) ||
                (version == "v0.9-reference" && result.Review is not null))
                throw new InvalidOperationException("Benchmark violated bounded/no-commit contract: " + name);
            count += result.Candidates.Count + result.Routes.Count;
        }
        Array.Sort(elapsed);
        Array.Sort(allocated);
        results.Add(new ReportRow(name, version, raw.Length, samples,
            Median(elapsed), Percentile(elapsed, .95), Percentile(elapsed, .99),
            Median(allocated), Percentile(allocated, .95)));
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
    WorkSink = count
};
var file = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(file)!);
File.WriteAllText(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (var row in results)
    Console.WriteLine($"{row.Scenario} {row.Model} utf16={row.Utf16Length} " +
        $"p50={row.P50Ms:F3}ms p95={row.P95Ms:F3}ms p99={row.P99Ms:F3}ms " +
        $"alloc-p50={row.P50AllocatedBytes}B");
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
    long P50AllocatedBytes, long P95AllocatedBytes);
