// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// H10 diagnostic runner: both engines see the SAME raw, with NO issue-derived hints.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meltia.Core.Boundary;

var input = Path.Combine(AppContext.BaseDirectory, "issue_intents_v2.jsonl");
string output = "artifacts/h10-comparison.json";
string? verifyBaseline = null;
bool emitBaseline = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
    else if (args[i] == "--input" && i + 1 < args.Length) input = args[++i];
    else if (args[i] == "--verify-baseline" && i + 1 < args.Length)
        verifyBaseline = args[++i];
    else if (args[i] == "--emit-baseline") emitBaseline = true;
    else throw new ArgumentException(
        "Usage: --input corpus.jsonl --output report.json " +
        "[--verify-baseline frozen.jsonl] [--emit-baseline]");
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
var challenger = new H11BoundaryPrototype();

// H11 is an independent, reversible comparison hypothesis, never a host IME.
var h11Locked = challenger.Analyze("tokyo",
    new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)]));
Check(h11Locked.Routes[0].EnglishSpans.SequenceEqual([new Utf16Span(0, 5)]),
    "H11 respects explicit English");
Check(challenger.Analyze("tokyo",
    new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)]))
    .Routes[0].EnglishSpans.Count == 0, "H11 respects explicit Japanese");
Check(!challenger.Analyze("data").AutoCommitAllowed, "H11 does not auto-commit");
Check(challenger.Analyze("🌸a").Edges.All(x => x.Span.Start != 1 &&
    x.Span.End != 1), "H11 no surrogate splits");
Check(challenger.Analyze("https://example.com").Routes[0].EnglishSpans
    .SequenceEqual([new Utf16Span(0, 19)]), "H11 protects URL");
Check(challenger.Analyze("dev@example.org").Routes[0].EnglishSpans
    .SequenceEqual([new Utf16Span(0, 15)]), "H11 protects email");
Check(challenger.Analyze("50ccgenntuki").Edges.Any(x =>
    x.Language == "English" && x.Span == new Utf16Span(0, 4)),
    "H11 generates numeric English word boundaries");
Check(challenger.Analyze("buy me a coffee").Edges.Any(x =>
    x.Language == "English" && x.Span == new Utf16Span(7, 8)),
    "H11 generates isolated one-letter English words");
var h11Repeat = challenger.Analyze("kyouhashoppingniiku");
Check(h11Repeat.Routes[0].EnglishSpans.SequenceEqual(
    challenger.Analyze("kyouhashoppingniiku").Routes[0].EnglishSpans),
    "H11 deterministic decoded route");


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

// H11 robustness: no fixed Issue vocabulary, no path mutation, no surrogate
// split, and no hidden nondeterminism over mixed scripts and edits.
var h11Fragments = new[]
{
    "nihongo", "koreha", "GitHub", "release", "tab", "de", "ga",
    "to", "さ", "😀", " ", "-", "50", "cc", "yappari", "xyz"
};
var h11Seed = new Random(20261014);
for (int sample = 0; sample < 160; sample++)
{
    var rawSample = string.Concat(Enumerable.Range(0, h11Seed.Next(1, 9))
        .Select(_ => h11Fragments[h11Seed.Next(h11Fragments.Length)]));
    var first = challenger.Analyze(rawSample);
    var second = challenger.Analyze(rawSample);
    Check(first.Raw == rawSample && second.Raw == rawSample &&
        !first.AutoCommitAllowed &&
        first.Routes.Count is > 0 and <= 8 &&
        first.Routes[0].EnglishSpans.SequenceEqual(
            second.Routes[0].EnglishSpans) &&
        first.Routes[0].Score.Equals(second.Routes[0].Score),
        "H11 160 seeded mixed-script deterministic cases");
    foreach (var route in first.Routes)
        ValidateEnglishSpans(route.EnglishSpans, rawSample);
    foreach (var edge in first.Edges)
        Check(edge.Span.Start >= 0 && edge.Span.End <= rawSample.Length &&
            edge.Span.Start < edge.Span.End &&
            !SplitPair(rawSample, edge.Span.Start) &&
            !SplitPair(rawSample, edge.Span.End),
            "H11 candidates preserve UTF-16 boundaries");
}
var h11Many = string.Concat(Enumerable.Range(0, 10).Select(_ => "nihongo"));
Check(h11Many.Length < H11BoundaryPrototype.MaxInputLength &&
    challenger.Analyze(h11Many).Routes.Count is > 0 and <= 8,
    "H11 long phonetic run remains bounded");
