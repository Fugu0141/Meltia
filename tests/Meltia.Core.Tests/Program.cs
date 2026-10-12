// SPDX-License-Identifier: GPL-3.0-or-later
using Meltia.Core.Boundary;

var engine = new MeltiaBoundaryEngine();
int passed = 0;
void Check(bool condition, string title)
{
    if (!condition) throw new Exception("FAILED: " + title);
    Console.WriteLine("PASS " + title);
    passed++;
}
Check(typeof(MeltiaBoundaryEngine).Assembly.GetName().Version is { Major: 1, Minor: 0 },
    "Meltia Core assembly development version is v1.0");
bool Has(BoundaryResult result, params Utf16Span[] spans) =>
    result.Routes.Any(route => spans.All(s => route.EnglishSpans.Contains(s)));
var japanese = engine.Analyze("nihongo", new BoundaryEvidence(EnglishWords:["go"]));
Check(!japanese.Routes[0].EnglishSpans.Any(), "nihongo does not become ...go");
Check(japanese.Candidates.Any(c => c.Span == new Utf16Span(5, 7)), "ambiguity is retained as candidate");
var next = engine.Analyze("ashitahaNIHONGOde", new BoundaryEvidence(EnglishWords:["as", "go"]));
Check(!next.Routes[0].EnglishSpans.Any(), "fully phonetic islands are not split");
Check(!engine.Analyze("tokyo", new BoundaryEvidence(EnglishWords:["tokyo"])).Routes[0].EnglishSpans.Any(),
    "romaji-readable ambiguous English remains Japanese");
Check(Has(engine.Analyze("tokyo", new BoundaryEvidence(ExplicitEnglish:[new Utf16Span(0, 5)])),new Utf16Span(0, 5)),
    "explicit English lock overrides ambiguity");
Check(!engine.Analyze("tokyo", new BoundaryEvidence(ExplicitJapanese:[new Utf16Span(0, 5)],
    EnglishWords:["tokyo"])).Routes[0].EnglishSpans.Any(), "explicit Japanese lock");
Check(Has(engine.Analyze("reflectsareta",new BoundaryEvidence(EnglishWords:["reflect"])),
    new Utf16Span(0, 7)), "reflect Japanese suffix");
Check(Has(engine.Analyze("handanwodelegatesuru",new BoundaryEvidence(EnglishWords:["delegate"])),
    new Utf16Span(8, 16)), "delegate in mixed romaji");
Check(Has(engine.Analyze("kyouhaKingGnudeSPYAIRde"),
    new Utf16Span(6, 13), new Utf16Span(15, 21)), "two distinct artist-name islands");
Check(Has(engine.Analyze("kyouhaAimerde"),new Utf16Span(6, 11)), "unknown capitalized name");
Check(Has(engine.Analyze("hello@example.com"),new Utf16Span(0, 17)), "email protected");
Check(Has(engine.Analyze("https://example.com"),new Utf16Span(0, 19)), "URL protected");
Check(!engine.Analyze("NIHONGO").AutoCommitAllowed, "never auto commit");
Check(engine.Analyze("to" + new string('a', 1000)).Candidates.Count<=128, "bounded input candidate set");
Check(engine.Analyze("kyouhaKingGnudeSPYAIRde").Routes.Count<=8, "bounded paths");
bool rejected = false;
try { engine.Analyze("😀a",new BoundaryEvidence(ExplicitEnglish:[new Utf16Span(1, 2)])); }
catch(ArgumentException) { rejected=true; }
Check(rejected,"surrogate pair may not be split");
var session = new MeltiaSession(engine);
session.Insert("nihongo");
var before = session.Analyze(["go"]);
session.MoveCaret(3);
session.Insert("to");
Check(session.Analyze(["go"]).Raw=="nihtoongo", "middle insert recomputes full input");
session.Backspace(); session.Backspace();
Check(session.Analyze(["go"]).Routes[0].EnglishSpans.SequenceEqual(before.Routes[0].EnglishSpans),
    "editing restores exact boundary path");
session.Clear();
session.Insert("😀a"); session.MoveCaret(2); session.Backspace();
Check(session.Raw=="a", "UTF-16-aware backspace");

// Regression: long mixed sentences can contain more than four protected/locked
// Latin islands. Dropping the fifth mandatory span used to make DP impossible.
var sixEnglish = "ok ok ok ok ok ok ";
var sixLocks = Enumerable.Range(0, 6).Select(i => new Utf16Span(3 * i, 3 * i + 2)).ToArray();
var sixResult = engine.Analyze(sixEnglish, new BoundaryEvidence(ExplicitEnglish: sixLocks));
Check(sixResult.Routes[0].EnglishSpans.SequenceEqual(sixLocks),
    "six independent explicit English spans survive the route search");
Check(sixResult.Routes.Count <= MeltiaBoundaryEngine.MaxRoutes && !sixResult.AutoCommitAllowed,
    "long mandatory route remains bounded and never commits");
var sameLock = new Utf16Span(0, 5);
var duplicateLocks = engine.Analyze("tokyo", new BoundaryEvidence(
    ExplicitEnglish: [sameLock, sameLock], Protected: [sameLock]));
Check(duplicateLocks.Routes[0].EnglishSpans.SequenceEqual([sameLock]),
    "identical explicit and protected locks are idempotent");
bool overlapRejected = false;
try { engine.Analyze("abcdef", new BoundaryEvidence(
    ExplicitEnglish: [new Utf16Span(0, 4), new Utf16Span(2, 6)])); }
