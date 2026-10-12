// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Meltype.Composition;
using Meltia.CompositionBridge;
using Meltia.Core.Boundary;
using Meltia.BoundaryEvaluation;

if ((args.Length != 2 && args.Length != 4) || args[0] != "--output" ||
    string.IsNullOrWhiteSpace(args[1]) ||
    args.Length == 4 && (args[2] != "--suite" || args[3] is not ("core" or "robustness" or "generalization" or "adversarial" or "longrun")))
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tests/Meltia.BoundaryEvaluation -- --output report.json [--suite core|robustness|generalization|adversarial|longrun]");
    return 64;
}
string suite = args.Length == 4 ? args[3] : "core";
var cases = suite switch
{
    "robustness" => RobustnessCorpus.Cases,
    "generalization" => GeneralizationCorpus.Cases,
    "adversarial" => AdversarialCorpus.Cases,
    "longrun" => LongRunCorpus.Cases,
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
var v10Independent = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true));
var v10WithHost = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true));
var v10WindowIndependent = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true));
var v10WindowWithHost = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true));
var v10DiverseIndependent = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true,
    ExperimentalDiverseCandidatePruning: true));
var v10DiverseWithHost = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true,
    ExperimentalDiverseCandidatePruning: true));
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
        predictions.Add(FromCore("Meltia-v1.0-independent",
            v10Independent.Analyze(gold.Raw)));
        predictions.Add(FromCore("Meltia-v1.0-windowed-independent",
            v10WindowIndependent.Analyze(gold.Raw)));
        predictions.Add(FromCore("Meltia-v1.0-diverse-independent",
            v10DiverseIndependent.Analyze(gold.Raw)));
        var experiment = experimental.Analyze(gold.Raw);
        predictions.Add(FromCore("Meltia-experimental-independent", experiment));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltia-experimental-independent", ex));
        predictions.Add(Error("Meltia-v0.7-independent", ex));
        predictions.Add(Error("Meltia-v0.8-independent", ex));
        predictions.Add(Error("Meltia-v0.9-independent", ex));
        predictions.Add(Error("Meltia-v1.0-independent", ex));
        predictions.Add(Error("Meltia-v1.0-windowed-independent", ex));
        predictions.Add(Error("Meltia-v1.0-diverse-independent", ex));
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
        predictions.Add(FromCore("Meltia-v1.0-lexicon",
            v10Independent.Analyze(gold.Raw, evidence)));
        predictions.Add(FromCore("Meltia-v1.0-windowed-lexicon",
            v10WindowIndependent.Analyze(gold.Raw, evidence)));
        predictions.Add(FromCore("Meltia-v1.0-diverse-lexicon",
            v10DiverseIndependent.Analyze(gold.Raw, evidence)));
        var matched = experimental.Analyze(gold.Raw, evidence);
        predictions.Add(FromCore("Meltia-experimental-lexicon", matched));
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltia-experimental-lexicon", ex));
        predictions.Add(Error("Meltia-v0.7-lexicon", ex));
        predictions.Add(Error("Meltia-v0.8-lexicon", ex));
        predictions.Add(Error("Meltia-v0.9-lexicon", ex));
        predictions.Add(Error("Meltia-v1.0-lexicon", ex));
        predictions.Add(Error("Meltia-v1.0-windowed-lexicon", ex));
        predictions.Add(Error("Meltia-v1.0-diverse-lexicon", ex));
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
        predictions.Add(FromCore("Meltia-v1.0-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v10WithHost).Meltia));
        predictions.Add(FromCore("Meltia-v1.0-windowed-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v10WindowWithHost).Meltia));
        predictions.Add(FromCore("Meltia-v1.0-diverse-shadow",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v10DiverseWithHost).Meltia));
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
        predictions.Add(FromCore("Meltia-v1.0-shadow-lexicon",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v10WithHost,
                englishWords: matchedEvidence.EnglishWords,
                properNames: matchedEvidence.ProperNames).Meltia));
        predictions.Add(FromCore("Meltia-v1.0-windowed-shadow-lexicon",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v10WindowWithHost,
                englishWords: matchedEvidence.EnglishWords,
                properNames: matchedEvidence.ProperNames).Meltia));
        predictions.Add(FromCore("Meltia-v1.0-diverse-shadow-lexicon",
            MeltypeBoundaryShadowBridge.CompareSegments(
                gold.Raw, segments, v10DiverseWithHost,
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
        predictions.Add(Error("Meltia-v1.0-shadow", ex));
        predictions.Add(Error("Meltia-v1.0-shadow-lexicon", ex));
        predictions.Add(Error("Meltia-v1.0-windowed-shadow", ex));
        predictions.Add(Error("Meltia-v1.0-windowed-shadow-lexicon", ex));
        predictions.Add(Error("Meltia-v1.0-diverse-shadow", ex));
        predictions.Add(Error("Meltia-v1.0-diverse-shadow-lexicon", ex));
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
    "Meltia-v0.9-shadow", "Meltia-v0.9-shadow-lexicon",
    "Meltia-v1.0-independent", "Meltia-v1.0-lexicon",
    "Meltia-v1.0-shadow", "Meltia-v1.0-shadow-lexicon",
    "Meltia-v1.0-windowed-independent", "Meltia-v1.0-windowed-lexicon",
    "Meltia-v1.0-windowed-shadow", "Meltia-v1.0-windowed-shadow-lexicon",
    "Meltia-v1.0-diverse-independent", "Meltia-v1.0-diverse-lexicon",
    "Meltia-v1.0-diverse-shadow", "Meltia-v1.0-diverse-shadow-lexicon"
];
var overall = models.ToDictionary(
    model => model, model => Aggregate(records, model));
var reviewMetrics = models.Where(x => x.StartsWith("Meltia-v1.0-", StringComparison.Ordinal))
    .ToDictionary(model => model, model => AggregateReviews(records, model));
var phase3Impact = new[] { "independent", "lexicon", "shadow", "shadow-lexicon" }
    .ToDictionary(condition => condition,
        condition => AggregateDiverseImpact(records, condition));
// The A04 adversarial cases intentionally have contradictory intents for
// identical input bytes. Report the impossibility explicitly, then require
// correct classification when a user actually supplies the language lock.
var ambiguityChecks = new List<object>();
if (suite == "adversarial")
{
    var pairs = cases.Where(x => x.Group == "A04-intent-ambiguous")
        .GroupBy(x => x.Raw, StringComparer.Ordinal).ToArray();
    if (pairs.Length != 6)
        throw new InvalidOperationException("Missing identical-input ambiguity pairs");
    foreach (var pair in pairs)
    {
        if (pair.Count() != 2 || pair.Select(x =>
                x.EnglishSpans.Count).Distinct().Count() != 2)
            throw new InvalidOperationException("Ambiguity pair labels are not contradictory");
        var raw = pair.Key;
        var lockSpan = new Utf16Span(0, raw.Length);
        var english = v10Independent.Analyze(raw, new BoundaryEvidence(
            EnglishWords: [raw], ExplicitEnglish: [lockSpan]));
        var japanese = v10Independent.Analyze(raw, new BoundaryEvidence(
            EnglishWords: [raw], ExplicitJapanese: [lockSpan]));
        var withoutLock = v10Independent.Analyze(raw, new BoundaryEvidence(
            EnglishWords: [raw]));
        if (!english.Routes[0].EnglishSpans.SequenceEqual([lockSpan]) ||
            japanese.Routes[0].EnglishSpans.Count != 0 ||
            withoutLock.Review is not {
                NeedsConfirmation: true,
                UnresolvedReadableEnglish.Count: 1
            } ||
            english.AutoCommitAllowed || japanese.AutoCommitAllowed)
        {
            Console.Error.WriteLine("v1.0 A04 AMBIGUOUS INTENT LOCK FAILURE " + raw);
            return 9;
        }
        ambiguityChecks.Add(new {
            Raw = raw,
            EnglishLockCorrect = true,
            JapaneseLockCorrect = true,
            WithoutUserIntentUnresolved = true,
            FundamentalTop1Indeterminacy = true
        });
    }
    Console.WriteLine("PASS v1.0 6 identical-input gold contradiction pairs; " +
        "explicit Japanese/English locks both work");
}
var byCategory = cases.Select(x => x.Group).Distinct()
    .OrderBy(x => x, StringComparer.Ordinal)
    .ToDictionary(group => group,
        group => models.ToDictionary(model => model,
            model => Aggregate(records.Where(x => x.Category == group), model)));

var report = new
{
    Schema = "meltia/boundary-evaluation-v1",
    EngineExperimentalFeatureVersion = "1.0-dev",
    EvidenceLevel = "B-offline-CSharp-synthetic-intent",
    GoldStatus = "synthetic-intent-labeled-not-human-verified",
    CorpusVersion = suite switch
    {
        "robustness" => "meltia-H1-vocabulary-robustness-v1",
        "generalization" => "meltia-v0.9-fresh-lexical-types-synthetic-v1",
        "adversarial" => "meltia-v1.0-fixed-intent-ambiguity-stress-v1",
        "longrun" => "meltia-v1.0-longrun-windowing-synthetic-v1",
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
        "v1.0: opt-in uncertainty review, near-tied TopK boundary agreement and explicit lexical-intent ambiguity; Top1 route scores deliberately unchanged",
        "Adversarial corpus: 56 hand-authored fixed synthetic intents including six identical-string contradictory gold pairs; never a blind natural-language benchmark",
        "Longrun: 60 synthetic scenarios with >96 contiguous ASCII characters, evaluates both baseline and strictly opt-in overlapping-window proposals",
        "Phase3 diverse: experimental start-region coverage only after 128-candidate pressure, always preserves user/protected and higher-priority lexicon and host candidates. No proven accuracy gain.",
        "Core 47 cases have already been studied and are DEVELOPMENT diagnostics, not a blind holdout",
        "Robustness suite: 530 synthetic inputs from H1/H9 previously used 54 OOV English words and 19 Japanese phonetic words, NOT natural holdout or Python parity"
    },
    CorpusCases = records.Count,
    DistinctRaw = records.Select(x => x.Raw).Distinct(StringComparer.Ordinal).Count(),
    GoldEnglishSpans = records.Sum(x => x.GoldEnglish.Count),
    Categories = byCategory,
    Overall = overall,
    ReviewMetrics = reviewMetrics,
    Phase3DiversityImpact = phase3Impact,
    AmbiguityPairs = ambiguityChecks,
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
foreach (var (model, measure) in reviewMetrics)
{
    Console.WriteLine(model + " review=" + measure.Reviewed +
        " confirmation=" + measure.NeedsConfirmation +
        " confirmationWrong=" + measure.ConfirmationTop1Wrong +
        " nonConfirmationWrong=" + measure.NoConfirmationTop1Wrong +
        " noConfirmationTop1Correct=" + measure.NoConfirmationTop1Correct +
        " unresolvedReadable=" + measure.UnresolvedReadableCases);
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
    "Meltia-v0.9-shadow", "Meltia-v0.9-shadow-lexicon",
    "Meltia-v1.0-independent", "Meltia-v1.0-lexicon",
    "Meltia-v1.0-shadow", "Meltia-v1.0-shadow-lexicon"
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
// v1.0 changes only the optional review signal. Check each result, not
// just aggregate scores, so no accidental boundary/score drift can pass.
foreach (var row in records)
{
    foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
    {
        var old = row.Predictions.Single(x => x.Model == "Meltia-v0.9-" + condition);
        var latest = row.Predictions.Single(x => x.Model == "Meltia-v1.0-" + condition);
        if (old.Error is not null || latest.Error is not null) continue;
        if (latest.Review is null || !old.English.SequenceEqual(latest.English) ||
            !old.Candidates!.SequenceEqual(latest.Candidates!) ||
            old.Routes.Count != latest.Routes.Count ||
            !old.Routes.Zip(latest.Routes,
                (a, b) => a.SequenceEqual(b)).All(x => x))
        {
            Console.Error.WriteLine("v1.0 TOP1/TOPK/CANDIDATE PARITY REGRESSION " +
                row.Id + " " + condition);
            return 8;
        }
    }
}
Console.WriteLine("PASS v1.0 candidate and path parity against v0.9 on " + suite);
// The new long-run hypothesis generator is opt-in. For any case without a
// contiguous >96 ASCII run its observable candidates and Top-K must be
// byte-for-byte equivalent to v1.0. On long runs, only compare candidate
// coverage and failure taxonomy; do not assume accuracy improvement.
foreach (var row in records)
{
    if (System.Text.RegularExpressions.Regex.IsMatch(row.Raw, "[A-Za-z]{97,}"))
        continue;
    foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
    {
        var baseline = row.Predictions.Single(x => x.Model == "Meltia-v1.0-" + condition);
        var windowed = row.Predictions.Single(x =>
            x.Model == "Meltia-v1.0-windowed-" + condition);
        if (baseline.Error is not null || windowed.Error is not null ||
            !baseline.Candidates!.SequenceEqual(windowed.Candidates!) ||
            !baseline.English.SequenceEqual(windowed.English) ||
            baseline.Routes.Count != windowed.Routes.Count ||
            !baseline.Routes.Zip(windowed.Routes,
                (a, b) => a.SequenceEqual(b)).All(x => x))
        {
            Console.Error.WriteLine("v1.0 SHORT INPUT WINDOW REGRESSION " +
                suite + " " + row.Id + " " + condition);
            return 10;
        }
    }
}
Console.WriteLine("PASS v1.0 windowing short-run identity on " + suite);

// Candidate pruning is a separate experiment. Without actual truncation
// it must not change candidates or routes. Japanese-only negative controls
// are checked separately even if long.
foreach (var row in records)
{
    foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
    {
        var before = row.Predictions.Single(x =>
            x.Model == "Meltia-v1.0-windowed-" + condition);
        var after = row.Predictions.Single(x =>
            x.Model == "Meltia-v1.0-diverse-" + condition);
        if (before.Error is not null || after.Error is not null) continue;
        var noPruningNeeded = before.Candidates!.Count < MeltiaBoundaryEngine.MaxCandidates;
        var japaneseNegative = row.Category is
            "S11-japanese-only" or "R08-japanese-only" or
            "G06-japanese-only" or "A01-japanese-orthographic" or
            "L04-long-japanese-only";
        if ((noPruningNeeded || japaneseNegative) &&
            (!before.Candidates.SequenceEqual(after.Candidates!) ||
             !before.English.SequenceEqual(after.English) ||
             before.Routes.Count != after.Routes.Count ||
             !before.Routes.Zip(after.Routes,
                 (a, b) => a.SequenceEqual(b)).All(x => x)))
        {
            Console.Error.WriteLine("v1.0 DIVERSITY REGRESSION " +
                suite + " " + row.Id + " " + condition);
            return 11;
        }
    }
}
Console.WriteLine("PASS phase3 nontruncated and Japanese-only candidate/route parity");
foreach (var (condition, impact) in phase3Impact)
    Console.WriteLine("PHASE3 IMPACT " + suite + " " + condition +
        " changed=" + impact.ChangedInputs +
        " goldAdded=" + impact.NewGoldEdges +
        " goldDropped=" + impact.DroppedGoldEdges +
        " newlyCorrect=" + impact.NewlyCorrectTop1 +
        " newlyWrong=" + impact.NewlyWrongTop1);
foreach (var condition in new[] { "independent", "lexicon", "shadow", "shadow-lexicon" })
{
    var baseline = overall["Meltia-v1.0-windowed-" + condition];
    var diverse = overall["Meltia-v1.0-diverse-" + condition];
    Console.WriteLine("PHASE3 " + suite + " " + condition +
        " deltaTop1=" + (diverse.ExactCases - baseline.ExactCases) +
        " deltaTopK=" + (diverse.TopKExactCases - baseline.TopKExactCases) +
        " deltaGoldCandidateHits=" +
            ((diverse.GoldCandidateHits ?? 0) - (baseline.GoldCandidateHits ?? 0)) +
        " deltaCandidateEdges=" +
            ((diverse.CandidateEdges ?? 0) - (baseline.CandidateEdges ?? 0)));
}
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
        result.Candidates.Select(c => c.Span).ToArray(), null, result.Review);

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

static CandidateSelectionImpact AggregateDiverseImpact(
    IEnumerable<CaseRecord> sample, string condition)
{
    int changed = 0, added = 0, dropped = 0, fixedTop1 = 0,
        brokenTop1 = 0, checkedCases = 0;
    foreach (var row in sample)
    {
        var before = row.Predictions.Single(x =>
            x.Model == "Meltia-v1.0-windowed-" + condition);
        var after = row.Predictions.Single(x =>
            x.Model == "Meltia-v1.0-diverse-" + condition);
        if (before.Error is not null || after.Error is not null ||
            before.Candidates is null || after.Candidates is null)
            continue;
        checkedCases++;
        if (!before.Candidates.SequenceEqual(after.Candidates)) changed++;
        foreach (var gold in row.GoldEnglish)
        {
            bool presentBefore = before.Candidates.Contains(gold);
            bool presentAfter = after.Candidates.Contains(gold);
            if (presentAfter && !presentBefore) added++;
            if (presentBefore && !presentAfter) dropped++;
        }
        bool wasCorrect = before.English.SequenceEqual(row.GoldEnglish);
        bool isCorrect = after.English.SequenceEqual(row.GoldEnglish);
        if (isCorrect && !wasCorrect) fixedTop1++;
        if (wasCorrect && !isCorrect) brokenTop1++;
    }
    return new CandidateSelectionImpact(
        checkedCases, changed, added, dropped, fixedTop1, brokenTop1);
}

static ReviewMetrics AggregateReviews(IEnumerable<CaseRecord> sample, string model)
{
    int reviewed = 0, needs = 0, flaggedWrong = 0, clearWrong = 0,
        clearCorrect = 0, unresolvedReadable = 0;
    foreach (var row in sample)
    {
        var prediction = row.Predictions.Single(p => p.Model == model);
        if (prediction.Error is not null || prediction.Review is null) continue;
        reviewed++;
        var flagged = prediction.Review.NeedsConfirmation;
        var correct = prediction.English.SequenceEqual(row.GoldEnglish);
        if (flagged) needs++;
        if (prediction.Review.UnresolvedReadableEnglish.Count > 0)
            unresolvedReadable++;
        if (flagged && !correct) flaggedWrong++;
        if (!flagged && !correct) clearWrong++;
        if (!flagged && correct) clearCorrect++;
    }
    return new ReviewMetrics(reviewed, needs, flaggedWrong,
        clearWrong, clearCorrect, unresolvedReadable);
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
        if (row.Category is "S11-japanese-only" or "R08-japanese-only" or "G06-japanese-only" or "A01-japanese-orthographic" or "L04-long-japanese-only")
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
    IReadOnlyList<Utf16Span>? Candidates, string? Error,
    BoundaryReview? Review = null);

internal sealed record CandidateSelectionImpact(
    int CheckedInputs, int ChangedInputs, int NewGoldEdges,
    int DroppedGoldEdges, int NewlyCorrectTop1, int NewlyWrongTop1);

internal sealed record ReviewMetrics(
    int Reviewed, int NeedsConfirmation, int ConfirmationTop1Wrong,
    int NoConfirmationTop1Wrong, int NoConfirmationTop1Correct,
    int UnresolvedReadableCases);

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
