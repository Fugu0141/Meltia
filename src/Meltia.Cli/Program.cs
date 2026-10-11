// SPDX-License-Identifier: GPL-3.0-or-later
// Research-only CLI. No host key injection, TSF registration or auto-commit.
using System.Text.Json;
using Meltia.Core.Boundary;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("Usage: dotnet run --project src/Meltia.Cli -- <raw> [--word=delegate] [--latin=0:5] [--japanese=0:5]");
    return;
}
var raw = args[0];
var lexicon = args.Where(x => x.StartsWith("--word=", StringComparison.Ordinal))
    .Select(x => x["--word=".Length..]).ToArray();
var english = ParseSpans("--latin=");
var japanese = ParseSpans("--japanese=");
var engine = new MeltiaBoundaryEngine();
var result = engine.Analyze(raw,
    new BoundaryEvidence(EnglishWords: lexicon, ExplicitEnglish: english, ExplicitJapanese: japanese));
var payload = new
{
    raw = result.Raw,
    candidates = result.Candidates.Select(c => new
    {
        startUtf16 = c.Span.Start, endUtf16 = c.Span.End,
        c.Origin, c.Score, c.Mandatory
    }),
    routes = result.Routes.Select(r => new
    {
        r.Score,
        englishSpans = r.EnglishSpans.Select(s => new { startUtf16 = s.Start, endUtf16 = s.End })
    }),
    result.Truncated, result.AutoCommitAllowed
};
Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

IReadOnlyList<Utf16Span> ParseSpans(string key)
{
    var result = new List<Utf16Span>();
    foreach (var value in args.Where(x => x.StartsWith(key, StringComparison.Ordinal)))
    {
        var fields = value[key.Length..].Split(':');
        if (fields.Length != 2 || !int.TryParse(fields[0], out var a) ||
            !int.TryParse(fields[1], out var b))
            throw new ArgumentException("Invalid " + key + " start:end argument");
        result.Add(new Utf16Span(a, b));
    }
    return result;
}