catch (ArgumentException) { overlapRejected = true; }
Check(overlapRejected, "distinct overlapping mandatory spans are still rejected");
var urlToken = "https://a.co";
var sixUrls = string.Join(" ", Enumerable.Repeat(urlToken, 6));
var sixUrlSpans = Enumerable.Range(0, 6)
    .Select(i => new Utf16Span(i * (urlToken.Length + 1),
        i * (urlToken.Length + 1) + urlToken.Length)).ToArray();
var manyUrls = engine.Analyze(sixUrls);
Check(manyUrls.Routes[0].EnglishSpans.SequenceEqual(sixUrlSpans),
    "six separate protected URLs retain their original raw boundaries");

// Seeded metamorphic evaluation: deterministic read-only output, ordered
// non-overlapping valid UTF-16 spans, and no loss of raw input over 300 samples.
// These are invariant checks, NOT 300 human-labeled accuracy measurements.
var seed = new Random(20261012);
var fragments = new[] { "ni", "hon", "go", "Tokyo", "SPYAIR", "あ",
    "😀", " ", "https://a.co", "-", "ga", "de", "KingGnu" };
var invariantOk = true;
for (int sample = 0; sample < 300; sample++)
{
    var rawSample = string.Concat(Enumerable.Range(0, seed.Next(1, 13))
        .Select(_ => fragments[seed.Next(fragments.Length)]));
    var evidenceSample = new BoundaryEvidence(EnglishWords: ["go", "tokyo", "SPYAIR"]);
    var a = engine.Analyze(rawSample, evidenceSample);
    var b = engine.Analyze(rawSample, evidenceSample);
    invariantOk &= a.Raw == rawSample && b.Raw == rawSample
        && !a.AutoCommitAllowed && !b.AutoCommitAllowed
        && a.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates
        && a.Routes.Count is > 0 and <= MeltiaBoundaryEngine.MaxRoutes
        && a.Routes.Count == b.Routes.Count;
    if (!invariantOk) break;
    for (int j = 0; j < a.Routes.Count; j++)
    {
        var left = a.Routes[j];
        var right = b.Routes[j];
        invariantOk &= left.Score.Equals(right.Score)
            && left.EnglishSpans.SequenceEqual(right.EnglishSpans);
        var previousEnd = 0;
        foreach (var span in left.EnglishSpans)
        {
            invariantOk &= span.Start >= previousEnd && span.End <= rawSample.Length
                && span.Start < span.End
                && !(span.Start > 0 && span.Start < rawSample.Length &&
                     char.IsHighSurrogate(rawSample[span.Start - 1]) &&
                     char.IsLowSurrogate(rawSample[span.Start]))
                && !(span.End > 0 && span.End < rawSample.Length &&
                     char.IsHighSurrogate(rawSample[span.End - 1]) &&
                     char.IsLowSurrogate(rawSample[span.End]));
            previousEnd = span.End;
        }
    }
    if (!invariantOk) break;
}
Check(invariantOk, "300 seeded mixed-script invariant and determinism cases");

var editSession = new MeltiaSession(engine);
var editRandom = new Random(20261013);
var editValid = true;
for (int i = 0; i < 300; i++)
{
    var movePositions = Enumerable.Range(0, editSession.Raw.Length + 1)
        .Where(x => x == 0 || x == editSession.Raw.Length ||
            !(char.IsHighSurrogate(editSession.Raw[x - 1]) &&
              char.IsLowSurrogate(editSession.Raw[x]))).ToArray();
    editSession.MoveCaret(movePositions[editRandom.Next(movePositions.Length)]);
    switch (editRandom.Next(3))
    {
        case 0:
            var insert = fragments[editRandom.Next(fragments.Length)];
            if (editSession.Raw.Length + insert.Length < MeltiaBoundaryEngine.MaxInputLength)
                editSession.Insert(insert);
            break;
        case 1: editSession.Backspace(); break;
        case 2: editSession.DeleteForward(); break;
    }
    var state = editSession.Analyze(["go", "tokyo"]);
    editValid &= state.Raw == editSession.Raw && !state.AutoCommitAllowed
        && state.Routes.Count > 0;
    if (!editValid) break;
}
Check(editValid, "300 seeded cursor/edit recomputations preserve raw and route");


// Optional research mode: lexical membership alone should not reward
// unrelated two-letter substrings inside Japanese phonetic suffixes.
var research = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalStrongHostAdvisory: true,
    ExperimentalFragmentationPenalty: true));
var isolatedLexicon = research.Analyze("commitsimasita",
    new BoundaryEvidence(EnglishWords: ["commit", "as", "it"]));
Check(isolatedLexicon.Routes[0].EnglishSpans.SequenceEqual([new Utf16Span(0, 6)]),
    "experimental cost keeps commit without fragmenting Japanese suffix into as/it");
var readableAdvisory = research.Analyze("tokyo",
    new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 5)]));
Check(readableAdvisory.Routes[0].EnglishSpans.Count == 0,
    "experiment does not force readable lowercase Japanese romanization to English");
var romajiUpperAdvisory = research.Analyze("NIHONGO",
    new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 7)]));
Check(romajiUpperAdvisory.Routes[0].EnglishSpans.Count == 0,
    "experiment does not promote all-capitals long Japanese word");
var shortJapaneseUpper = research.Analyze("KURU",
    new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 4)]));
Check(shortJapaneseUpper.Routes[0].EnglishSpans.Count == 0,
    "unlisted uppercase Japanese romaji does not become an English acronym");
var strongAdvisory = research.Analyze("commit",
    new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 6)]));
