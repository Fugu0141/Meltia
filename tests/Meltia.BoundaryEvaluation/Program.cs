// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Meltype.Composition;
using Meltia.CompositionBridge;
using Meltia.Core.Boundary;
using Meltia.BoundaryEvaluation;

if (args.Length != 2 || args[0] != "--output" || string.IsNullOrWhiteSpace(args[1]))
{
    Console.Error.WriteLine("Usage: dotnet run --project tests/Meltia.BoundaryEvaluation -- --output report.json");
    return 64;
}

var engine = new MeltiaBoundaryEngine();
var detector = CompositionDetector.CreateDefault();
var records = new List<CaseRecord>();
foreach (var gold in GoldCorpus.Cases)
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
    }
    catch (Exception ex)
    {
        predictions.Add(Error("Meltype-offline", ex));
        predictions.Add(Error("Meltia-shadow-advised", ex));
    }
    records.Add(new CaseRecord(gold.Id, gold.Group, gold.Raw, gold.EnglishSpans, predictions));
}

string[] models = ["Meltype-offline", "Meltia-independent", "Meltia-shadow-advised"];
var overall = models.ToDictionary(
    model => model, model => Aggregate(records, model));
var byCategory = GoldCorpus.Cases.Select(x => x.Group).Distinct()
    .OrderBy(x => x, StringComparer.Ordinal)
    .ToDictionary(group => group,
        group => models.ToDictionary(model => model,
            model => Aggregate(records.Where(x => x.Category == group), model)));

var report = new
{
    Schema = "meltia/boundary-evaluation-v1",
    EvidenceLevel = "B-offline-CSharp-synthetic-intent",
    GoldStatus = "synthetic-intent-labeled-not-human-verified",
    CorpusVersion = "meltia-synthetic-v1.1",
    SourceNotes = new[] {
        "H7 categories: IncrementalBoundaryLab/docs/research-evaluation-standard.md (research branch)",
        "Selected H9 failure cases: IncrementalBoundaryLab/research/2026-10-10-h9-additional-evaluation.md (research/h9-followup-evaluation-20261010)",
        "Remaining cases: handcrafted regression and intent-ambiguity cases; NOT a blind holdout",
        "Meltia-independent: no external English lexicon or Meltype evidence",
        "Meltia-shadow-advised: includes Meltype English suggestions and protected spans; NOT an independent baseline",
        "Meltype-offline: CompositionText.Append per character + CompositionDetector.Segment; NOT Windows TSF"
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
Console.WriteLine("Corpus: " + records.Count + " intent-labeled synthetic cases; " +
    report.DistinctRaw + " distinct raw inputs; " + report.GoldEnglishSpans + " gold English spans");
foreach (var model in models)
{
    var m = overall[model];
    Console.WriteLine(model + " exact=" + m.ExactCases + "/" + m.Cases +
        " topK=" + m.TopKExactCases + "/" + m.Cases +
        " spanGoldRecall=" + Format(m.GoldCandidateRecall) +
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
Console.WriteLine("CATEGORY | Meltype Top1/TopK | Meltia-alone Top1/TopK | Shadow Top1/TopK");
foreach (var category in byCategory)
{
    var a = category.Value["Meltype-offline"];
    var b = category.Value["Meltia-independent"];
    var c = category.Value["Meltia-shadow-advised"];
    Console.WriteLine(category.Key + " | " +
        a.ExactCases + "/" + a.Cases + " / " + a.TopKExactCases + "/" + a.Cases + " | " +
        b.ExactCases + "/" + b.Cases + " / " + b.TopKExactCases + "/" + b.Cases + " | " +
        c.ExactCases + "/" + c.Cases + " / " + c.TopKExactCases + "/" + c.Cases);
}
Console.WriteLine("Evaluation JSON: " + output);
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

static Metrics Aggregate(IEnumerable<CaseRecord> sample, string model)
{
    int n = 0, exact = 0, topK = 0, goldSpans = 0, hits = 0;
    int tp = 0, fp = 0, fn = 0, errors = 0, japanese = 0, falseEnglish = 0;
    var macros = new List<double>();
    foreach (var row in sample)
    {
        n++;
        var p = row.Predictions.Single(x => x.Model == model);
        var isExact = p.Error is null && p.English.SequenceEqual(row.GoldEnglish);
        if (isExact) exact++;
        if (p.Error is null && p.Routes.Any(route => route.SequenceEqual(row.GoldEnglish))) topK++;
        if (p.Error is not null) errors++;
        if (p.Candidates is not null)
        {
            hits += row.GoldEnglish.Count(g => p.Candidates.Contains(g));
        }
        goldSpans += row.GoldEnglish.Count;
        if (row.Category == "S11-japanese-only")
        {
            japanese++;
            if (p.English.Count > 0) falseEnglish++;
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
    return new Metrics(n, exact, topK, errors, goldSpans,
        hasCandidates ? hits : null,
        hasCandidates && goldSpans > 0 ? (double)hits / goldSpans : null,
        tp, fp, fn,
        2*tp+fp+fn > 0 ? 2.0*tp/(2*tp+fp+fn) : null,
        macros.Count > 0 ? macros.Average() : null,
        japanese, falseEnglish);
}

internal sealed record Prediction(string Model, IReadOnlyList<Utf16Span> English,
    IReadOnlyList<IReadOnlyList<Utf16Span>> Routes,
    IReadOnlyList<Utf16Span>? Candidates, string? Error);

internal sealed record CaseRecord(string Id, string Category, string Raw,
    IReadOnlyList<Utf16Span> GoldEnglish, IReadOnlyList<Prediction> Predictions);

internal sealed record Metrics(
    int Cases, int ExactCases, int TopKExactCases, int Errors,
    int GoldSpans, int? GoldCandidateHits, double? GoldCandidateRecall,
    int SwitchTP, int SwitchFP, int SwitchFN,
    double? SwitchF1Micro, double? SwitchF1Macro,
    int JapaneseOnlyCases, int JapaneseOnlyFalseEnglishCases);
