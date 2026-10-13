// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Deterministic synthetic input matrix; it does not simulate Windows keyboard hooks.
using System.Diagnostics;
using System.Text.Json;
using Meltype.Composition;
using Meltia.Core.Boundary;
using static Vocabulary;

if (args.Length != 6 || args[0] != "--tier" ||
    args[1] is not ("smoke" or "full" or "stress") ||
    args[2] != "--seed" || !int.TryParse(args[3], out var seed) ||
    args[4] != "--output" || string.IsNullOrWhiteSpace(args[5]))
{
    Console.Error.WriteLine("Usage: --tier smoke|full|stress --seed 20261013 --output report.json");
    return 64;
}

var perFamily = args[1] switch { "smoke" => 80, "full" => 1000, _ => 10000 };
var options = new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true,
    ExperimentalDiverseCandidatePruning: true,
    ExperimentalJapaneseFlankScoring: true);
var engine = new MeltiaBoundaryEngine(options);
var detector = CompositionDetector.CreateDefault();
var categories = new[] {
    "japanese-only", "english-only", "japanese-english-japanese",
    "english-japanese-english", "three-english-islands", "romaji-variants",
    "ambiguous-intent-pairs", "urls-emails-and-unicode",
    "short-technical-terms", "long-window-and-boundaries"
};
var failures = new List<object>();
var rows = new List<object>();
int total = 0, goldTop1 = 0, goldTopK = 0, truncated = 0, keystrokeChecks = 0;
var clock = Stopwatch.StartNew();