Check(strongAdvisory.Routes[0].EnglishSpans.SequenceEqual([new Utf16Span(0, 6)]),
    "experiment promotes non-phonetic host English evidence");
var shortUpper = research.Analyze("IME",
    new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 3)]));
Check(shortUpper.Routes[0].EnglishSpans.SequenceEqual([new Utf16Span(0, 3)]),
    "experiment recognizes host-identified short uppercase acronym");
var swallowed = research.Analyze("kyouhaKingGnudeSPYAIRde",
    new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 23)]));
Check(!swallowed.Routes[0].EnglishSpans.Contains(new Utf16Span(0, 23)),
    "experiment refuses whole-sentence lowercase+uppercase host swallowing");
Check(research.Analyze("tokyo",
    new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 5)])).Routes[0]
        .EnglishSpans.SequenceEqual([new Utf16Span(0, 5)]),
    "explicit user English lock still takes precedence in research mode");
Check(!research.Analyze("commit").AutoCommitAllowed,
    "experimental routing is never allowed to commit text");

// v0.7 independent candidate generation. This deliberately tests whether the
// *candidate exists*, not whether English user intent is already inferred.
// Untyped Japanese remains untouched, and old mode remains byte-for-byte stable.
var v07 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalFragmentationPenalty: true,
    ExperimentalUnknownLatinCandidates: true));
var v07DefaultPenalty = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true));
Check(v07DefaultPenalty.Analyze("kinouhastrategywakaru").Routes[0].EnglishSpans.Count == 0,
    "v0.7 enables cautious fragment penalty even when caller did not request it");
Check(v07DefaultPenalty.Analyze("kinouhastrategywakaru").Routes[0].EnglishSpans
        .SequenceEqual(v07.Analyze("kinouhastrategywakaru").Routes[0].EnglishSpans),
    "v0.7 implicit safety cost matches explicit candidate mode");
bool Proposed(BoundaryResult result, int start, int end) =>
    result.Candidates.Any(x =>
        x.Span == new Utf16Span(start, end) &&
        x.Origin == "v0.7-unknown-latin-provisional");
Check(Proposed(v07.Analyze("kinouhastrategywakaru"), 7, 15),
    "v0.7 exposes unknown strategy inside Japanese flanks");
var v07Two = v07.Analyze("kinouhastrategynorequirementtoiu");
Check(Proposed(v07Two, 7, 15) && Proposed(v07Two, 17, 28),
    "v0.7 retains two independent unknown English islands");
Check(Proposed(v07.Analyze("korekaraknowledge.wakaru"), 8, 17),
    "v0.7 stops the unknown English span before a punctuation mark");
Check(!v07.Analyze("dokoniakerusonomamakyouno").Candidates
    .Any(x => x.Origin == "v0.7-unknown-latin-provisional"),
    "v0.7 never splits completely phonetic Japanese sample");
Check(!v07.Analyze("nihongo").Candidates
    .Any(x => x.Origin == "v0.7-unknown-latin-provisional"),
    "v0.7 leaves simple Japanese phonetic sequence unchanged");
Check(!engine.Analyze("kinouhastrategywakaru").Candidates
    .Any(x => x.Origin == "v0.7-unknown-latin-provisional"),
    "v0.7 remains disabled by default");
var v07Ambiguous = v07.Analyze("tokyo");
Check(!v07Ambiguous.Routes[0].EnglishSpans.Any() &&
      !v07Ambiguous.Candidates.Any(x => x.Origin == "v0.7-unknown-latin-provisional"),
    "v0.7 does not hallucinate an English label for ambiguous Tokyo");
Check(!v07.Analyze("kinouhastrategywakaru").AutoCommitAllowed,
    "v0.7 hypotheses can never auto-commit to an application");
var v07Repeat = v07.Analyze("kinouhastrategynorequirementtoiu");
Check(v07Repeat.Candidates.SequenceEqual(v07Two.Candidates) &&
      v07Repeat.Routes.Count == v07Two.Routes.Count &&
      v07Repeat.Routes.Zip(v07Two.Routes,
          (a, b) => a.Score.Equals(b.Score) &&
                    a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x),
    "v0.7 candidate lattice and Top-K ordering are deterministic");
Check(v07.Analyze(new string('b', 1000)).Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates,
    "v0.7 skips enormous ASCII runs within explicit complexity bound");
var hostEvidence = new BoundaryEvidence(
    MeltypeEnglish: [new Utf16Span(7, 15)],
    EnglishWords: ["strategy", "as", "no"]);
var priorResearch = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalFragmentationPenalty: true)).Analyze(
        "kinouhastrategywakaru", hostEvidence);
var v07Research = v07.Analyze("kinouhastrategywakaru", hostEvidence);
Check(priorResearch.Candidates.All(c => v07Research.Candidates.Contains(c)),
    "v0.7 candidate generation preserves host/lexicon spans and scores");
Check(v07Research.Candidates.Any(c => c.Span == new Utf16Span(7, 15) &&
        c.Origin != "v0.7-unknown-latin-provisional"),
    "v0.7 never overwrites the origin of an existing reference candidate");
Check(v07Research.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates &&
      v07Research.Routes.Count <= MeltiaBoundaryEngine.MaxRoutes,
    "v0.7 preserves the global candidate and route caps");

