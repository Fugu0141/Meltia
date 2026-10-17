// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Only the public Meltype Issue diagnostic corpus is permitted in CI.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meltia.Core.Boundary;

string root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "src/Meltia.Core/Meltia.Core.csproj")))
{
    root = Path.GetDirectoryName(root)
        ?? throw new DirectoryNotFoundException("Run in Meltia repository");
}
string output = "artifacts/h11-trace/phase1-public-issue-diagnostics.json";
if (args.Length == 2 && args[0] == "--output")
    output = args[1];
else if (args.Length != 0)
    throw new ArgumentException("Usage: [--output path]");

var engine = new H11BoundaryPrototype();
var files = new[] { "issue_intents_v2.jsonl", "issue_intents_extra_20261014.jsonl" };
var summaries = new List<object>();
int total = 0, top1 = 0, topk = 0, missing = 0, pruned = 0, omitted = 0;
int checkedBeamDrops = 0, multiUnitBeamDrops = 0;
var observed = new List<(string Name, string Id, H11CaseSnapshot Snapshot)>();
foreach (string file in files)
{
    string path = Path.Combine(root, "tests/Meltia.H10Evaluation", file);
    string corpusSha = H11Snapshot.CorpusSha(path);
    var snapshots = new List<H11CaseSnapshot>();
    foreach (string line in File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x)))
    {
        using var json = JsonDocument.Parse(line);
        var doc = json.RootElement;
        string id = doc.GetProperty("id").GetString()!;
        string raw = doc.GetProperty("raw").GetString()!;
        var gold = Gold(doc.GetProperty("parts"), raw);
        // No labels are passed to either inference call.
        var baseline = engine.Analyze(raw);
        var traced = engine.AnalyzeWithTrace(raw);
        var oldSnapshot = H11Snapshot.Capture(file, id, corpusSha, baseline);
        var newSnapshot = H11Snapshot.Capture(file, id, corpusSha, traced.Result);
        Check(JsonSerializer.Serialize(oldSnapshot) == JsonSerializer.Serialize(newSnapshot),
            "OFF/ON snapshot mismatch: " + id);
        snapshots.Add(newSnapshot);
        observed.Add((file, id, newSnapshot));
        var trace = traced.Trace;
        Check(trace.Edges.Count == traced.Result.Edges.Count,
            "missing trace candidates: " + id);
        Check(trace.Routes.Count == traced.Result.Routes.Count,
            "missing diagnostic routes: " + id);
        foreach (var drop in trace.BeamDrops)
        {
            checkedBeamDrops++;
            // The reported position is dp[edge.End, last], NOT the edge's
            // originating cursor position. Check multi-unit edges explicitly.
            Check(drop.AtUtf16Position == drop.IncomingEdgeSpan.End &&
                drop.IncomingEdgeSpan.Start < drop.AtUtf16Position &&
                drop.AtUtf16Position <= raw.Length,
                "beam event must point to its incoming edge destination: " + id);
            if (drop.IncomingEdgeSpan.Length > 1) multiUnitBeamDrops++;
            Check(drop.Reason ==
                H11BoundaryPrototype.DiagnosticBeamDrop.ClassifyReason(
                    drop.DroppedScore, drop.LowestKeptScore),
                "beam drop reason must match scores: " + id);
        }
        Check(trace.TotalBeamDropped ==
            trace.BeamDrops.Count + trace.OmittedBeamDropEvents,
            "beam event cap accounting mismatch: " + id);
        foreach (var edge in trace.Edges)
        {
            double subtotal = edge.Contributions.Sum(x => x.Value) +
                edge.RoundingResidual;
            Check(Math.Abs(subtotal - edge.LocalScore) < 1e-9,
                "score terms mismatch: " + id);
            // Significant residuals imply a bug in the feature explanation.
            Check(Math.Abs(edge.RoundingResidual) < 1e-8,
                "named contributions do not explain local score: " + id +
                " " + edge.Origin + " " + edge.RawUtf16Span);
        }
        foreach (var route in trace.Routes)
        {
            double previous = 0;
            foreach (var step in route.Steps)
            {
                Check(Math.Abs(previous - step.PreviousScore) < 1e-10,
                    "broken predecessor score: " + id);
                double computed = previous + step.LocalScore +
                    step.LanguageSwitchPenalty + step.EnglishJapaneseSeamBonus;
                Check(Math.Abs(computed - step.CumulativeScore) < 1e-9,
                    "transition score mismatch: " + id);
                previous = step.CumulativeScore;
            }
            Check(Math.Abs(previous - route.Score) < 1e-9,
                "route total score mismatch: " + id);
        }
        var comparison = trace.CompareToGold(gold);
        bool match = traced.Result.Routes[0].EnglishSpans.SequenceEqual(gold);
        bool inK = traced.Result.Routes.Any(x => x.EnglishSpans.SequenceEqual(gold));
        Check(comparison.AppearsInTopK == inK && (comparison.Status == "correct") == match,
            "wrong post-hoc ranking classification: " + id);
        Check(comparison.ExistsInCandidateGraph == comparison.BestGoldScore.HasValue,
            "inconsistent gold feasibility: " + id);
        if (comparison.ExistsInCandidateGraph)
        {
            double reconstructed = comparison.BestGoldSteps.Count == 0 ? 0 :
                comparison.BestGoldSteps[^1].CumulativeScore;
            Check(Math.Abs(reconstructed - comparison.BestGoldScore!.Value) < 1e-9,
                "gold route score mismatch: " + id);
            foreach (var step in comparison.BestGoldSteps)
                Check(trace.Edges.Any(e => e.RawUtf16Span == step.RawUtf16Span &&
                    e.Language == step.Language && e.Origin == step.Origin &&
                    e.LocalScore == step.LocalScore),
                    "gold path synthesized an unavailable edge: " + id);
        }
        top1 += match ? 1 : 0;
        topk += inK ? 1 : 0;
        missing += comparison.Status == "missing_candidate" ? 1 : 0;
        pruned += comparison.Status == "path_pruned" ? 1 : 0;
        omitted += comparison.Status == "topk_omitted" ? 1 : 0;
        total++;
        summaries.Add(new
        {
            Corpus = file, Id = id, InputUtf16Length = raw.Length,
            RawSha256 = Sha(raw), Gold = gold, Comparison = comparison,
            Trace = new {
                trace.Edges, trace.Rejections, trace.BeamDrops,
                trace.Routes, trace.Top1MinusTop2,
                trace.TotalBeamDropped, trace.OmittedBeamDropEvents
            }
        });
    }
    // Validate against frozen 103-case graph/order/score/dictionary baseline.
    H11Snapshot.Verify(Path.Combine(root,
        "tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl"), file, snapshots);
}
Check(total == 103 && top1 == 72 && topk == 94,
    $"Frozen public Issue metrics changed: top1 {top1}/{total}, topk {topk}/{total}");