bool h11BadRange = false;
try { challenger.Analyze("😀a", new BoundaryEvidence(
    ExplicitEnglish: [new Utf16Span(1, 2)])); }
catch (ArgumentException) { h11BadRange = true; }
Check(h11BadRange, "H11 rejects explicit UTF-16 surrogate split");
h11BadRange = false;
try { challenger.Analyze("abcdef", new BoundaryEvidence(
    ExplicitEnglish: [new Utf16Span(0, 4)],
    ExplicitJapanese: [new Utf16Span(2, 6)])); }
catch (ArgumentException) { h11BadRange = true; }
Check(h11BadRange, "H11 rejects overlapping contradictory locks");

// Edit consistency oracle: session edit operations must match a full decode.
var session = new H10PrototypeSession(prototype);
session.Insert("kyouha🌸commitniiku");
var full = prototype.Analyze(session.Raw);
Check(session.Analyze().Routes[0].EnglishSpans.SequenceEqual(
    full.Routes[0].EnglishSpans), "session append/full reanalysis consistency");
session.MoveCaret(6); // after kyouha, before the surrogate pair
session.DeleteForward(); // delete the full surrogate pair
Check(session.Raw == "kyouhacommitniiku" &&
    session.Analyze().Routes[0].EnglishSpans.SequenceEqual(
        prototype.Analyze("kyouhacommitniiku").Routes[0].EnglishSpans),
    "UTF-16 forward deletion and full reanalysis consistency");
session.MoveCaret(session.Raw.Length);
session.Backspace();
session.Insert("u");
Check(session.Analyze().Routes[0].EnglishSpans.SequenceEqual(
    prototype.Analyze(session.Raw).Routes[0].EnglishSpans),
    "backspace/retype produces the same route");
var editSeed = new Random(20261014);
for (int iteration = 0; iteration < 120; iteration++)
{
    var positions = Enumerable.Range(0, session.Raw.Length + 1)
        .Where(x => x == 0 || x == session.Raw.Length ||
            !(char.IsHighSurrogate(session.Raw[x - 1]) &&
              char.IsLowSurrogate(session.Raw[x]))).ToArray();
    session.MoveCaret(positions[editSeed.Next(positions.Length)]);
    switch (editSeed.Next(3))
    {
        case 0:
            if (session.Raw.Length < 200)
                session.Insert(new [] { "ni", "github", "to", "🌸", "A" }
                    [editSeed.Next(5)]);
            break;
        case 1: session.Backspace(); break;
        default: session.DeleteForward(); break;
    }
    var a = session.Analyze();
    var b = prototype.Analyze(session.Raw);
    Check(a.Raw == b.Raw && a.Routes[0].EnglishSpans.SequenceEqual(
        b.Routes[0].EnglishSpans), "seeded edited state matches full decode");
}