// Seeded adversarial stream safety. This does not use gold: only checks
// deterministic, bounded output and immutable UTF-16 raw interpretation.
var v07Rng = new Random(20261015);
string[] v07Bits = [
    "nihongo", "kinouha", "strategy", "no", "requirement", "😀",
    "knowledge.", "wakaru", "feature", "database", " ", "KURU",
    "commit", "https://a.co", "@", "ga", "SPYAIR", "API"
];
var v07PropertyOk = true;
for (int i = 0; i < 120; i++)
{
    var input = string.Concat(Enumerable.Range(0, v07Rng.Next(1, 12))
        .Select(_ => v07Bits[v07Rng.Next(v07Bits.Length)]));
    var a = v07.Analyze(input);
    var b = v07.Analyze(input);
    v07PropertyOk &= a.Raw == input && b.Raw == input &&
        !a.AutoCommitAllowed && !b.AutoCommitAllowed &&
        a.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates &&
        a.Routes.Count is > 0 and <= MeltiaBoundaryEngine.MaxRoutes &&
        a.Candidates.SequenceEqual(b.Candidates) &&
        a.Routes.Zip(b.Routes, (x, y) =>
            x.Score == y.Score && x.EnglishSpans.SequenceEqual(y.EnglishSpans))
            .All(x => x) &&
        a.Routes.Count == b.Routes.Count;
    foreach (var route in a.Routes)
    {
        var previousEnd = 0;
        foreach (var span in route.EnglishSpans)
        {
            v07PropertyOk &= span.Start >= previousEnd && span.End <= input.Length &&
                span.Start < span.End &&
                !(span.Start > 0 && span.Start < input.Length &&
                    char.IsHighSurrogate(input[span.Start - 1]) &&
                    char.IsLowSurrogate(input[span.Start])) &&
                !(span.End > 0 && span.End < input.Length &&
                    char.IsHighSurrogate(input[span.End - 1]) &&
                    char.IsLowSurrogate(input[span.End]));
            previousEnd = span.End;
        }
    }
    if (!v07PropertyOk) break;
}
Check(v07PropertyOk, "v0.7 120 seeded mixed Japanese-English-emoji invariant cases");


// v0.8: the Japanese complement must have valid phonetic paths whenever
// possible. V0.7 is deliberately kept as the reference branch/engine.
var v08 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true));
var v07Snapshot = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true));
Check(!v08.Analyze("nihongo").Routes[0].EnglishSpans.Any(),
    "v0.8 preserves fully readable Japanese-only input");
Check(!v08.Analyze("dokoniakerusonomamakyouno").Routes[0].EnglishSpans.Any(),
    "v0.8 phonetic path does not invent an English island in Japanese-only control");
Check(v08.Analyze("strategy").Routes[0].EnglishSpans
      .SequenceEqual([new Utf16Span(0, 8)]),
    "v0.8 selects a standalone unreadable unknown English word");
Check(Has(v08.Analyze("kinouhastrategywakaru"),
    new Utf16Span(7, 15)),
    "v0.8 retains unknown English between Japanese syllables in Top-K");
var v08Multi = v08.Analyze("kinouhastrategynorequirementtoiu");
Console.WriteLine("DIAG v0.8 multi candidateGold=" +
    v08Multi.Candidates.Any(c => c.Span == new Utf16Span(7, 15)) + "," +
    v08Multi.Candidates.Any(c => c.Span == new Utf16Span(17, 28)));
foreach (var route in v08Multi.Routes)
    Console.WriteLine("DIAG v0.8 multi score=" + route.Score.ToString("F2") +
        " spans=" + string.Join(",", route.EnglishSpans.Select(
            z => z.Start + "-" + z.End)));
Check(v08Multi.Candidates.Any(c => c.Span == new Utf16Span(7, 15)) &&
    v08Multi.Candidates.Any(c => c.Span == new Utf16Span(17, 28)) &&
    v08Multi.Routes.Any(route => route.EnglishSpans.Count >= 2),
    "v0.8 preserves both unknown gold candidates and at least one multi-island route");
// A full two-island exact Top-K is a *quality metric*, not an invariant:
// previous evidence demonstrates that the original 8-path beam may keep
// shorter, incorrect English spans. The fixed evaluator reports this miss.
Check(v08.Analyze("tokyo").Routes[0].EnglishSpans.Count == 0 &&
    v08.Analyze("IME", new BoundaryEvidence(
        ExplicitEnglish: [new Utf16Span(0, 3)])).Routes[0].EnglishSpans
        .SequenceEqual([new Utf16Span(0, 3)]),
    "v0.8 honors ambiguous Japanese and explicit English locks");
Check(v08.Analyze("https://example.com").Routes[0].EnglishSpans
        .SequenceEqual([new Utf16Span(0, 19)]),
    "v0.8 keeps protected URLs mandatory");
Check(v08.Analyze("ka😀strategy").Routes.All(route =>
    route.EnglishSpans.All(span => !(span.Start > 0 &&
        char.IsHighSurrogate("ka😀strategy"[span.Start - 1]) &&
        char.IsLowSurrogate("ka😀strategy"[span.Start])))),
    "v0.8 does not split surrogate pairs");
Check(v08.Analyze("kinouhastrategywakaru").Candidates
    .SequenceEqual(v07Snapshot.Analyze("kinouhastrategywakaru").Candidates),
    "v0.8 changes the decoder only, not v0.7 candidate generation");
Check(v08Multi.Routes.Count <= MeltiaBoundaryEngine.MaxRoutes &&
    v08Multi.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates &&
    !v08Multi.AutoCommitAllowed,
    "v0.8 maintains bounded routes and read-only contract");
