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
Check(typeof(MeltiaBoundaryEngine).Assembly.GetName().Version is { Major: 0, Minor: 8 },
    "Meltia Core assembly development version is v0.8");
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
Check(Has(v08Multi, new Utf16Span(7, 15), new Utf16Span(17, 28)),
    "v0.8 supports two unknown English islands on one phonetic lattice");
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

Console.WriteLine($"ALL PASS {passed}");
