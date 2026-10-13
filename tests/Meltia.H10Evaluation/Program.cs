// SPDX-License-Identifier: GPL-3.0-or-later
// H10 diagnostic runner: both engines see the SAME raw, with NO issue-derived hints.
using System.Diagnostics;
using System.Text.Json;
using Meltia.Core.Boundary;

var input = Path.Combine(AppContext.BaseDirectory, "issue_intents_v2.jsonl");
string output = "artifacts/h10-comparison.json";
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
    else if (args[i] == "--input" && i + 1 < args.Length) input = args[++i];
    else throw new ArgumentException("Usage: --input corpus.jsonl --output report.json");
}
var cases = File.ReadLines(input).Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => JsonSerializer.Deserialize<Case>(x, new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true }) ??
        throw new InvalidDataException("Null diagnostic case")).ToArray();
if (cases.Length == 0 || cases.Select(x => x.Id).Distinct().Count() != cases.Length)
    throw new InvalidDataException("Empty or duplicate H10 corpus");

var old = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true,
    ExperimentalDiverseCandidatePruning: true,
    ExperimentalJapaneseFlankScoring: true));
var prototype = new H10BoundaryPrototype();

// Unlabelled self-tests: fixed behavior, deterministic scores, explicit intent,
// raw integrity, unicode offsets, and the inability to auto-commit.
Check(!prototype.Analyze("tokyo").AutoCommitAllowed, "H10 never commits");
var lockSpan = new Utf16Span(0, 5);
Check(prototype.Analyze("tokyo", new BoundaryEvidence(ExplicitEnglish: [lockSpan]))
    .Routes[0].EnglishSpans.SequenceEqual([lockSpan]), "explicit Latin lock");
Check(prototype.Analyze("tokyo", new BoundaryEvidence(ExplicitJapanese: [lockSpan]))
    .Routes[0].EnglishSpans.Count == 0, "explicit Japanese lock");
Check(prototype.Analyze("https://example.com").Routes[0].EnglishSpans
    .SequenceEqual([new Utf16Span(0, 19)]), "URL unchanged");
Check(prototype.Analyze("dev@example.org").Routes[0].EnglishSpans
    .SequenceEqual([new Utf16Span(0, 15)]), "email unchanged");
var unicode = prototype.Analyze("🌸a");
Check(unicode.Raw == "🌸a" && unicode.Edges.All(e => e.Span.Start != 1 &&
    e.Span.End != 1), "no UTF-16 surrogate splits");
var ambiguous = prototype.Analyze("data");
Check(ambiguous.Edges.Any(x => x.Language == "Japanese") &&
    ambiguous.Edges.Any(x => x.Language == "English"),
    "both language graphs survive ambiguous Latin");
var repeat = prototype.Analyze("kyouhacommitniiku");
Check(repeat.Routes[0].EnglishSpans.SequenceEqual(
    prototype.Analyze("kyouhacommitniiku").Routes[0].EnglishSpans),
    "same input, same H10 result");
bool rejected = false;
try { prototype.Analyze("🌸a", new BoundaryEvidence(
    ExplicitEnglish: [new Utf16Span(1, 3)])); }
catch (ArgumentException) { rejected = true; }
Check(rejected, "surrogate boundary rejected");
rejected = false;
try { prototype.Analyze("abcdef", new BoundaryEvidence(
    ExplicitEnglish: [new Utf16Span(0, 4)],
    ExplicitJapanese: [new Utf16Span(2, 6)])); }
catch (ArgumentException) { rejected = true; }
Check(rejected, "contradictory locks rejected");