var v08Seed = new Random(20261018);
var v08Ok = true;
for (int k = 0; k < 120; ++k)
{
    var typed = string.Concat(Enumerable.Range(0, v08Seed.Next(1, 9))
        .Select(_ => v07Bits[v08Seed.Next(v07Bits.Length)]));
    var x = v08.Analyze(typed);
    var y = v08.Analyze(typed);
    v08Ok &= x.Candidates.SequenceEqual(y.Candidates) &&
        x.Routes.Count == y.Routes.Count &&
        x.Routes.Zip(y.Routes, (a, b) =>
            a.Score.Equals(b.Score) &&
            a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(ok => ok) &&
        x.Routes.Count is > 0 and <= MeltiaBoundaryEngine.MaxRoutes &&
        !x.AutoCommitAllowed;
    if (!v08Ok) break;
}
Check(v08Ok, "v0.8 deterministic 120-case seeded mixed-script lattice evaluation");


// v0.9 opt-in scoring: same candidate inventory as v0.8; only the route
// relationship of nonphonetic English islands connected by Japanese
// particles is allowed to change. No learned gold words or auto-commit.
var v09 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true));
const string v09TwoRaw = "kinouhastrategynorequirementtoiu";
var v09Two = v09.Analyze(v09TwoRaw);
Check(v09Two.Candidates.SequenceEqual(v08.Analyze(v09TwoRaw).Candidates),
    "v0.9 does not change v0.8 English proposal inventory");
Check(v09Two.Routes.Any(r => r.EnglishSpans.Count >= 2),
    "v0.9 keeps multi-English-island candidate routes");
Check(!v09.Analyze("dokoniakerusonomamakyouno").Routes[0].EnglishSpans.Any() &&
      !v09.Analyze("nihongo").Routes[0].EnglishSpans.Any() &&
      !v09.Analyze("tokyo").Routes[0].EnglishSpans.Any(),
    "v0.9 does not promote Japanese-only or Tokyo ambiguity to English");
Check(v09.Analyze("https://example.com").Routes[0].EnglishSpans
        .SequenceEqual([new Utf16Span(0, 19)]) &&
      Has(v09.Analyze("tokyo", new BoundaryEvidence(
          ExplicitEnglish: [new Utf16Span(0, 5)])), new Utf16Span(0, 5)),
    "v0.9 keeps mandatory locks and protected URL handling");
var v09Repeat = v09.Analyze(v09TwoRaw);
Check(v09Two.Candidates.SequenceEqual(v09Repeat.Candidates) &&
      v09Two.Routes.Count == v09Repeat.Routes.Count &&
      v09Two.Routes.Zip(v09Repeat.Routes, (a, b) =>
          a.Score.Equals(b.Score) &&
          a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x) &&
      !v09Two.AutoCommitAllowed,
    "v0.9 remains deterministic and read-only");
Check(!v09.Analyze("ime").Routes[0].EnglishSpans.Any() &&
      !v09.Analyze("japaneseno").Routes[0].EnglishSpans.Any(),
    "v0.9 maintains Japanese preference for lowercase IME and japaneseno");
Check(Has(v09.Analyze("ime", new BoundaryEvidence(
        ExplicitEnglish: [new Utf16Span(0, 3)])), new Utf16Span(0, 3)),
    "v0.9 explicit user English selection resolves ambiguous ime");
var invalidV09ModeRejected = false;
try
{
    _ = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
        ExperimentalContextualIslandScoring: true));
}
catch (ArgumentException)
{
    invalidV09ModeRejected = true;
}
Check(invalidV09ModeRejected,
    "v0.9 rejects contradictory configuration without lattice decoder");


// v1.0 remains a read-only boundary engine, and flags uncertainty rather
// than claiming to infer the intended language from a romanizable spelling.
var v10 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true));
var v09Final = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true));
var v10Raw = "kinouhastrategynorequirementtoiu";
var v10Diagnostic = v10.Analyze(v10Raw);
var v09Diagnostic = v09.Analyze(v10Raw);
Check(v10Diagnostic.Review is not null && !v10Diagnostic.AutoCommitAllowed,
    "v1.0 review is opt-in diagnostic with no commit permission");
Check(v09Diagnostic.Review is null &&
      v10Diagnostic.Candidates.SequenceEqual(v09Diagnostic.Candidates) &&
      v10Diagnostic.Routes.Count == v09Diagnostic.Routes.Count &&
      v10Diagnostic.Routes.Zip(v09Diagnostic.Routes, (x, y) =>
          x.Score.Equals(y.Score) &&
          x.EnglishSpans.SequenceEqual(y.EnglishSpans)).All(z => z),
    "v1.0 review preserves v0.9 Top1 TopK and candidate evidence");
Check(v10.Analyze("tokyo", new BoundaryEvidence(
    EnglishWords: ["tokyo"])).Review is { NeedsConfirmation: true,
        Reason: "romanized-japanese-english-intent-unresolved" } t1 &&
    t1.UnresolvedReadableEnglish.SequenceEqual([new Utf16Span(0, 5)]),
    "v1.0 treats readable Tokyo English intent as unresolved");
Check(v10.Analyze("ime", new BoundaryEvidence(
    EnglishWords: ["ime"])).Review is { NeedsConfirmation: true } t2 &&
    t2.UnresolvedReadableEnglish.SequenceEqual([new Utf16Span(0, 3)]),
    "v1.0 treats lowercase ime as unresolved with lexical evidence");
Check(v10.Analyze("tokyo", new BoundaryEvidence(
    EnglishWords: ["tokyo"],
    ExplicitEnglish: [new Utf16Span(0, 5)])).Review is { NeedsConfirmation: false } t3 &&
    t3.UnresolvedReadableEnglish.Count == 0 &&
    t3.StableEnglishSpans.SequenceEqual([new Utf16Span(0, 5)]),
    "v1.0 explicit intent overrides ambiguity without losing mandatory lock");