// All 47 diagnostic cases are copied unchanged from IncrementalBoundaryLab
// H10's issue-source snapshot. The data includes intentionally contradictory
// intent for identical raw input, and must NOT be used as a learned dictionary.
var diagnostics = new List<CaseReport>();
var frozen = new List<H11CaseSnapshot>(cases.Length);
string corpusName = Path.GetFileName(input);
string corpusSha = H11Snapshot.CorpusSha(input);
var timings = new Dictionary<string, List<double>>
{
    ["v1.0-flank-independent"] = [],
    ["H10-dual-channel"] = [],
    ["H11-word-phonetic"] = []
};
foreach (var c in cases)
{
    if (c.Parts is null || string.Concat(c.Parts.Select(x => x.Text)) != c.Raw)
        throw new InvalidDataException("Parts mismatch: " + c.Id);
    var gold = Gold(c);
    var length = c.Raw.Length;
    var oldResult = old.Analyze(c.Raw);
    var h10Result = prototype.Analyze(c.Raw);
    var h11Result = challenger.Analyze(c.Raw);
    frozen.Add(H11Snapshot.Capture(corpusName, c.Id, corpusSha, h11Result));
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
        watch.Restart();
        _ = challenger.Analyze(c.Raw);
        watch.Stop();
        timings["H11-word-phonetic"].Add(watch.Elapsed.TotalMilliseconds);
    }
    var before = Make("v1.0-flank-independent", oldResult.Candidates.Select(x => x.Span),
        oldResult.Routes, gold, length);
    var after = Make("H10-dual-channel",
        h10Result.Edges.Where(x => x.Language == "English").Select(x => x.Span),
        h10Result.Routes, gold, length);
    var updated = Make("H11-word-phonetic",
        h11Result.Edges.Where(x => x.Language == "English").Select(x => x.Span),
        h11Result.Routes, gold, length);
    foreach (var route in h10Result.Routes)
        ValidateEnglishSpans(route.EnglishSpans, c.Raw);
    foreach (var route in h11Result.Routes)
        ValidateEnglishSpans(route.EnglishSpans, c.Raw);
    diagnostics.Add(new CaseReport(c.Id, c.Family, c.Raw, gold, before, after,
        c.Id.StartsWith("meltype-contrast-", StringComparison.Ordinal),
        c.SourceUrl, c.IssueStateAtCollection, c.Qualification, c.Provenance,
        updated));
}
var stats = cases.Select(x => x.Family).Distinct(StringComparer.Ordinal)
    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
var summaries = stats.Append("TOTAL").ToDictionary(family => family,
    family => Summarize(diagnostics.Where(x => family == "TOTAL" || x.Family == family).ToArray()));