// All 47 diagnostic cases are copied unchanged from IncrementalBoundaryLab
// H10's issue-source snapshot. The data includes intentionally contradictory
// intent for identical raw input, and must NOT be used as a learned dictionary.
var diagnostics = new List<CaseReport>();
var timings = new Dictionary<string, List<double>>
{
    ["v1.0-flank-independent"] = [],
    ["H10-dual-channel"] = []
};
foreach (var c in cases)
{
    if (c.Parts is null || string.Concat(c.Parts.Select(x => x.Text)) != c.Raw)
        throw new InvalidDataException("Parts mismatch: " + c.Id);
    var gold = Gold(c);
    var length = c.Raw.Length;
    var oldResult = old.Analyze(c.Raw);
    var h10Result = prototype.Analyze(c.Raw);
    // Warm both engines, then take 5 samples of a single independent Analyze.
    for (int i = 0; i < 5; i++)
    {
        var watch = Stopwatch.StartNew();
        _ = old.Analyze(c.Raw);
        watch.Stop();
        timings["v1.0-flank-independent"].Add(watch.Elapsed.TotalMilliseconds);
        watch.Restart();
        _ = prototype.Analyze(c.Raw);
        watch.Stop();
        timings["H10-dual-channel"].Add(watch.Elapsed.TotalMilliseconds);
    }
    var before = Make("v1.0-flank-independent", oldResult.Candidates.Select(x => x.Span),
        oldResult.Routes, gold, length);
    var after = Make("H10-dual-channel",
        h10Result.Edges.Where(x => x.Language == "English").Select(x => x.Span),
        h10Result.Routes, gold, length);
    foreach (var route in h10Result.Routes)
        ValidateEnglishSpans(route.EnglishSpans, c.Raw);
    diagnostics.Add(new CaseReport(c.Id, c.Family, c.Raw, gold, before, after,
        c.Id.StartsWith("meltype-contrast-", StringComparison.Ordinal)));
}
var stats = new [] { "EN-OOV", "STRUCT", "SEAM", "JA-PROTECT", "CONTEXT", "SCRIPT", "AMBIGUOUS" };
var summaries = stats.Append("TOTAL").ToDictionary(family => family,
    family => Summarize(diagnostics.Where(x => family == "TOTAL" || x.Family == family).ToArray()));
var oldSummary = summaries["TOTAL"]["v1.0-flank-independent"];
var newSummary = summaries["TOTAL"]["H10-dual-channel"];
var report = new
{
    Schema = "meltia-h10-issue-diagnostic-v1",
    Date = "2026-10-14",
    Baseline = "v1.0-flank-independent",
    Candidate = "H10-dual-channel",
    DataKind = "Meltype Issue diagnostic snapshot; not independent holdout or production IME",
    NoUserTextTraining = true,
    Measurement = "Five post-warmup-ish runs per input; local process times include allocation/GC noise, not keyboard E2E",
    Runtime = Environment.Version.ToString(),
    OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    SampleCount = cases.Length,
    Summaries = summaries,
    Timings = timings.ToDictionary(x => x.Key, x => Timing(x.Value)),
    Cases = diagnostics
};
var dir = Path.GetDirectoryName(Path.GetFullPath(output))!;
Directory.CreateDirectory(dir);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions
    { WriteIndented = true }));
Console.WriteLine($"H10 issue diagnostic ({cases.Length} cases, NOT holdout)");
Console.WriteLine("Mode                      CandRecall    TopK    Top1    JA->EN   BoundaryF1");
foreach (var s in new [] { oldSummary, newSummary })
    Console.WriteLine($"{s.Model,-25} {s.CandidateCoverage,3}/{s.Count,-3} {s.TopKExact,3}/{s.Count,-3} " +
        $"{s.Top1Exact,3}/{s.Count,-3} {s.JapaneseFalseEnglish,3}/{s.JapaneseOnly,-3} {s.BoundaryF1:F3}");
Console.WriteLine("Report: " + Path.GetFullPath(output));
return 0;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("H10 selftest failed: " + message);
}
static void ValidateEnglishSpans(IReadOnlyList<Utf16Span> spans, string raw)
{
    int previous = 0;
    foreach (var span in spans)
    {
        if (span.Start < previous || span.Start >= span.End || span.End > raw.Length ||
            SplitPair(raw, span.Start) || SplitPair(raw, span.End))
            throw new InvalidDataException("Invalid predicted English span");
        previous = span.End;
    }
}
static bool SplitPair(string text, int index) => index > 0 && index < text.Length &&
    char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);