Check(v10.Analyze("nihongo", new BoundaryEvidence(
    EnglishWords: ["go"])).Review is { UnresolvedReadableEnglish.Count: 0 },
    "v1.0 does not mistake the go substring in nihongo for standalone English");
Check(v10.Analyze("tokyo", new BoundaryEvidence(
    EnglishWords: ["tokyo"],
    ExplicitJapanese: [new Utf16Span(0, 5)])).Review is
    { NeedsConfirmation: false, UnresolvedReadableEnglish.Count: 0 },
    "v1.0 respects explicit Japanese choice against English dictionary ambiguity");
Check(v10.Analyze("tokyo").Review is { NeedsConfirmation: false,
        UnresolvedReadableEnglish.Count: 0 },
    "v1.0 never invents English intent evidence for untagged Tokyo");
Check(v10.Analyze("https://example.com").Review is { NeedsConfirmation: false } urlReview &&
    urlReview.StableEnglishSpans.SequenceEqual([new Utf16Span(0, 19)]),
    "v1.0 preserves protected URL despite any competing route");
Check(v10.Analyze("nihongo").Routes[0].EnglishSpans.Count == 0 &&
    v10.Analyze("japaneseno").Routes[0].EnglishSpans.Count == 0 &&
    v10.Analyze("ime").Routes[0].EnglishSpans.Count == 0,
    "v1.0 maintains Japanese-default lower-case ambiguity safety");
Check(v10Diagnostic.Review!.StableEnglishSpans.All(x =>
        v10Diagnostic.Routes[0].EnglishSpans.Contains(x)) &&
    v10Diagnostic.Review.ContestedEnglishSpans.All(x =>
        v10Diagnostic.Routes[0].EnglishSpans.Contains(x)) &&
    v10Diagnostic.Review.StableEnglishSpans.Concat(
        v10Diagnostic.Review.ContestedEnglishSpans).Distinct().Count() ==
        v10Diagnostic.Routes[0].EnglishSpans.Count,
    "v1.0 review partitions all Top1 English spans without omission");
// Incremental recomputation invariants: callers will eventually analyze
// prefixes and edits from Windows input sessions, but this does not
// pretend that real Windows TSF timing or IME integration was tested.
var v10ReplayParts = new[] {
    "kyouha", "strategy", "no", "requirement", "toiu",
    "tokyo", "😀", ".", "API", "de", "https://a.co", " ", "nihongo"
};
var v10Replay = "";
var v10PrefixSafe = true;
foreach (var part in v10ReplayParts)
{
    v10Replay += part;
    for (int i = 0; i <= v10Replay.Length; i++)
    {
        if (i > 0 && i < v10Replay.Length &&
            char.IsHighSurrogate(v10Replay[i - 1]) &&
            char.IsLowSurrogate(v10Replay[i])) continue;
        var prefix = v10Replay[..i];
        var current = v10.Analyze(prefix);
        var rerun = v10.Analyze(prefix);
        v10PrefixSafe &= current.Raw == prefix &&
            !current.AutoCommitAllowed && current.Review is not null &&
            current.Candidates.SequenceEqual(rerun.Candidates) &&
            current.Routes.Count == rerun.Routes.Count &&
            current.Routes.Zip(rerun.Routes, (a, b) =>
                a.Score.Equals(b.Score) &&
                a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(z => z);
        if (!v10PrefixSafe) break;
    }
    if (!v10PrefixSafe) break;
}
Check(v10PrefixSafe, "v1.0 all legal incremental prefixes are read-only and deterministic");


// v1.0 quality release blocker: the exact same raw text can mean Japanese
// or English. An orthography-only model cannot satisfy both labels without
// user-supplied intention. Check both explicit choices work reproducibly.
var intentPairs = new[] { "tokyo", "ime", "sake", "take", "kaki", "no" };
var pairedIntentOk = true;
foreach (var rawIntent in intentPairs)
{
    var whole = new Utf16Span(0, rawIntent.Length);
    var englishChoice = v10.Analyze(rawIntent, new BoundaryEvidence(
        EnglishWords: [rawIntent], ExplicitEnglish: [whole]));
    var japaneseChoice = v10.Analyze(rawIntent, new BoundaryEvidence(
        EnglishWords: [rawIntent], ExplicitJapanese: [whole]));
    var unresolved = v10.Analyze(rawIntent, new BoundaryEvidence(
        EnglishWords: [rawIntent]));
    pairedIntentOk &= englishChoice.Routes[0].EnglishSpans.SequenceEqual([whole]) &&
        japaneseChoice.Routes[0].EnglishSpans.Count == 0 &&
        englishChoice.Review is { UnresolvedReadableEnglish.Count: 0 } &&
        japaneseChoice.Review is { UnresolvedReadableEnglish.Count: 0 } &&
        unresolved.Review is { NeedsConfirmation: true } &&
        !englishChoice.AutoCommitAllowed && !japaneseChoice.AutoCommitAllowed;
}
Check(pairedIntentOk,
    "v1.0 6 contradictory-intent spellings obey both explicit language locks and remain reversible");

// Mandatory/protected inputs must remain mandatory even when the tentative
// path search chooses a different language hypothesis elsewhere in the input.
var mixedWithUrl = "ka😀https://example.org/docs ni compiler";
var mixedWithUrlResult = v10.Analyze(mixedWithUrl);
Check(mixedWithUrlResult.Raw == mixedWithUrl &&
    mixedWithUrlResult.Routes.All(r =>
        r.EnglishSpans.Contains(new Utf16Span(4, 28))) &&
    !mixedWithUrlResult.AutoCommitAllowed,
    "v1.0 protected URL after a surrogate pair survives Top-K and never commits");


// v1.0 phase 2: short inputs must be fully equivalent to v1.0 without
// windowing. For >96 ASCII runs, the older v0.7 generator returned no
// unknown-Latin candidates at all; overlapping windows restore coverage.
var phase2 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true));
var phase2Legacy = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true));
var shortLongParity = true;
foreach (var shortSample in new[] {
    "kyouhaphotonicswotukau", "kyouhaKingGnudeSPYAIRde",
    "nihongodearigatou", "tokyo", "ime", "https://example.org/docs"
})
{
    var before = phase2Legacy.Analyze(shortSample);
    var after = phase2.Analyze(shortSample);
    shortLongParity &= before.Candidates.SequenceEqual(after.Candidates) &&
        before.Routes.Count == after.Routes.Count &&
        before.Routes.Zip(after.Routes, (a, b) =>
            a.Score.Equals(b.Score) &&
            a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x);
}
Check(shortLongParity,
    "v1.0 long-run opt-in preserves short-run candidate and route equality");