for (int family = 0; family < categories.Length; family++)
{
    var name = categories[family];
    var rng = new Random(unchecked(seed + family * 104729));
    var seen = new HashSet<string>(StringComparer.Ordinal);
    int familyTop1 = 0, familyTopK = 0, familyTruncated = 0, familyKeystrokes = 0;
    int familyFailures = 0;
    string? previousAmbiguous = null;
    for (int i = 0; i < perFamily; i++)
    {
        // Every adjacent ambiguous pair has EXACTLY identical bytes but
        // deliberately contradictory intended language labels.
        var item = MakeCase(family, i, rng, seed);
        seen.Add(item.Raw);
        if (family == 6)
        {
            if (i % 2 == 0) previousAmbiguous = item.Raw;
            else if (previousAmbiguous != item.Raw)
                RecordFailure("ambiguous-pair-input-changed", item, "Opposite intent must use identical raw");
        }
        try
        {
            if (item.Raw.Length == 0 ||
                item.Raw.Length > MeltiaBoundaryEngine.MaxInputLength ||
                item.GoldEnglish.Count + item.GoldJapanese.Count + item.Protected.Count == 0)
                throw new InvalidOperationException("Invalid generated test data");

            // Unassisted classification is diagnostic, NOT a hard pass/fail
            // because a spelling can mean either Japanese or English.
            var observed = engine.Analyze(item.Raw);
            ValidateRoutes(item.Raw, observed);
            if (observed.Routes[0].EnglishSpans.SequenceEqual(item.GoldEnglish))
            {
                familyTop1++;
                goldTop1++;
            }
            if (observed.Routes.Any(r => r.EnglishSpans.SequenceEqual(item.GoldEnglish)))
            {
                familyTopK++;
                goldTopK++;
            }
            if (observed.Truncated) { familyTruncated++; truncated++; }

            // Strict test: explicit user language decisions and protected
            // regions must NOT be discarded, overlapped or auto-committed.
            var evidence = new BoundaryEvidence(
                ExplicitEnglish: item.GoldEnglish,
                ExplicitJapanese: item.GoldJapanese,
                Protected: item.Protected);
            var locked = engine.Analyze(item.Raw, evidence);
            ValidateRoutes(item.Raw, locked);
            foreach (var route in locked.Routes)
            {
                if (item.GoldEnglish.Concat(item.Protected)
                    .Any(span => !route.EnglishSpans.Contains(span)))
                    throw new InvalidOperationException("Explicit English/protected span was dropped");
                if (item.GoldJapanese.Any(span => route.EnglishSpans.Any(span.Overlaps)))
                    throw new InvalidOperationException("Explicit Japanese span was overwritten");
            }

            if (i % 257 == 0)
            {
                var replay = engine.Analyze(item.Raw);
                if (replay.Candidates.Count != observed.Candidates.Count ||
                    !replay.Candidates.SequenceEqual(observed.Candidates) ||
                    replay.Routes.Count != observed.Routes.Count ||
                    !replay.Routes.Zip(observed.Routes, (a, b) =>
                        a.Score.Equals(b.Score) &&
                        a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x))
                    throw new InvalidOperationException("Non-deterministic candidate/route generation");
            }

            // Exercise the actual OS-neutral composition keystroke pipeline.
            // Symbol-rich/UTF-16 inputs remain Core-only: keyboard hooks and
            // Windows IME are NOT simulated by this test.
            if (i % 25 == 0 && family is 0 or 2 or 3 or 5 or 8 &&
                item.Raw.Length <= 100 && item.Raw.All(char.IsAsciiLetter))
            {
                var composition = new CompositionText(detector);
                for (int k = 0; k < item.Raw.Length; k++)
                {
                    composition.Append(item.Raw[k]);
                    if (composition.Raw != item.Raw[..(k + 1)])
                        throw new InvalidOperationException("Keystroke prefix changed/lost input");
                    // Compare at intermediate prefixes, not only after all
                    // keys are entered (no commit to a user application).
                    if (k % 7 == 0 || k == item.Raw.Length - 1)
                    {
                        var pieces = detector.Segment(composition.Units, composition.Pending);
                        if (string.Concat(pieces.Select(p => p.Raw)) != composition.Raw)
                            throw new InvalidOperationException("Composition segment lost input");
                    }
                }
                familyKeystrokes++;
                keystrokeChecks++;
            }
        }
        catch (Exception e)
        {
            familyFailures++;
            if (failures.Count < 60)
                failures.Add(new { Category = name, Index = i, Raw = item.Raw,
                    Error = e.GetType().Name + ": " + e.Message,
                    EnglishGold = item.GoldEnglish });
        }
        total++;
    }
    rows.Add(new {
        Category = name, Cases = perFamily, UniqueRaw = seen.Count,
        ObservedTop1 = familyTop1, ObservedTopK = familyTopK,
        Truncated = familyTruncated, KeystrokeReplays = familyKeystrokes,
        InvariantFailures = familyFailures
    });
    Console.WriteLine($"{name}: {perFamily} examples, distinct={seen.Count}, " +
        $"observed Top1={familyTop1}, TopK={familyTopK}, invariant-failures={familyFailures}");
}
clock.Stop();
var output = Path.GetFullPath(args[5]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var passed = rows.All(row => (int)row.GetType().GetProperty("InvariantFailures")!.GetValue(row)! == 0) &&
    failures.Count == 0;
File.WriteAllText(output, JsonSerializer.Serialize(new {
    Schema = "meltia/input-matrix-v1",
    EvidenceLevel = "synthetic-generator-not-blind-human-labels",
    Engine = "v1.0-experimental-all-options",
    Tier = args[1], Seed = seed, Families = categories.Length,
    CasesPerFamily = perFamily, TotalCases = total,
    ObservedTop1 = goldTop1, ObservedTopK = goldTopK,
    TruncatedCases = truncated, KeystrokeReplays = keystrokeChecks,
    DurationSeconds = Math.Round(clock.Elapsed.TotalSeconds, 3),
    Passed = passed, Summary = rows, Failures = failures,
    Caveats = new[] {
        "Top1/TopK are observations against synthetic intended labels, NOT CI success thresholds.",
        "Unassisted Japanese/English intent cannot always be determined from raw bytes.",
        "Explicit-language protection, round-trip, boundedness and determinism ARE strict assertions.",
        "CompositionText exercises OS-neutral key sequences, not Windows hook/focus/TSF E2E."
    }
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Input matrix: {total} / invariant failures={failures.Count} / " +
    $"keystroke replays={keystrokeChecks}; JSON={output}; duration={clock.Elapsed.TotalSeconds:F1}s");
return passed ? 0 : 1;

void RecordFailure(string name, TestCase item, string reason)
{
    if (failures.Count < 60) failures.Add(new {
        Category = name, Raw = item.Raw, Error = reason
    });
}

static void ValidateRoutes(string raw, BoundaryResult result)
{
    if (result.Raw != raw || result.AutoCommitAllowed ||
        result.Candidates.Count > MeltiaBoundaryEngine.MaxCandidates ||
        result.Routes.Count is < 1 or > MeltiaBoundaryEngine.MaxRoutes)
        throw new InvalidOperationException("Raw, auto-commit, or bounded candidate/route contract failed");
    foreach (var route in result.Routes)
    {
        var end = 0;
        foreach (var span in route.EnglishSpans)
        {
            if (span.Start < end || span.Start < 0 || span.Start >= span.End ||
                span.End > raw.Length || !Boundary(raw, span.Start) || !Boundary(raw, span.End))
                throw new InvalidOperationException("Invalid route span or UTF-16 split");
            end = span.End;
        }
    }
}
static bool Boundary(string raw, int at) => at <= 0 || at >= raw.Length ||
    !(char.IsHighSurrogate(raw[at - 1]) && char.IsLowSurrogate(raw[at]));

static TestCase MakeCase(int family, int number, Random source, int seed)
{
    // Paired gold labels must not accidentally receive different prefixes.
    var rng = family == 6 ? new Random(unchecked(seed ^ (number / 2 * 7919 + 713)))
        : source;
    var list = new List<Piece>();
    void J(string s) => list.Add(new Piece(s, 'J'));
    void E(string s) => list.Add(new Piece(s, 'E'));
    void P(string s) => list.Add(new Piece(s, 'P'));
    string Ja() => Japanese[rng.Next(Japanese.Length)];
    string En() => English[rng.Next(English.Length)];
    string Short() => ShortEnglish[rng.Next(ShortEnglish.Length)];
    string Particle() => Particles[rng.Next(Particles.Length)];
    string Alias() => RomajiAliases[rng.Next(RomajiAliases.Length)][rng.Next(2)];
    switch (family)
    {
        case 0:
            J(Ja()); J(Ja()); J(Ja()); J(Ja());
            break;
        case 1:
            E(En()); E(En()); E(Short());
            break;
        case 2:
            J(Ja()); E(En()); J(Particle()); J(Ja());
            break;
        case 3:
            E(En()); J(Particle()); E(En()); J(Ja());
            break;
        case 4:
            J(Ja()); E(En()); J(Particle()); E(En()); J(Particle());
            E(Short()); J(Ja());
            break;
        case 5:
            J(Alias()); J(Particle()); E(En()); J(Particle()); J(Alias());
            break;
        case 6:
            J(Ja()); J(Particle());
            var term = Ambiguous[rng.Next(Ambiguous.Length)];
            if (number % 2 == 0) E(term); else J(term);
            J(Particle()); J(Ja());
            break;
        case 7:
            J(Ja()); J(" ");
            if (number % 2 == 0)
                P($"https://{Hosts[rng.Next(Hosts.Length)]}/{UrlPaths[rng.Next(UrlPaths.Length)]}");
            else
                P($"{Users[rng.Next(Users.Length)]}@{Hosts[rng.Next(Hosts.Length)]}");
            J(" ");
            J(rng.Next(3) == 0 ? "😀" : Ja());
            break;
        case 8:
            J(Ja()); E(Short()); J(Particle()); E(Short());
            J(Particle()); J(Ja());
            break;
        case 9:
            for (var k = 0; k < 12 + rng.Next(12); k++) J(Ja());
            E(En()); J(Particle()); E(En());
            for (var k = 0; k < 6 + rng.Next(8); k++) J(Ja());
            break;
        default: throw new ArgumentOutOfRangeException(nameof(family));
    }
    var raw = string.Concat(list.Select(x => x.Text));
    var english = new List<Utf16Span>();
    var japanese = new List<Utf16Span>();
    var protect = new List<Utf16Span>();
    var pos = 0;
    foreach (var part in list)
    {
        if (part.Text.Length == 0) throw new InvalidOperationException("Empty matrix piece");
        var span = new Utf16Span(pos, pos + part.Text.Length);
        switch (part.Kind)
        {
            case 'E': english.Add(span); break;
            case 'J': japanese.Add(span); break;
            case 'P': protect.Add(span); break;
        }
        pos = span.End;
    }
    return new TestCase(raw, english, japanese, protect);
}

internal sealed record Piece(string Text, char Kind);
internal sealed record TestCase(string Raw, IReadOnlyList<Utf16Span> GoldEnglish,
    IReadOnlyList<Utf16Span> GoldJapanese, IReadOnlyList<Utf16Span> Protected);

// Each entry is a raw keystroke spelling, not a kana/kanji output.
// The combinatorial generator tests relationships, not linguistic fluency.
internal static class Vocabulary
{
    internal static readonly string[] Japanese = [
        "nihongo", "watashiha", "kyouha", "kinouha", "korekara",
        "benkyousuru", "wotamesu", "dekinai", "shigoto", "arigatou",
        "tsukatte", "gakkou", "jissou", "toukyou", "omou", "desu",
        "wakaranai", "kotoba", "nankai", "kangaeru", "mitsukeru"
    ];
    internal static readonly string[] English = [
        "compiler", "cryptography", "semiconductor", "photonics",
        "repository", "synchronization", "firmware", "interoperability",
        "neuroscience", "hyperspectral", "calibration", "development",
        "application", "performance", "deployment", "integration",
        "authentication", "virtualization", "microprocessor",
        "configuration", "accessibility", "transcription", "reflection"
    ];
    internal static readonly string[] ShortEnglish = [
        "GitHub", "Python", "Linux", "Docker", "React", "Swift", "Meltia",
        "commit", "SPYAIR", "JSON", "GPU", "API", "CSharp", "HTTP",
        "Rust", "Java", "TypeScript", "Ubuntu", "shell", "build"
    ];
    internal static readonly string[] Particles = [
        "no", "de", "wo", "ga", "ni", "to", "ha"
    ];
    internal static readonly string[][] RomajiAliases = [
        ["shi", "si"], ["chi", "ti"], ["tsu", "tu"], ["ji", "zi"],
        ["cha", "tya"], ["chu", "tyu"], ["cho", "tyo"],
        ["sha", "sya"], ["shu", "syu"], ["sho", "syo"]
    ];
    internal static readonly string[] Ambiguous = [
        "sake", "take", "kaki", "no", "ime", "tokyo"
    ];
    internal static readonly string[] Hosts = [
        "example.org", "example.com", "docs.example.net", "test.example.org"
    ];
    internal static readonly string[] UrlPaths = [
        "docs", "issue/18", "meltia", "index.html", "v1/beta", "api"
    ];
    internal static readonly string[] Users = [
        "dev", "support", "test", "fugu", "meltia", "feedback"
    ];
}
