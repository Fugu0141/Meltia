// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Meltype.Composition;
using Meltia.CompositionBridge;
using Meltia.Core.Boundary;
using Meltia.BoundaryEvaluation;

if ((args.Length != 2 && args.Length != 4) || args[0] != "--output" ||
    string.IsNullOrWhiteSpace(args[1]) ||
    args.Length == 4 && (args[2] != "--suite" || args[3] is not ("core" or "robustness" or "generalization")))
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tests/Meltia.BoundaryEvaluation -- --output report.json [--suite core|robustness|generalization]");
    return 64;
}
string suite = args.Length == 4 ? args[3] : "core";
var cases = suite switch
{
    "robustness" => RobustnessCorpus.Cases,
    "generalization" => GeneralizationCorpus.Cases,
    _ => GoldCorpus.Cases
};

var engine = new MeltiaBoundaryEngine();
var experimental = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalFragmentationPenalty: true));
var penaltyOnly = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalFragmentationPenalty: true));
var hostOnly = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true));
var v07Independent = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalFragmentationPenalty: true,
    ExperimentalUnknownLatinCandidates: true));
var v07WithHost = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalFragmentationPenalty: true,
    ExperimentalUnknownLatinCandidates: true));
var v08Independent = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true));
var v08WithHost = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true));
var v09Independent = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true));
var v09WithHost = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true));
var detector = CompositionDetector.CreateDefault();
var records = new List<CaseRecord>();
foreach (var gold in cases)
{
    var predictions = new List<Prediction>();
    try
    {
        var independent = engine.Analyze(gold.Raw);
        predictions.Add(FromCore("Meltia-independent", independent));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltia-independent", ex));
    }

    try
    {
        var v07Result = v07Independent.Analyze(gold.Raw);
        predictions.Add(FromCore("Meltia-v0.7-independent", v07Result));
        predictions.Add(FromCore("Meltia-v0.8-independent",
            v08Independent.Analyze(gold.Raw)));
        predictions.Add(FromCore("Meltia-v0.9-independent",
            v09Independent.Analyze(gold.Raw)));
        var experiment = experimental.Analyze(gold.Raw);
        predictions.Add(FromCore("Meltia-experimental-independent", experiment));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltia-experimental-independent", ex));
        predictions.Add(Error("Meltia-v0.7-independent", ex));
        predictions.Add(Error("Meltia-v0.8-independent", ex));
        predictions.Add(Error("Meltia-v0.9-independent", ex));
    }

    try
    {
        var matched = engine.Analyze(gold.Raw, MeltypeLexiconEvidence.For(gold.Raw));
        predictions.Add(FromCore("Meltia-lexicon-matched", matched));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltia-lexicon-matched", ex));
    }
    try
    {
        var evidence = MeltypeLexiconEvidence.For(gold.Raw);
        var withV07 = v07Independent.Analyze(gold.Raw, evidence);
        predictions.Add(FromCore("Meltia-v0.7-lexicon", withV07));
        predictions.Add(FromCore("Meltia-v0.8-lexicon",
            v08Independent.Analyze(gold.Raw, evidence)));
        predictions.Add(FromCore("Meltia-v0.9-lexicon",
            v09Independent.Analyze(gold.Raw, evidence)));
        var matched = experimental.Analyze(gold.Raw, evidence);
        predictions.Add(FromCore("Meltia-experimental-lexicon", matched));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltia-experimental-lexicon", ex));
        predictions.Add(Error("Meltia-v0.7-lexicon", ex));
        predictions.Add(Error("Meltia-v0.8-lexicon", ex));
        predictions.Add(Error("Meltia-v0.9-lexicon", ex));
    }

    try
    {
        // Do not hand-label composition units: use the actual Meltype
        // CompositionText keystroke pipeline and its CompositionDetector.
        var composition = new CompositionText(detector);
        foreach (char c in gold.Raw) composition.Append(c);
        if (composition.Raw != gold.Raw)
            throw new InvalidOperationException("Meltype keystrokes did not preserve raw");
        var segments = detector.Segment(composition.Units, composition.Pending);
        var comparison = MeltypeBoundaryShadowBridge.CompareSegments(
            gold.Raw, segments, engine);
        predictions.Add(new Prediction("Meltype-offline", comparison.MeltypeEnglish.ToArray(),
            [comparison.MeltypeEnglish.ToArray()], null, null));
        predictions.Add(FromCore("Meltia-shadow-advised", comparison.Meltia));
        var matchedEvidence = MeltypeLexiconEvidence.For(gold.Raw);
        var matchedShadow = MeltypeBoundaryShadowBridge.CompareSegments(
            gold.Raw, segments, engine,
            englishWords: matchedEvidence.EnglishWords,
            properNames: matchedEvidence.ProperNames);
        predictions.Add(FromCore("Meltia-shadow-lexicon-matched", matchedShadow.Meltia));
        var experimentalShadow = MeltypeBoundaryShadowBridge.CompareSegments(
            gold.Raw, segments, experimental);
        predictions.Add(FromCore("Meltia-experimental-shadow", experimentalShadow.Meltia));
        var v07Shadow = MeltypeBoundaryShadowBridge.CompareSegments(
            gold.Raw, segments, v07WithHost);
        predictions.Add(FromCore("Meltia-v0.7-shadow", v07Shadow.Meltia));
        predictions.Add(FromCore("Meltia-v0.8-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v08WithHost).Meltia));
        predictions.Add(FromCore("Meltia-v0.9-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v09WithHost).Meltia));
        var v07SharedShadow = MeltypeBoundaryShadowBridge.CompareSegments(
            gold.Raw, segments, v07WithHost,
            englishWords: matchedEvidence.EnglishWords,
            properNames: matchedEvidence.ProperNames);
        predictions.Add(FromCore("Meltia-v0.7-shadow-lexicon",
            v07SharedShadow.Meltia));
        predictions.Add(FromCore("Meltia-v0.8-shadow-lexicon",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v08WithHost,
                englishWords: matchedEvidence.EnglishWords,
                properNames: matchedEvidence.ProperNames).Meltia));
        predictions.Add(FromCore("Meltia-v0.9-shadow-lexicon",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v09WithHost,
                englishWords: matchedEvidence.EnglishWords,
                properNames: matchedEvidence.ProperNames).Meltia));
        predictions.Add(FromCore("Meltia-penalty-only-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, penaltyOnly).Meltia));
        predictions.Add(FromCore("Meltia-host-only-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, hostOnly).Meltia));
        var experimentalMatchedShadow = MeltypeBoundaryShadowBridge.CompareSegments(
            gold.Raw, segments, experimental,
            englishWords: matchedEvidence.EnglishWords,
            properNames: matchedEvidence.ProperNames);
        predictions.Add(FromCore("Meltia-experimental-shadow-lexicon",
            experimentalMatchedShadow.Meltia));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltype-offline", ex));
        predictions.Add(Error("Meltia-shadow-advised", ex));
        predictions.Add(Error("Meltia-shadow-lexicon-matched", ex));
        predictions.Add(Error("Meltia-experimental-shadow", ex));
        predictions.Add(Error("Meltia-v0.7-shadow", ex));
        predictions.Add(Error("Meltia-v0.7-shadow-lexicon", ex));
        predictions.Add(Error("Meltia-v0.8-shadow", ex));
        predictions.Add(Error("Meltia-v0.8-shadow-lexicon", ex));
        predictions.Add(Error("Meltia-v0.9-shadow", ex));
        predictions.Add(Error("Meltia-v0.9-shadow-lexicon", ex));
        predictions.Add(Error("Meltia-penalty-only-shadow", ex));
        predictions.Add(Error("Meltia-host-only-shadow", ex));
        predictions.Add(Error("Meltia-experimental-shadow-lexicon", ex));
    }
    records.Add(new CaseRecord(gold.Id, gold.Group, gold.Raw, gold.EnglishSpans, predictions));
}

string[] models = [
    "Meltype-offline",
    "Meltia-independent", "Meltia-lexicon-matched",
    "Meltia-shadow-advised", "Meltia-shadow-lexicon-matched",
    "Meltia-experimental-independent", "Meltia-experimental-lexicon",
    "Meltia-experimental-shadow", "Meltia-experimental-shadow-lexicon",
    "Meltia-penalty-only-shadow", "Meltia-host-only-shadow",
    "Meltia-v0.7-independent", "Meltia-v0.7-lexicon",
    "Meltia-v0.7-shadow", "Meltia-v0.7-shadow-lexicon",
    "Meltia-v0.8-independent", "Meltia-v0.8-lexicon",
    "Meltia-v0.8-shadow", "Meltia-v0.8-shadow-lexicon",
    "Meltia-v0.9-independent", "Meltia-v0.9-lexicon",
    "Meltia-v0.9-shadow", "Meltia-v0.9-shadow-lexicon"
];
var overall = models.ToDictionary(
    model => model, model => Aggregate(records, model));
var byCategory = cases.Select(x => x.Group).Distinct()
    .OrderBy(x => x, StringComparer.Ordinal)
    .ToDictionary(group => group,
        group => models.ToDictionary(model => model,
            model => Aggregate(records.Where(x => x.Category == group), model)));

var report = new
{
    Schema = "meltia/boundary-evaluation-v1",
    EngineExperimentalFeatureVersion = "0.9",
    EvidenceLevel = "B-offline-CSharp-synthetic-intent",
    GoldStatus = "synthetic-intent-labeled-not-human-verified",
    CorpusVersion = suite switch
    {
        "robustness" => "meltia-H1-vocabulary-robustness-v1",
        "generalization" => "meltia-v0.9-fresh-lexical-types-synthetic-v1",
        _ => "meltia-synthetic-v1.1"
    },
    Suite = suite,
    EnglishDictionaryTypes = MeltypeLexiconEvidence.EnglishWordTypes,
    ProperNameTypes = MeltypeLexiconEvidence.ProperNameTypes,
    SourceNotes = new[] {
        "H7 categories: IncrementalBoundaryLab/docs/research-evaluation-standard.md (research branch)",
        "Selected H9 failure cases: IncrementalBoundaryLab/research/2026-10-10-h9-additional-evaluation.md (research/h9-followup-evaluation-20261010)",
        "Remaining cases: handcrafted regression and intent-ambiguity cases; NOT a blind holdout",
        "Meltia-independent: no external English lexicon or Meltype evidence",
        "Meltia-lexicon-matched: English and proper names from same embedded Meltype files, filtered by raw occurrence, no gold labels",
        "Meltia-shadow-lexicon-matched: same evidence plus Meltype segmentation; an assisted comparison, NOT independent",
        "Meltia-shadow-advised: includes Meltype English suggestions and protected spans; NOT an independent baseline",
        "Meltype-offline: CompositionText.Append per character + CompositionDetector.Segment; NOT Windows TSF",
        "Experimental mode: same C# core, independent opt-in host corroboration and higher per-English-span cost, not active IME",
        "Ablation: penalty-only and host-only share the exact same Meltype segments and raw strings",
        "v0.7: bounded unknown-Latin phonetic seam proposals, no dictionary or gold-dependent training",
        "v0.8: opt-in dynamic-programming lattice with zero-cost readable romaji paths and scored unknown-letter fallback; candidate generator unchanged",
        "v0.9: opt-in pairwise route evidence for two nonphonetic English islands separated by a Japanese 2-letter particle; no user text changes",
        "Generalization: 40 lexical types not in old 54-word list, 18 Japanese romanized strings; 290 synthetic templates, NOT an independent blind holdout",
        "Core 47 cases have already been studied and are DEVELOPMENT diagnostics, not a blind holdout",
        "Robustness suite: 530 synthetic inputs from H1/H9 previously used 54 OOV English words and 19 Japanese phonetic words, NOT natural holdout or Python parity"
    },
    CorpusCases = records.Count,
    DistinctRaw = records.Select(x => x.Raw).Distinct(StringComparer.Ordinal).Count(),
    GoldEnglishSpans = records.Sum(x => x.GoldEnglish.Count),
    Categories = byCategory,
    Overall = overall,
    Cases = records,
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
});
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, json + "\n");
Console.WriteLine("Corpus suite=" + suite + ": " + records.Count + " intent-labeled synthetic cases; " +
    report.DistinctRaw + " distinct raw inputs; " + report.GoldEnglishSpans + " gold English spans");
foreach (var model in models)
{
    var m = overall[model];
    Console.WriteLine(model + " exact=" + m.ExactCases + "/" + m.Cases +
        " topK=" + m.TopKExactCases + "/" + m.Cases +
        " languageRegionExact=" + m.LanguageRegionExactCases + "/" + m.Cases +
        " languageRegionTopK=" + m.TopKLanguageRegionExactCases + "/" + m.Cases +
        " spanGoldRecall=" + Format(m.GoldCandidateRecall) +
        " candidateEdges=" + (m.CandidateEdges?.ToString() ?? "N/A") +
        " candidateGoldMatchFraction=" + Format(m.CandidateGoldMatchFraction) +
        " japaneseOnlyCandidateCases=" + (m.JapaneseOnlyCandidateCases?.ToString() ?? "N/A") +
        " stages=" + string.Join(",", m.FailureStages
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key + ":" + x.Value)) +
        " switchF1=" + Format(m.SwitchF1Micro) +
        " errors=" + m.Errors);
    foreach (var item in records.Where(x => !x.Predictions.Single(p => p.Model == model)
                       .English.SequenceEqual(x.GoldEnglish))
                 .Take(6))
    {
        var pred = item.Predictions.Single(p => p.Model == model);
        Console.WriteLine("  MISS " + item.Id + " raw=" + item.Raw +
            " expected=" + Show(item.GoldEnglish) + " actual=" + Show(pred.English) +
            (pred.Error is null ? "" : " error=" + pred.Error));
    }
}
Console.WriteLine("CATEGORY | case count | model | Top1 | TopK | gold candidate recall");
foreach (var category in byCategory)
{
    foreach (var model in models)
    {
        var m = category.Value[model];
        Console.WriteLine(category.Key + " | " + m.Cases + " | " + model +
            " | " + m.ExactCases + " | " + m.TopKExactCases +
            " | " + Format(m.GoldCandidateRecall));
    }
}
Console.WriteLine("Evaluation JSON: " + output);
// Strict negative control: experimental routing must not newly interpret
// Japanese-only cases as English compared with the existing shadow mode.
// Do NOT set a post-hoc overall accuracy threshold from these studied cases.
var priorJapanese = overall["Meltia-shadow-advised"].JapaneseOnlyFalseEnglishCases;
foreach (var model in new[] {
    "Meltia-experimental-independent", "Meltia-experimental-lexicon",
    "Meltia-experimental-shadow", "Meltia-experimental-shadow-lexicon",
    "Meltia-penalty-only-shadow", "Meltia-host-only-shadow",
    "Meltia-v0.7-independent", "Meltia-v0.7-lexicon",
    "Meltia-v0.7-shadow", "Meltia-v0.7-shadow-lexicon",
    "Meltia-v0.8-independent", "Meltia-v0.8-lexicon",
    "Meltia-v0.8-shadow", "Meltia-v0.8-shadow-lexicon",
    "Meltia-v0.9-independent", "Meltia-v0.9-lexicon",
    "Meltia-v0.9-shadow", "Meltia-v0.9-shadow-lexicon"
})
{
    if (overall[model].JapaneseOnlyFalseEnglishCases > priorJapanese)
    {
        Console.Error.WriteLine("NEGATIVE CONTROL REGRESSION: " + model +
            " Japanese-only false English=" + overall[model].JapaneseOnlyFalseEnglishCases +
            " baseline=" + priorJapanese);
        return 3;
    }
}
Console.WriteLine("PASS Japanese-only negative-control nonregression (" +
    overall["Meltype-offline"].JapaneseOnlyCases + " synthetic cases)");

// Unlike a post-hoc numerical accuracy threshold, these are semantic v0.7
// contracts: adding *provisional* hypotheses must never discard an old
// gold candidate or add extra English candidates to fully phonetic Japanese.
foreach (var (v07, reference) in new[] {
    ("Meltia-v0.7-independent", "Meltia-experimental-independent"),
    ("Meltia-v0.7-lexicon", "Meltia-experimental-lexicon"),
    ("Meltia-v0.7-shadow", "Meltia-experimental-shadow"),
    ("Meltia-v0.7-shadow-lexicon", "Meltia-experimental-shadow-lexicon")
})
{
    var newer = overall[v07];
    var older = overall[reference];
    if (newer.GoldCandidateHits < older.GoldCandidateHits ||
        newer.JapaneseOnlyCandidateEdges > older.JapaneseOnlyCandidateEdges)
    {
        Console.Error.WriteLine("v0.7 CONSERVATIVE CANDIDATE REGRESSION: " +
            v07 + " goldHits=" + newer.GoldCandidateHits +
            " baseline=" + older.GoldCandidateHits +
            " japaneseCandidates=" + newer.JapaneseOnlyCandidateEdges +
            " baseline=" + older.JapaneseOnlyCandidateEdges);
        return 4;
    }
}
Console.WriteLine("PASS v0.7 candidate inclusion and Japanese-only candidate burden");

// v0.8 is a decoder-only experiment: it must not drop, insert or reorder
// candidate evidence. Check *every* case rather than only aggregate recall.
foreach (var row in records)
{
    foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
    {
        var older = row.Predictions.Single(x => x.Model == "Meltia-v0.7-" + condition);
        var newer = row.Predictions.Single(x => x.Model == "Meltia-v0.8-" + condition);
        if (older.Error is not null || newer.Error is not null) continue;
        if (older.Candidates is null || newer.Candidates is null ||
            !older.Candidates.SequenceEqual(newer.Candidates))
        {
            Console.Error.WriteLine("v0.8 DECODER CANDIDATE PARITY REGRESSION: " +
                row.Id + " model=" + condition);
            return 5;
        }
    }
}
Console.WriteLine("PASS v0.8 per-input candidate parity across all four evidence conditions");
foreach (var row in records)
{
    foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
    {
        var older = row.Predictions.Single(x => x.Model == "Meltia-v0.8-" + condition);
        var newer = row.Predictions.Single(x => x.Model == "Meltia-v0.9-" + condition);
        if (older.Error is not null || newer.Error is not null) continue;
        if (older.Candidates is null || newer.Candidates is null ||
            !older.Candidates.SequenceEqual(newer.Candidates))
        {
            Console.Error.WriteLine("v0.9 CANDIDATE PARITY REGRESSION: " +
                row.Id + " model=" + condition);
            return 6;
        }
    }
}
Console.WriteLine("PASS v0.9 per-input candidate parity with v0.8");
// The same semantic nonregression now holds for all three named synthetic
// suites, including newly introduced lexical types. All models use the same
// exact inputs, evidence and gold labels within each suite. This guard is not
// evidence of naturally typed text accuracy or a statistical significance test.
foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
{
    var older = overall["Meltia-v0.8-" + condition];
    var newer = overall["Meltia-v0.9-" + condition];
    if (newer.ExactCases < older.ExactCases ||
        newer.TopKExactCases < older.TopKExactCases)
    {
        Console.Error.WriteLine("v0.9 SYNTHETIC CORPUS ACCURACY REGRESSION: " +
            suite + " " + condition +
            " Top1=" + newer.ExactCases + "/" + older.ExactCases +
            " TopK=" + newer.TopKExactCases + "/" + older.TopKExactCases);
        return 7;
    }
}
Console.WriteLine("PASS v0.9 Top1/TopK nonregression on " + suite +
    " across all evidence conditions");
var errors = overall.Values.Sum(x => x.Errors);
if (errors > 0)
{
    Console.Error.WriteLine("Evaluation contains " + errors +
        " per-model exceptions; see JSON. CI treats these as failures.");
    return 2;
}
return 0;

static string Format(double? value) => value.HasValue ? value.Value.ToString("0.0000") : "N/A";
static string Show(IReadOnlyList<Utf16Span> spans) =>
    string.Join(";", spans.Select(x => "[" + x.Start + "," + x.End + ")"));

static Prediction FromCore(string model, BoundaryResult result) =>
    new(model, result.Routes[0].EnglishSpans.ToArray(),
        result.Routes.Select(r => (IReadOnlyList<Utf16Span>)r.EnglishSpans.ToArray()).ToArray(),
        result.Candidates.Select(c => c.Span).ToArray(), null);

static Prediction Error(string model, Exception exception) =>
    new(model, [], [], null, exception.GetType().Name + ": " + exception.Message);

// Language regions differ from English word-token edges: adjacent English
// words are still one continuous Latin-language region. Retain both metrics.
static IReadOnlyList<Utf16Span> CoalesceEnglishRegions(IReadOnlyList<Utf16Span> spans)
{
    var output = new List<Utf16Span>();
    foreach (var span in spans)
    {
        if (output.Count > 0 && output[^1].End == span.Start)
            output[^1] = new Utf16Span(output[^1].Start, span.End);
        else if (output.Count > 0 && output[^1].End > span.Start)
            throw new InvalidOperationException("Overlapping English regions");
        else output.Add(span);
    }
    return output;
}

static HashSet<int> Switches(int length, IReadOnlyList<Utf16Span> spans)
{
    var english = new bool[length];
    foreach (var span in spans)
    {
        if (span.Start < 0 || span.Start >= span.End || span.End > length)
            throw new InvalidOperationException("Invalid prediction span");
        for (int i = span.Start; i < span.End; ++i) english[i] = true;
    }
    var switches = new HashSet<int>();
    for (int i = 1; i < length; ++i)
        if (english[i] != english[i - 1]) switches.Add(i);
    return switches;
}

// Diagnostic stages are exclusive and prioritized. The route/top-K stage can
// reflect either graph connectivity, beam pruning, or scoring; it does NOT
// prove the correct combination was never constructible.
static string FailureStage(CaseRecord row, Prediction p)
{
    if (p.Error is not null) return "exception";
    if (p.English.SequenceEqual(row.GoldEnglish)) return "correct";
    if (p.Candidates is null) return "host-top1-wrong";
    if (row.GoldEnglish.Any(g => !p.Candidates.Contains(g))) return "candidate-missing";
    if (p.Routes.Any(route => route.SequenceEqual(row.GoldEnglish)))
        return "top1-ranking-wrong";
    return "route-or-topk-missing";
}

static Metrics Aggregate(IEnumerable<CaseRecord> sample, string model)
{
    int n = 0, exact = 0, topK = 0, regionExact = 0, regionTopK = 0;
    int goldSpans = 0, hits = 0;
    int tp = 0, fp = 0, fn = 0, errors = 0, japanese = 0, falseEnglish = 0;
    int candidateEdges = 0, japaneseCandidateCases = 0, japaneseCandidateEdges = 0;
    var failureStages = new Dictionary<string, int>(StringComparer.Ordinal);
    var macros = new List<double>();
    foreach (var row in sample)
    {
        n++;
        var p = row.Predictions.Single(x => x.Model == model);
        var stage = FailureStage(row, p);
        failureStages[stage] = failureStages.GetValueOrDefault(stage) + 1;
        var isExact = p.Error is null && p.English.SequenceEqual(row.GoldEnglish);
        if (isExact) exact++;
        if (p.Error is null && p.Routes.Any(route => route.SequenceEqual(row.GoldEnglish))) topK++;
        var languageGold = CoalesceEnglishRegions(row.GoldEnglish);
        if (p.Error is null &&
            CoalesceEnglishRegions(p.English).SequenceEqual(languageGold))
            regionExact++;
        if (p.Error is null && p.Routes.Any(route =>
            CoalesceEnglishRegions(route).SequenceEqual(languageGold)))
            regionTopK++;
        if (p.Error is not null) errors++;
        if (p.Candidates is not null)
        {
            hits += row.GoldEnglish.Count(g => p.Candidates.Contains(g));
            candidateEdges += p.Candidates.Count;
        }
        goldSpans += row.GoldEnglish.Count;
        if (row.Category is "S11-japanese-only" or "R08-japanese-only" or "G06-japanese-only")
        {
            japanese++;
            if (p.English.Count > 0) falseEnglish++;
            if (p.Candidates is { Count: > 0 })
            {
                japaneseCandidateCases++;
                japaneseCandidateEdges += p.Candidates.Count;
            }
        }
        var truth = Switches(row.Raw.Length, row.GoldEnglish);
        var predicted = Switches(row.Raw.Length, p.English);
        int a = truth.Intersect(predicted).Count();
        int b = predicted.Except(truth).Count();
        int c = truth.Except(predicted).Count();
        tp += a; fp += b; fn += c;
        if (2*a + b + c > 0) macros.Add(2.0*a / (2*a+b+c));
    }
    bool hasCandidates = model != "Meltype-offline";
    return new Metrics(n, exact, topK, regionExact, regionTopK,
        errors, goldSpans,
        hasCandidates ? hits : null,
        hasCandidates && goldSpans > 0 ? (double)hits / goldSpans : null,
        tp, fp, fn,
        2*tp+fp+fn > 0 ? 2.0*tp/(2*tp+fp+fn) : null,
        macros.Count > 0 ? macros.Average() : null,
        japanese, falseEnglish,
        hasCandidates ? candidateEdges : null,
        hasCandidates && candidateEdges > 0 ? (double)hits / candidateEdges : null,
        hasCandidates ? japaneseCandidateCases : null,
        hasCandidates ? japaneseCandidateEdges : null,
        failureStages);
}

internal sealed record Prediction(string Model, IReadOnlyList<Utf16Span> English,
    IReadOnlyList<IReadOnlyList<Utf16Span>> Routes,
    IReadOnlyList<Utf16Span>? Candidates, string? Error);

internal sealed record CaseRecord(string Id, string Category, string Raw,
    IReadOnlyList<Utf16Span> GoldEnglish, IReadOnlyList<Prediction> Predictions);

internal sealed record Metrics(
    int Cases, int ExactCases, int TopKExactCases,
    int LanguageRegionExactCases, int TopKLanguageRegionExactCases, int Errors,
    int GoldSpans, int? GoldCandidateHits, double? GoldCandidateRecall,
    int SwitchTP, int SwitchFP, int SwitchFN,
    double? SwitchF1Micro, double? SwitchF1Macro,
    int JapaneseOnlyCases, int JapaneseOnlyFalseEnglishCases,
    int? CandidateEdges, double? CandidateGoldMatchFraction,
    int? JapaneseOnlyCandidateCases, int? JapaneseOnlyCandidateEdges,
    IReadOnlyDictionary<string, int> FailureStages);