Check(total - top1 == 31, "Expected 31 unresolved diagnostic examples");
Check(checkedBeamDrops > 0 && multiUnitBeamDrops > 0,
    "103-case fixture must cover beam pruning on a multi-UTF-16-unit edge");

// Deterministic equal-score regression: the diagnostic label must not
// claim stable ordering (List.Sort has no stable-tie contract).
Check(H11BoundaryPrototype.DiagnosticBeamDrop.ClassifyReason(7.25, 7.25) ==
    "tie-at-width", "score-tie must be reported without implying stability");
Check(H11BoundaryPrototype.DiagnosticBeamDrop.ClassifyReason(7.24, 7.25) ==
    "beam-width-overflow", "non-tie drop classification changed");
var tieFixture = engine.AnalyzeWithTrace(new string('a', 30));
Check(tieFixture.Trace.BeamDrops.All(drop => drop.Reason ==
    H11BoundaryPrototype.DiagnosticBeamDrop.ClassifyReason(
        drop.DroppedScore, drop.LowestKeptScore)),
    "synthetic repetitive input must classify every beam drop by its scores");

var selected = new[] { "yaml", "new", "hotelltu", "Are you sushi?",
    "handanwodelegatesuru" };
var probeCases = new List<object>();
foreach (string raw in selected)
{
    var diag = engine.AnalyzeWithTrace(raw);
    Check(!diag.Result.AutoCommitAllowed, "trace cannot auto-commit");
    // These are unlabeled probes; don't guess gold from a feature hypothesis.
    probeCases.Add(new { RawSha256 = Sha(raw), InputUtf16Length = raw.Length,
        diag.Trace.Edges, diag.Trace.Routes, diag.Trace.BeamDrops,
        diag.Trace.Rejections, diag.Trace.Top1MinusTop2 });
}
Check(selected.All(raw => summaries.Any(s =>
    JsonSerializer.Serialize(s).Contains(Sha(raw), StringComparison.Ordinal)) ||
    probeCases.Any(s => JsonSerializer.Serialize(s).Contains(Sha(raw),
        StringComparison.Ordinal))), "missing required probe");

void RejectBoth(string raw, BoundaryEvidence evidence)
{
    bool plainRejected = false, traceRejected = false;
    try { engine.Analyze(raw, evidence); } catch (ArgumentException) { plainRejected = true; }
    try { engine.AnalyzeWithTrace(raw, evidence); }
    catch (ArgumentException) { traceRejected = true; }
    Check(plainRejected && traceRejected, "trace must preserve invalid-lock exceptions");
}
// A one-letter x has no English candidate, even though it has a legal
// Japanese fallback. This proves missing_candidate is distinct from pruning.
var noEnglishEdge = engine.AnalyzeWithTrace("x");
var absentEnglish = noEnglishEdge.Trace.CompareToGold([new Utf16Span(0, 1)]);
Check(absentEnglish.Status == "missing_candidate" &&
    !absentEnglish.ExistsInCandidateGraph &&
    absentEnglish.BestGoldScore is null &&
    absentEnglish.BestGoldSteps.Count == 0,
    "gold comparison must not fabricate missing English candidates");
