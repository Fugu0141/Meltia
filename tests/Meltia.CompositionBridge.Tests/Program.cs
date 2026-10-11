// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.Composition;
using Meltia.CompositionBridge;
using Meltia.Core.Boundary;

int passed = 0;
void Check(bool condition, string title)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + title);
    Console.WriteLine("PASS " + title);
    ++passed;
}
void Reject(Action action, string title)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, title);
}
var engine = new MeltiaBoundaryEngine();
var japanese = MeltypeBoundaryShadowBridge.CompareSegments(
    "nihongo", [new CompositionSegment(false, "にほんご", "nihongo")],
    engine, englishWords: ["go"]);
Check(japanese.Meltia.Raw == "nihongo", "raw input preserved");
Check(japanese.Meltia.Routes[0].EnglishSpans.Count == 0,
    "partial dictionary hit does not cut Japanese");
Check(japanese.Meltia.Candidates.Any(x => x.Span == new Utf16Span(5, 7)),
    "ambiguous English remains in candidate inventory");
Check(!japanese.AutoCommitAllowed && !japanese.Meltia.AutoCommitAllowed,
    "no text commit possible from shadow API");
var two = MeltypeBoundaryShadowBridge.CompareSegments(
    "kyouhaKingGnudeSPYAIRde",
    [
        new CompositionSegment(false, "きょうは", "kyouha"),
        new CompositionSegment(true, "", "KingGnu"),
        new CompositionSegment(false, "で", "de"),
        new CompositionSegment(true, "", "SPYAIR"),
        new CompositionSegment(false, "で", "de")
    ], engine);
Check(two.MeltypeEnglish.SequenceEqual([new Utf16Span(6, 13), new Utf16Span(15, 21)]),
    "actual CompositionSegment to UTF-16 coordinates");
Check(two.Meltia.Candidates.Any(x => x.Span == new Utf16Span(6, 13)) &&
      two.Meltia.Candidates.Any(x => x.Span == new Utf16Span(15, 21)),
    "both artist name edges forwarded");
Check(two.Meltia.Routes.Count <= 8 && two.Meltia.Candidates.Count <= 128,
    "candidate and Top-K safety caps");

var ambiguous = MeltypeBoundaryShadowBridge.CompareSegments(
    "tokyo", [new CompositionSegment(true, "", "tokyo")],
    engine, englishWords: ["tokyo"]);
Check(!ambiguous.Meltia.Routes[0].EnglishSpans.Any(),
    "Meltype advisory is not an English lock");
Check(!ambiguous.TopRouteMatchesMeltype, "diagnostic mismatch is explicitly reported");
var locked = MeltypeBoundaryShadowBridge.CompareSegments(
    "tokyo", [new CompositionSegment(false, "ときょ", "tokyo")],
    engine, explicitEnglish: [new Utf16Span(0, 5)]);
Check(locked.Meltia.Routes[0].EnglishSpans.SequenceEqual([new Utf16Span(0, 5)]),
    "explicit user Latin lock overrides uncertainty");
var lockJapanese = MeltypeBoundaryShadowBridge.CompareSegments(
    "tokyo", [new CompositionSegment(true, "", "tokyo")],
    engine, englishWords: ["tokyo"], explicitJapanese: [new Utf16Span(0, 5)]);
Check(!lockJapanese.Meltia.Routes[0].EnglishSpans.Any(),
    "explicit Japanese lock overrides host advisory");

var unicode = MeltypeBoundaryShadowBridge.FromSegments(
    "😀tokyo",
    [new CompositionSegment(false, "😀", "😀"), new CompositionSegment(true, "", "tokyo")]);
Check(unicode.MeltypeEnglish!.SequenceEqual([new Utf16Span(2, 7)]),
    "emoji offsets account for two UTF-16 code units");
Reject(() => MeltypeBoundaryShadowBridge.FromSegments("😀tokyo", [
    new CompositionSegment(false, "", "\uD83D"),
    new CompositionSegment(false, "", "\uDE00tokyo")
]), "reject segmentation in the middle of a surrogate pair");
Reject(() => MeltypeBoundaryShadowBridge.FromSegments("nihongo", [
    new CompositionSegment(false, "", "nihon"), new CompositionSegment(false, "", "ga")
]), "reject mismatched host segmentation");
Reject(() => MeltypeBoundaryShadowBridge.FromSegments("nihongo", [
    new CompositionSegment(false, "", "ni")
]), "reject incomplete host segmentation");

var protectedUrl = MeltypeBoundaryShadowBridge.CompareSegments(
    "https://example.com",
    [new CompositionSegment(true, "", "https://example.com") { IsProtected = true }],
    engine);
Check(protectedUrl.Meltia.Routes[0].EnglishSpans.SequenceEqual([new Utf16Span(0, 19)]),
    "protected URL remains indivisible");
Reject(() => MeltypeBoundaryShadowBridge.CompareSegments(
    "tokyo", [new CompositionSegment(true, "", "tokyo") { IsProtected = true }],
    engine, explicitJapanese: [new Utf16Span(0, 5)]),
    "conflicting user Japanese lock and protected span rejected");

// Real Meltype CompositionDetector is linked and invoked, not mocked as a
// duplicate type. This remains an offline test, not a live TSF integration.
var detector = CompositionDetector.CreateDefault();
var liveUnits = new CompositionUnit[] {
    new("と", "to"), new("きょ", "kyo")
};
var fromDetector = MeltypeBoundaryShadowBridge.CompareDetector(
    detector, liveUnits, "", engine);
Check(fromDetector.Meltia.Raw == "tokyo" && !fromDetector.AutoCommitAllowed,
    "real Meltype detector -> Meltia shadow adapter");

Console.WriteLine($"ALL PASS {passed}");
