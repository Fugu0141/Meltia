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
Console.WriteLine($"ALL PASS {passed}");