// Whitespace is neutral, so "Are you " is two English intervals; literal
// strings remain raw and UTF-16 offsets are never inferred from UTF-8 bytes.
static Utf16Span[] Gold(Case entry)
{
    var intervals = new List<Utf16Span>();
    int pos = 0;
    foreach (var part in entry.Parts)
    {
        if (part.Lang == "en")
        {
            int i = 0;
            while (i < part.Text.Length)
            {
                if (char.IsWhiteSpace(part.Text[i])) { i++; continue; }
                int start = i;
                while (i < part.Text.Length && !char.IsWhiteSpace(part.Text[i])) i++;
                intervals.Add(new Utf16Span(pos + start, pos + i));
            }
        }
        pos += part.Text.Length;
    }
    return intervals.ToArray();
}
static int[] Changes(IReadOnlyList<Utf16Span> spans, int length)
{
    var cuts = new HashSet<int>();
    foreach (var x in spans)
    {
        if (x.Start > 0 && x.Start < length) cuts.Add(x.Start);
        if (x.End > 0 && x.End < length) cuts.Add(x.End);
    }
    return cuts.Order().ToArray();
}
static Prediction Make(string model, IEnumerable<Utf16Span> candidates,
    IReadOnlyList<BoundaryRoute> routes, Utf16Span[] gold, int length)
{
    var proposed = candidates.Distinct().ToArray();
    var first = routes.Count > 0 ? routes[0].EnglishSpans.ToArray() : [];
    var topK = routes.Any(r => r.EnglishSpans.SequenceEqual(gold));
    var top1 = first.SequenceEqual(gold);
    var candidateCoverage = gold.All(proposed.Contains);
    var trueCuts = Changes(gold, length);
    var found = Changes(first, length);
    int tp = found.Intersect(trueCuts).Count();
    return new Prediction(model, proposed.Length, candidateCoverage, topK, top1,
        first, tp, found.Length - tp, trueCuts.Length - tp, !gold.Any() && first.Length > 0,
        !candidateCoverage ? "missing-candidate" : !topK ? "pruned-or-missing-ja" :
        !top1 ? "ranking" : "correct");
}
static Dictionary<string, Summary> Summarize(CaseReport[] rows)
{
    Summary Aggregate(string name, Func<CaseReport, Prediction> select)
    {
        var p = rows.Select(select).ToArray();
        int tp = p.Sum(x => x.TruePositives);
        int fp = p.Sum(x => x.FalsePositives);
        int fn = p.Sum(x => x.FalseNegatives);
        int pure = rows.Count(x => x.Gold.Length == 0);
        double f1 = 2.0 * tp / Math.Max(1, 2 * tp + fp + fn);
        return new Summary(name, rows.Length, p.Count(x => x.CandidateCovered),
            p.Count(x => x.TopKExact), p.Count(x => x.Top1Exact), pure,
            p.Count(x => x.PureJapaneseFalseEnglish), f1,
            p.GroupBy(x => x.Failure).ToDictionary(x => x.Key, x => x.Count()));
    }
    return new Dictionary<string, Summary>
    {
        ["v1.0-flank-independent"] = Aggregate("v1.0-flank-independent", x => x.Before),
        ["H10-dual-channel"] = Aggregate("H10-dual-channel", x => x.After)
    };
}
static object Timing(List<double> input)
{
    var samples = input.Order().ToArray();
    double Percentile(double fraction) => samples[Math.Min(samples.Length - 1,
        (int)Math.Ceiling(fraction * samples.Length) - 1)];
    return new { Samples = samples.Length, P50ms = Percentile(.50),
        P95ms = Percentile(.95), P99ms = Percentile(.99) };
}

sealed record Case(string Id, string Family, string Raw, Segment[] Parts);
sealed record Segment(string Lang, string Text);
sealed record Prediction(string Model, int CandidateCount, bool CandidateCovered,
    bool TopKExact, bool Top1Exact, Utf16Span[] Top1, int TruePositives,
    int FalsePositives, int FalseNegatives, bool PureJapaneseFalseEnglish,
    string Failure);
sealed record CaseReport(string Id, string Family, string Raw, Utf16Span[] Gold,
    Prediction Before, Prediction After, bool IsContrastPair);
sealed record Summary(string Model, int Count, int CandidateCoverage, int TopKExact,
    int Top1Exact, int JapaneseOnly, int JapaneseFalseEnglish, double BoundaryF1,
    Dictionary<string, int> FailureStages);
