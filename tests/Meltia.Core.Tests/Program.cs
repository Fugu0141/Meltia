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

// H10 independent candidate generation. This deliberately tests whether the
// *candidate exists*, not whether English user intent is already inferred.
// Untyped Japanese remains untouched, and old mode remains byte-for-byte stable.
var h10 = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalFragmentationPenalty: true,
    ExperimentalUnknownLatinCandidates: true));
var h10DefaultPenalty = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true));
Check(h10DefaultPenalty.Analyze("kinouhastrategywakaru").Routes[0].EnglishSpans.Count == 0,
    "H10 enables cautious fragment penalty even when caller did not request it");
Check(h10DefaultPenalty.Analyze("kinouhastrategywakaru").Routes[0].EnglishSpans
        .SequenceEqual(h10.Analyze("kinouhastrategywakaru").Routes[0].EnglishSpans),
    "H10 implicit safety cost matches explicit candidate mode");
bool Proposed(BoundaryResult result, int start, int end) =>
    result.Candidates.Any(x =>
        x.Span == new Utf16Span(start, end) &&
        x.Origin == "h10-unknown-latin-provisional");
Check(Proposed(h10.Analyze("kinouhastrategywakaru"), 7, 15),
    "H10 exposes unknown strategy inside Japanese flanks");
var h10Two = h10.Analyze("kinouhastrategynorequirementtoiu");
Check(Proposed(h10Two, 7, 15) && Proposed(h10Two, 17, 28),
    "H10 retains two independent unknown English islands");
Check(Proposed(h10.Analyze("korekaraknowledge.wakaru"), 8, 17),
    "H10 stops the unknown English span before a punctuation mark");
Check(!h10.Analyze("dokoniakerusonomamakyouno").Candidates
    .Any(x => x.Origin == "h10-unknown-latin-provisional"),
    "H10 never splits completely phonetic Japanese sample");
Check(!h10.Analyze("nihongo").Candidates
    .Any(x => x.Origin == "h10-unknown-latin-provisional"),
    "H10 leaves simple Japanese phonetic sequence unchanged");
Check(!engine.Analyze("kinouhastrategywakaru").Candidates
    .Any(x => x.Origin == "h10-unknown-latin-provisional"),
    "H10 remains disabled by default");
var h10Ambiguous = h10.Analyze("tokyo");
Check(!h10Ambiguous.Routes[0].EnglishSpans.Any() &&
      !h10Ambiguous.Candidates.Any(x => x.Origin == "h10-unknown-latin-provisional"),
    "H10 does not hallucinate an English label for ambiguous Tokyo");
Check(!h10.Analyze("kinouhastrategywakaru").AutoCommitAllowed,
    "H10 hypotheses can never auto-commit to an application");
var h10Repeat = h10.Analyze("kinouhastrategynorequirementtoiu");
Check(h10Repeat.Candidates.SequenceEqual(h10Two.Candidates) &&
      h10Repeat.Routes.Count == h10Two.Routes.Count &&
      h10Repeat.Routes.Zip(h10Two.Routes,
          (a, b) => a.Score.Equals(b.Score) &&
                    a.EnglishSpans.SequenceEqual(b.EnglishSpans)).All(x => x),
    "H10 candidate lattice and Top-K ordering are deterministic");
Check(h10.Analyze(new string('b', 1000)).Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates,
    "H10 skips enormous ASCII runs within explicit complexity bound");
var hostEvidence = new BoundaryEvidence(
    MeltypeEnglish: [new Utf16Span(7, 15)],
    EnglishWords: ["strategy", "as", "no"]);
var priorResearch = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalFragmentationPenalty: true)).Analyze(
        "kinouhastrategywakaru", hostEvidence);
var h10Research = h10.Analyze("kinouhastrategywakaru", hostEvidence);
Check(priorResearch.Candidates.All(c => h10Research.Candidates.Contains(c)),
    "H10 candidate generation preserves host/lexicon spans and scores");
Check(h10Research.Candidates.Any(c => c.Span == new Utf16Span(7, 15) &&
        c.Origin != "h10-unknown-latin-provisional"),
    "H10 never overwrites the origin of an existing reference candidate");
Check(h10Research.Candidates.Count <= MeltiaBoundaryEngine.MaxCandidates &&
      h10Research.Routes.Count <= MeltiaBoundaryEngine.MaxRoutes,
    "H10 preserves the global candidate and route caps");

Console.WriteLine($"ALL PASS {passed}");