Check(noEnglishEdge.Trace.Rejections.Any(r =>
    r.Reason == "generation-skipped/english-single-letter-without-context" &&
    r.Count > 0), "the missing single-letter English candidate must explain why");
Check(noEnglishEdge.Trace.Rejections.All(r =>
    r.Reason.StartsWith("generation-skipped/", StringComparison.Ordinal) ||
    r.Reason.StartsWith("add-rejected/", StringComparison.Ordinal)),
    "skipped generation and rejected Add calls need distinct reason categories");
var dashedRomaji = engine.AnalyzeWithTrace("sha-nashi");
Check(dashedRomaji.Trace.Rejections.Any(r =>
    r.Reason == "generation-skipped/structural-japanese-hyphen"),
    "skipped structural hypothesis must be visible");
var validJapanese = noEnglishEdge.Trace.CompareToGold([]);
Check(validJapanese.Status == "correct" && validJapanese.ExistsInCandidateGraph,
    "empty gold should accept a legal Japanese-only route");

var lockedEnglish = engine.AnalyzeWithTrace("abcde",
    new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 3)]));
Check(lockedEnglish.Trace.Edges.Any(e =>
    e.Origin == "mandatory" && e.RawUtf16Span == new Utf16Span(0, 3)),
    "mandatory lock must be traceable");
Check(lockedEnglish.Trace.Edges.All(e =>
    Math.Abs(e.Contributions.Sum(t => t.Value) +
        e.RoundingResidual - e.LocalScore) < 1e-9),
    "locked candidate score accounting must remain exact");

RejectBoth("😀test", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(1, 3)]));
RejectBoth("abcdef", new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
    ExplicitJapanese: [new Utf16Span(2, 6)]));
var sample = engine.AnalyzeWithTrace("😀GitHub");
bool goldRejected = false;
try { sample.Trace.CompareToGold([new Utf16Span(1, 4)]); }
catch (ArgumentException) { goldRejected = true; }
Check(goldRejected, "gold must reject UTF-16 surrogate splits");
goldRejected = false;
try { sample.Trace.CompareToGold([new Utf16Span(2, 5), new Utf16Span(4, 8)]); }
catch (ArgumentException) { goldRejected = true; }
Check(goldRejected, "gold must reject overlapping ranges");

string destination = Path.GetFullPath(output);
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
var jsonReport = new
{
    Schema = "meltia-h11-phase1-diagnostic-trace-v1",
    DataKind = "PUBLIC Issue diagnostic only, NOT independent holdout or user typing data",
    InputCorpusCount = 2,
    CaseCount = total,
    Top1Exact = top1,
    TopKExact = topk,
    Unresolved = total - top1,
    MissingCandidate = missing,
    PathPruned = pruned,
    TopKOmitted = omitted,
    CheckedBeamDrops = checkedBeamDrops,
    MultiUnitBeamDrops = multiUnitBeamDrops,
    DictionarySha256 = H11Snapshot.EmbeddedDictionarySha(),
    CorpusFingerprints = files.ToDictionary(file => file, file =>
        H11Snapshot.CorpusSha(Path.Combine(root, "tests/Meltia.H10Evaluation", file))),
    SourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-unspecified",
    Cases = summaries,
    UnlabeledProbes = probeCases
};
File.WriteAllText(destination, JsonSerializer.Serialize(jsonReport,
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"H11_TRACE_PASS: {total} frozen cases, Top1 {top1}, TopK {topk}, " +
    $"missing={missing}, pruned={pruned}, omitted={omitted}; " +
    $"beamDrops={checkedBeamDrops}, multiUnitDrops={multiUnitBeamDrops}; output={destination}");
return 0;

static string Sha(string raw) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

static Utf16Span[] Gold(JsonElement parts, string raw)
{
    var gold = new List<Utf16Span>();
    int offset = 0;
    foreach (var part in parts.EnumerateArray())
    {
        string lang = part.GetProperty("lang").GetString()!;
        string text = part.GetProperty("text").GetString()!;
        if (lang == "en")
        {
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                int begin = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                gold.Add(new Utf16Span(offset + begin, offset + i));
            }
        }
        offset += text.Length;
    }
    Check(offset == raw.Length, "corpus parts must match raw UTF-16 input");
    return gold.ToArray();
}

static void Check(bool pass, string reason)
{
    if (!pass) throw new InvalidDataException("H11 trace regression: " + reason);
}