var longRomaji = string.Concat(Enumerable.Repeat("nihongo", 20));
var longRomajiResult = phase2.Analyze(longRomaji);
Check(longRomajiResult.Candidates.Count == 0 &&
    longRomajiResult.Routes[0].EnglishSpans.Count == 0 &&
    !longRomajiResult.AutoCommitAllowed,
    "v1.0 long Japanese-only romanized stream never proposes English");

var longMixed = string.Concat(Enumerable.Repeat("nihongo", 16)) +
    "neuroscience" + string.Concat(Enumerable.Repeat("nihongo", 5));
var desiredLongSpan = new Utf16Span(112, 124);
var legacyLong = phase2Legacy.Analyze(longMixed);
var tiledLong = phase2.Analyze(longMixed);
Check(longMixed.Length > 96 && legacyLong.Candidates.Count == 0 &&
    tiledLong.Candidates.Any(c => c.Span == desiredLongSpan) &&
    tiledLong.Raw == longMixed && !tiledLong.AutoCommitAllowed,
    "v1.0 long-run tiling recovers a precise English candidate previously omitted");
Check(tiledLong.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates &&
    tiledLong.Routes.Count <= MeltiaBoundaryEngine.MaxRoutes,
    "v1.0 windowed generation respects hard candidate and route caps");
var tiledLongRepeat = phase2.Analyze(longMixed);
Check(tiledLong.Candidates.SequenceEqual(tiledLongRepeat.Candidates) &&
    tiledLong.Routes.Zip(tiledLongRepeat.Routes, (a, b) =>
        a.Score.Equals(b.Score) &&
        a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x),
    "v1.0 long-run duplicate-window resolution stays deterministic");
var englishLong = phase2.Analyze(longMixed, new BoundaryEvidence(
    ExplicitEnglish: [desiredLongSpan]));
Check(englishLong.Routes.All(r => r.EnglishSpans.Contains(desiredLongSpan)) &&
    !englishLong.AutoCommitAllowed,
    "v1.0 long-run hypotheses cannot displace an explicit English lock");

var blockedLong = phase2.Analyze(longMixed, new BoundaryEvidence(
    ExplicitJapanese: [desiredLongSpan]));
Check(blockedLong.Candidates.All(c => !c.Span.Overlaps(desiredLongSpan)) &&
    blockedLong.Routes.All(r =>
        r.EnglishSpans.All(span => !span.Overlaps(desiredLongSpan))),
    "v1.0 long-run opt-in cannot override an explicit Japanese lock");

var longRunOptionRejected = false;
try { _ = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalLongRunWindowing: true)); }
catch (ArgumentException) { longRunOptionRejected = true; }
Check(longRunOptionRejected,
    "v1.0 long-run option rejects missing unknown-Latin candidate generator");

var unicodeLong = "😀" + longMixed + "。";
var unicodeLongResult = phase2.Analyze(unicodeLong);
Check(unicodeLongResult.Raw == unicodeLong &&
    unicodeLongResult.Candidates.Any(c =>
        c.Span == new Utf16Span(desiredLongSpan.Start + 2, desiredLongSpan.End + 2)) &&
    unicodeLongResult.Candidates.All(c => c.Span.Start >= 2 && c.Span.End <= unicodeLong.Length - 1),
    "v1.0 tiling keeps absolute UTF-16 boundaries after an emoji");


// v1.0 phase 3: direct adversarial candidate-budget contract. Do not use
// fabricated gold to claim a real Japanese/English accuracy improvement.
var speculativeDense = new List<BoundaryCandidate>();
for (int i = 0; i < 160; ++i)
{
    var start = i % 20;
    var length = 5 + i / 20;
    speculativeDense.Add(new BoundaryCandidate(
        new Utf16Span(start, start + length),
        MeltiaDiverseCandidateSelector.UnknownOrigin, 10.0 - i * 0.01));
}
for (int i = 0; i < 16; ++i)
    speculativeDense.Add(new BoundaryCandidate(
        new Utf16Span(216 + i, 221 + i),
        MeltiaDiverseCandidateSelector.UnknownOrigin, 0.01 - i * 0.0001));
var prioritized = new BoundaryCandidate(
    new Utf16Span(320, 328), "host-lexicon-advisory", 5.0);