var oldSummary = summaries["TOTAL"]["v1.0-flank-independent"];
var newSummary = summaries["TOTAL"]["H10-dual-channel"];
var h11Summary = summaries["TOTAL"]["H11-word-phonetic"];
var report = new
{
    Schema = "meltia-h10-issue-diagnostic-v1",
    Date = "2026-10-14",
    Baseline = "v1.0-flank-independent",
    Candidate = "H10-dual-channel",
    NextCandidate = "H11-word-phonetic",
    ExperimentalCandidateSource = "PRE-EXISTING dictionaries/english.txt embedded as read-only baseline lexicon",
    DataKind = "Meltype Issue diagnostic snapshot; not independent holdout or production IME",
    SourceIssueStates = diagnostics.GroupBy(x => x.IssueStateAtCollection ?? "unrecorded")
        .ToDictionary(x => x.Key, x => x.Count()),
    SourceIssueCount = diagnostics.Where(x => x.SourceUrl is not null)
        .Select(x => x.SourceUrl).Distinct().Count(),
    NoUserTextTraining = true,
    Measurement = "Five post-warmup-ish runs per input; local process times include allocation/GC noise, not keyboard E2E",
    Runtime = Environment.Version.ToString(),
    OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    SampleCount = cases.Length,
    CorpusSha256 = corpusSha,
    DictionarySha256 = H11Snapshot.EmbeddedDictionarySha(),
    RepositoryCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-unspecified",
    SnapshotKind = "Immutable diagnostic fingerprint: candidates, routes, order, scores, confirmation",
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
foreach (var s in new [] { oldSummary, newSummary, h11Summary })
    Console.WriteLine($"{s.Model,-25} {s.CandidateCoverage,3}/{s.Count,-3} {s.TopKExact,3}/{s.Count,-3} " +
        $"{s.Top1Exact,3}/{s.Count,-3} {s.JapaneseFalseEnglish,3}/{s.JapaneseOnly,-3} {s.BoundaryF1:F3}");
foreach (var family in stats)
{
    var a = summaries[family]["v1.0-flank-independent"];
    var b = summaries[family]["H10-dual-channel"];
    var d = summaries[family]["H11-word-phonetic"];
    Console.WriteLine($"FAMILY {family,-12} Top1 {a.Top1Exact}/{a.Count} -> " +
        $"{b.Top1Exact}/{b.Count} -> {d.Top1Exact}/{d.Count}; pure-JA false English " +
        $"{a.JapaneseFalseEnglish} -> {b.JapaneseFalseEnglish} -> {d.JapaneseFalseEnglish}");
}
foreach (var group in diagnostics.GroupBy(x => x.IssueStateAtCollection ?? "unrecorded"))
{
    var s = Summarize(group.ToArray());
    var a = s["v1.0-flank-independent"];
    var b = s["H10-dual-channel"];
    var d = s["H11-word-phonetic"];
    Console.WriteLine($"ISSUE_STATE {group.Key}: n={group.Count()} " +
        $"Top1 {a.Top1Exact}->{b.Top1Exact}->{d.Top1Exact}; JA false English " +
        $"{a.JapaneseFalseEnglish}->{b.JapaneseFalseEnglish}->{d.JapaneseFalseEnglish}");
}
foreach (var c in diagnostics.Where(x => !x.After.Top1Exact))
    Console.WriteLine($"H10_ERROR {c.Id}: {c.Raw} | {c.After.Failure} " +
        $"| expected {string.Join(",", c.Gold.Select(s => $"{s.Start}-{s.End}"))} " +
        $"| top1 {string.Join(",", c.After.Top1.Select(s => $"{s.Start}-{s.End}"))}");
foreach (var c in diagnostics.Where(x => x.H11 is { Top1Exact: false }))
    Console.WriteLine($"H11_ERROR {c.Id}: {c.Raw} | {c.H11!.Failure} " +
        $"| expected {string.Join(",", c.Gold.Select(s => $"{s.Start}-{s.End}"))} " +
        $"| top1 {string.Join(",", c.H11!.Top1.Select(s => $"{s.Start}-{s.End}"))}");
foreach (var entry in timings)
{
    var samples = entry.Value.Order().ToArray();
    double P(double quantile) => samples[Math.Min(samples.Length - 1,
        (int)Math.Ceiling(quantile * samples.Length) - 1)];
    Console.WriteLine($"TIMING {entry.Key}: samples={samples.Length}, " +
        $"p50={P(.50):F4}ms, p95={P(.95):F4}ms, p99={P(.99):F4}ms");
}
Console.WriteLine("Report: " + Path.GetFullPath(output));
if (emitBaseline) H11Snapshot.Emit(frozen);
if (verifyBaseline is not null) H11Snapshot.Verify(verifyBaseline, corpusName, frozen);
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
        ["H10-dual-channel"] = Aggregate("H10-dual-channel", x => x.After),
        ["H11-word-phonetic"] = Aggregate("H11-word-phonetic", x =>
            x.H11 ?? throw new InvalidDataException("Missing H11 prediction"))
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

sealed record Case(string Id, string Family, string Raw, Segment[] Parts,
    [property: JsonPropertyName("source_url")] string? SourceUrl = null,
    [property: JsonPropertyName("issue_state_at_collection")] string? IssueStateAtCollection = null,
    [property: JsonPropertyName("qualification")] string? Qualification = null,
    [property: JsonPropertyName("provenance")] string? Provenance = null);
sealed record Segment(string Lang, string Text);
sealed record Prediction(string Model, int CandidateCount, bool CandidateCovered,
    bool TopKExact, bool Top1Exact, Utf16Span[] Top1, int TruePositives,
    int FalsePositives, int FalseNegatives, bool PureJapaneseFalseEnglish,
    string Failure);
sealed record CaseReport(string Id, string Family, string Raw, Utf16Span[] Gold,
    Prediction Before, Prediction After, bool IsContrastPair,
    string? SourceUrl, string? IssueStateAtCollection, string? Qualification,
    string? Provenance, Prediction? H11);
sealed record Summary(string Model, int Count, int CandidateCoverage, int TopKExact,
    int Top1Exact, int JapaneseOnly, int JapaneseFalseEnglish, double BoundaryF1,
    Dictionary<string, int> FailureStages);