speculativeDense.Add(prioritized);
var baselineDense = speculativeDense
    .OrderBy(c => c.Origin == MeltiaDiverseCandidateSelector.UnknownOrigin ? 1 : 0)
    .ThenByDescending(c => c.Score)
    .ThenBy(c => c.Span.Start).ThenBy(c => c.Span.End)
    .Take(MeltiaBoundaryEngine.MaxCandidates).ToArray();
var selectedDense = MeltiaDiverseCandidateSelector.Select(
    speculativeDense, MeltiaBoundaryEngine.MaxCandidates).ToArray();
Check(baselineDense.Length == 128 && selectedDense.Length == 128 &&
    selectedDense.Contains(prioritized) &&
    !baselineDense.Any(c => c.Span.Start >= 216 && c.Span.Start < 240) &&
    selectedDense.Any(c => c.Span.Start >= 216 && c.Span.Start < 240) &&
    selectedDense.Select(c => c.Span).Distinct().Count() == selectedDense.Length,
    "v1.0 phase3 preserves lexicon and rescues a distant candidate region under overload");
Check(MeltiaDiverseCandidateSelector.Select(
        speculativeDense, MeltiaBoundaryEngine.MaxCandidates)
        .SequenceEqual(selectedDense) &&
    MeltiaDiverseCandidateSelector.Select(
        speculativeDense.AsEnumerable().Reverse(),
        MeltiaBoundaryEngine.MaxCandidates)
        .SequenceEqual(selectedDense),
    "v1.0 phase3 budget is deterministic and insertion-order independent");

var withoutCap = speculativeDense.Take(15).ToArray();
var expectedWithoutCap = withoutCap
    .OrderBy(c => c.Origin == MeltiaDiverseCandidateSelector.UnknownOrigin ? 1 : 0)
    .ThenByDescending(c => c.Score)
    .ThenBy(c => c.Span.Start).ThenBy(c => c.Span.End).ToArray();
Check(MeltiaDiverseCandidateSelector.Select(withoutCap, 128)
        .SequenceEqual(expectedWithoutCap) &&
    MeltiaDiverseCandidateSelector.Select(withoutCap, 0).Count == 0,
    "v1.0 phase3 reuses baseline sorting when cap is not reached");

var phase3 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true,
    ExperimentalLongRunWindowing: true,
    ExperimentalDiverseCandidatePruning: true));
var phase3Parity = true;
foreach (var rawSample in new[] {
    "kyouhaphotonicswotukau", "tokyo", "ime", "nihongodearigatou",
    "kyouhahttps://example.org/docs", "watashihaGPUwotukau"
})
{
    var old = phase2.Analyze(rawSample);
    var next = phase3.Analyze(rawSample);
    phase3Parity &= old.Candidates.SequenceEqual(next.Candidates) &&
        old.Routes.Count == next.Routes.Count &&
        old.Routes.Zip(next.Routes, (a, b) =>
            a.Score.Equals(b.Score) &&
            a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x);
}
Check(phase3Parity, "v1.0 phase3 does not change non-truncated short input");
var phase3Japanese = phase3.Analyze(longRomaji);
Check(phase3Japanese.Candidates.Count == 0 &&
    phase3Japanese.Routes[0].EnglishSpans.Count == 0 &&
    !phase3Japanese.AutoCommitAllowed,
    "v1.0 phase3 preserves long Japanese-only negative control");
var phase3Long = phase3.Analyze(longMixed);
Check(phase3Long.Raw == longMixed &&
    phase3Long.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates &&
    phase3Long.Routes.Count <= MeltiaBoundaryEngine.MaxRoutes &&
    !phase3Long.AutoCommitAllowed,
    "v1.0 phase3 keeps UTF-16 raw, candidate limit and no auto-commit");
var phase3EnglishLocked = phase3.Analyze(longMixed, new BoundaryEvidence(
    ExplicitEnglish: [desiredLongSpan]));
Check(phase3EnglishLocked.Routes.All(r =>
        r.EnglishSpans.Contains(desiredLongSpan)) &&
    phase3EnglishLocked.Candidates.Any(c =>
        c.Span == desiredLongSpan && c.Mandatory) &&
    !phase3EnglishLocked.AutoCommitAllowed,
    "v1.0 phase3 never displaces a mandatory English range");
var phase3JapaneseLocked = phase3.Analyze(longMixed, new BoundaryEvidence(
    ExplicitJapanese: [desiredLongSpan]));
Check(phase3JapaneseLocked.Candidates.All(c =>
        !c.Span.Overlaps(desiredLongSpan)) &&
    phase3JapaneseLocked.Routes.All(r =>
        r.EnglishSpans.All(span => !span.Overlaps(desiredLongSpan))),
    "v1.0 phase3 preserves a mandatory Japanese range");
var phase3Protected = phase3.Analyze(
    string.Concat(Enumerable.Repeat("nihongo", 15)) + "https://example.org/docs");
var expectedUrl = new Utf16Span(105, 129);
Check(phase3Protected.Candidates.Any(c =>
        c.Span == expectedUrl && c.Mandatory) &&
    phase3Protected.Routes.All(r => r.EnglishSpans.Contains(expectedUrl)),
    "v1.0 phase3 preserves protected URL at the end of a long ASCII run");
var rejectedPhase3 = false;
try { _ = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalDiverseCandidatePruning: true)); }
catch (ArgumentException) { rejectedPhase3 = true; }
Check(rejectedPhase3, "v1.0 phase3 rejects diversity without speculative generator");

Console.WriteLine($"ALL PASS {passed}");
