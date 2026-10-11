// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Meltia.Core.Boundary;

/// <summary>Half-open raw UTF-16 span. Neither coordinate may split a surrogate pair.</summary>
public readonly record struct Utf16Span(int Start, int End)
{
    public int Length => End - Start;
    public bool Overlaps(Utf16Span other) => Start < other.End && other.Start < End;
}

public sealed record BoundaryCandidate(Utf16Span Span, string Origin, double Score, bool Mandatory = false);
public sealed record BoundaryRoute(IReadOnlyList<Utf16Span> EnglishSpans, double Score);
public sealed record BoundaryResult(
    string Raw, IReadOnlyList<BoundaryCandidate> Candidates,
    IReadOnlyList<BoundaryRoute> Routes, bool Truncated)
{
    // v0.5 is an advisory engine. It must never commit to the user's application.
    public bool AutoCommitAllowed => false;
}
public sealed record BoundaryEvidence(
    IReadOnlyList<string>? EnglishWords = null,
    IReadOnlyList<string>? ProperNames = null,
    IReadOnlyList<Utf16Span>? MeltypeEnglish = null,
    IReadOnlyList<Utf16Span>? ExplicitEnglish = null,
    IReadOnlyList<Utf16Span>? ExplicitJapanese = null,
    IReadOnlyList<Utf16Span>? Protected = null);

// Explicit opt-in for offline experiments. Default remains the v0.5 behavior.
// This is NOT wired to the active Meltype TSF/composition host.
public sealed record MeltiaBoundaryOptions(
    bool ExperimentalStrongHostAdvisory = false,
    bool ExperimentalFragmentationPenalty = false,
    bool ExperimentalUnknownLatinCandidates = false);

/// <summary>
/// v0.5 parallel boundary engine. No changes to Meltype composition/TSF or commit behavior.
/// Keeps ambiguous lexical matches as candidates without allowing them to fragment
/// a fully phonetic Japanese island. Top-K routes are provisional and reversible.
/// </summary>
public sealed class MeltiaBoundaryEngine
{
    public const int MaxInputLength = 1024;
    public const int MaxCandidates = 128;
    public const int MaxRoutes = 8;
    private readonly MeltiaBoundaryOptions _options;

    public MeltiaBoundaryEngine(MeltiaBoundaryOptions? options = null) =>
        _options = options ?? new MeltiaBoundaryOptions();

    // Short uppercase text can be valid Japanese romaji (e.g. KURU).
    // Only these conventional technical acronyms override that ambiguity
    // without an explicit language lock; extend via dictionaries in future.
    private static readonly HashSet<string> CommonTechnicalAcronyms =
        new(StringComparer.Ordinal)
        {
            "AI", "API", "BIOS", "CLI", "CPU", "CSS", "DNS", "GPU",
            "HTML", "HTTP", "IDE", "IME", "JS", "JSON", "LAN",
            "OS", "RAM", "SDK", "SQL", "SSD", "TSF", "UI",
            "URL", "USB", "UTF", "XML"
        };

    private static readonly string[] Particles = ["shimasu", "shita", "sareta", "suru",
        "desu", "kara", "made", "noni", "noto", "de", "ga", "no", "wo", "ni", "ha", "to"];
    private static readonly Regex AsciiRuns = new("[A-Za-z]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProtectedUrl = new(@"https?://[^\s]+", RegexOptions.Compiled);
    private static readonly Regex ProtectedEmail = new(@"(?<![\w.@])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    public BoundaryResult Analyze(string raw, BoundaryEvidence? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > MaxInputLength) throw new ArgumentOutOfRangeException(nameof(raw));
        evidence ??= new BoundaryEvidence();
        var japanese = (evidence.ExplicitJapanese ?? []).ToArray();
        // Identical explicit/protected locks are idempotent. Overlapping distinct locks
        // are still rejected below rather than silently resolved.
        var hard = (evidence.ExplicitEnglish ?? []).Concat(evidence.Protected ?? [])
            .Distinct().ToList();
        foreach (var expression in new[] { ProtectedUrl, ProtectedEmail })
            foreach (Match match in expression.Matches(raw))
            {
                var candidate = new Utf16Span(match.Index, match.Index + match.Length);
                // A URL can contain an email. The longer protecting span wins.
                if (!hard.Any(x => x.Overlaps(candidate))) hard.Add(candidate);
            }
        foreach (var span in japanese.Concat(hard).Concat(evidence.MeltypeEnglish ?? []))
            CheckSpan(raw, span);
        foreach (var span in hard)
        {
            if (japanese.Any(span.Overlaps) || hard.Any(x => x != span && span.Overlaps(x)))
                throw new ArgumentException("Overlapping or contradictory mandatory ranges");
        }

        var bySpan = new Dictionary<Utf16Span, BoundaryCandidate>();
        void Add(Utf16Span span, string origin, double score, bool mandatory = false)
        {
            if (span.Start < 0 || span.End > raw.Length || span.Length <= 0) return;
            if (japanese.Any(span.Overlaps)) return;
            if (hard.Any(x => x.Overlaps(span) && x != span)) return;
            var next = new BoundaryCandidate(span, origin, score, mandatory);
            if (!bySpan.TryGetValue(span, out var old) || mandatory && !old.Mandatory ||
                !old.Mandatory && score > old.Score) bySpan[span] = next;
        }
        foreach (var span in hard) Add(span, "user-or-protected", 100, true);
        foreach (var span in evidence.MeltypeEnglish ?? [])
        {
            // In the default mode, host output is ONLY a provisional candidate.
            // Optional experiment: prioritize host suggestions when the token has
            // strong orthographic evidence that it is not Japanese romaji.
            // An embedded CamelCase transition with a lowercase prefix is NOT
            // such evidence (Meltype may have swallowed a full mixed sentence).
            double advisoryScore = ExperimentalHostScore(raw, span);
            Add(span, advisoryScore > 0 ? "meltype-strong-advisory" : "meltype-advisory",
                advisoryScore);
        }

        var words = (evidence.EnglishWords ?? []).Concat(evidence.ProperNames ?? [])
            .Where(x => x is { Length: >= 2 and <= 72 } && x.All(char.IsAsciiLetter))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(4096).ToArray();
        var islands = MeltiaRomaji.Islands(raw);
        foreach (var word in words)
        {
            int index = 0;
            while (index < raw.Length)
            {
                int found = raw.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                var span = new Utf16Span(found, found + word.Length);
                index = found + 1;
                var token = raw.Substring(span.Start, span.Length);
                // A lexicon hit alone cannot force Latin inside a Japanese phonetic island.
                bool readablePart = islands.Any(i => i.Start <= span.Start && span.End <= i.End);
                bool readableToken = MeltiaRomaji.CanRead(token);
                double score = readablePart || readableToken ? -0.6 : 4 + Math.Min(3, token.Length * .15);
                Add(span, "host-lexicon-advisory", score);
            }
        }

        // Strong orthographic evidence: uppercase starts after a Japanese prefix or
        // a Japanese particle. Enumerate bounded ends BEFORE a Japanese particle.
        foreach (Match run in AsciiRuns.Matches(raw))
        {
            if (run.Length > 256) continue;
            int end = run.Index + run.Length;
            for (int start = run.Index; start < end; ++start)
            {
                if (!char.IsUpper(raw[start])) continue;
                bool boundary = start == run.Index && MeltiaRomaji.CanRead(raw[..start]) ||
                    Particles.Any(p => start >= p.Length &&
                        raw.AsSpan(start - p.Length, p.Length).Equals(p, StringComparison.OrdinalIgnoreCase));
                if (!boundary) continue;
                for (int stop = start + 3; stop <= Math.Min(end, start + 32); ++stop)
                {
                    var name = raw.Substring(start, stop - start);
                    if (MeltiaRomaji.CanRead(name)) continue;
                    bool rightBoundary = stop == end && stop == raw.Length ||
                        Particles.Any(p => stop + p.Length <= end &&
                            raw.AsSpan(stop, p.Length).Equals(p, StringComparison.OrdinalIgnoreCase) &&
                            (stop + p.Length == end ||
                             char.IsUpper(raw[stop + p.Length]) ||
                             MeltiaRomaji.CanRead(raw[(stop + p.Length)..end])));
                    if (rightBoundary) Add(new Utf16Span(start, stop), "capital-name-japanese-seam", 9.0);
                }
            }
        }

        // Lexical evidence with Japanese flanks can be useful even for lower-case
        // input. Such candidates remain provisional; purely phonetic text is protected.
        foreach (Match run in AsciiRuns.Matches(raw))
        {
            foreach (var candidate in bySpan.Values.ToArray())
            {
                var span = candidate.Span;
                if (span.Start < run.Index || span.End > run.Index + run.Length ||
                    candidate.Origin != "host-lexicon-advisory") continue;
                var left = raw.Substring(run.Index, span.Start - run.Index);
                var right = raw.Substring(span.End, run.Index + run.Length - span.End);
                if (MeltiaRomaji.CanRead(left) && MeltiaRomaji.CanRead(right) &&
                    !MeltiaRomaji.CanRead(raw.Substring(run.Index, run.Length)) &&
                    candidate.Score > 0)
                    Add(span, "lexicon-with-phonetic-flanks", 6.0);
            }
        }

        // v0.7 research mode: independent spelling/phonetic hypotheses.
        // Low-confidence by design: these help candidate recall, but are not
        // an instruction to show or commit English in the user's IME.
        if (_options.ExperimentalUnknownLatinCandidates)
            foreach (var edge in MeltiaUnknownLatinCandidates.Generate(raw))
            {
                // v0.7 adds hypotheses; it must never alter the provenance or
                // ranking of an existing lexicon / Meltype / capitalization edge.
                if (!bySpan.ContainsKey(edge.Span))
                    Add(edge.Span, edge.Origin, edge.Score);
            }

        // Mandatory spans survive pruning. All other proposals may be truncated.
        var mandatoryCandidates = bySpan.Values.Where(x => x.Mandatory)
            .OrderBy(x => x.Span.Start).ThenBy(x => x.Span.End).ToList();
        if (mandatoryCandidates.Count > MaxCandidates)
            throw new ArgumentException("Too many mandatory ranges");
        var remaining = bySpan.Values.Where(x => !x.Mandatory)
            // Preserve previously ranked host/lexicon candidates before
            // admitting speculative v0.7 edges under the bounded candidate cap.
            .OrderBy(x => x.Origin == "v0.7-unknown-latin-provisional" ? 1 : 0)
            .ThenByDescending(x => x.Score).ThenBy(x => x.Span.Start).ThenBy(x => x.Span.End)
            .Take(MaxCandidates - mandatoryCandidates.Count).ToList();
        var candidates = mandatoryCandidates.Concat(remaining)
            .OrderBy(x => x.Span.Start).ThenBy(x => x.Span.End).ToArray();
        // The original 0.3/span favors chaining many small lexical hits;
        // research mode charges roughly the base score of a short dictionary
        // word to discourage gratuitous language switches. Defaults unchanged.
        // v0.7 produces many speculative Latin candidates. Always use the
        // cautious fragmentation cost when its generator is enabled, even if
        // a caller forgot to opt in to the ranking experiment separately.
        var perSpanCost = _options.ExperimentalFragmentationPenalty ||
            _options.ExperimentalUnknownLatinCandidates ? 4.5 : 0.3;
        var routes = BuildRoutes(raw.Length, candidates, hard, perSpanCost);
        return new BoundaryResult(raw, candidates, routes, bySpan.Count > MaxCandidates);
    }

    private double ExperimentalHostScore(string raw, Utf16Span span)
    {
        const double conservative = -0.2;
        if (!_options.ExperimentalStrongHostAdvisory ||
            span.Start < 0 || span.End > raw.Length || span.Start >= span.End)
            return conservative;
        var token = raw.Substring(span.Start, span.Length);
        if (token.Length > 72 || token.Length < 2 || !token.All(char.IsAsciiLetter))
            return conservative;
        // Do not promote a whole mixed-script romanized stream just because
        // the old engine returned it as one English segment.
        if (char.IsAsciiLetterLower(token[0]) &&
            token.Any(char.IsAsciiLetterUpper))
            return conservative;
        // Explicitly typed short uppercase acronyms carry evidence that their
        // lower-case counterpart does not (e.g. IME vs ime).
        if (token.Length <= 5 && token.All(char.IsAsciiLetterUpper) &&
            CommonTechnicalAcronyms.Contains(token))
            return 5.0;
        // An unreadable Latin token is stronger evidence than a dictionary
        // substring. However it is still only a reversible preview hypothesis.
        if (token.Length >= 4 && !MeltiaRomaji.CanRead(token))
            return 5.0;
        return conservative;
    }

    private static IReadOnlyList<BoundaryRoute> BuildRoutes(
        int length, IReadOnlyList<BoundaryCandidate> candidates,
        IReadOnlyList<Utf16Span> mandatory, double perSpanCost)
    {
        var byStart = candidates.GroupBy(x => x.Span.Start)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var requiredStart = mandatory.ToDictionary(s => s.Start, s => s);
        var dp = new List<BoundaryRoute>[length + 1];
        dp[length] = [new BoundaryRoute([], 0)];
        for (int index = length - 1; index >= 0; --index)
        {
            var offers = new List<BoundaryRoute>();
            bool forced = requiredStart.TryGetValue(index, out var required);
            bool inside = mandatory.Any(x => x.Start < index && index < x.End);
            if (!forced && !inside) offers.AddRange(dp[index + 1]);
            if (byStart.TryGetValue(index, out var local))
                foreach (var edge in local)
                {
                    if (forced && edge.Span != required) continue;
                    foreach (var tail in dp[edge.Span.End])
                    {
                        // Do not impose a four-English-span cap here: mandatory locks
                        // (including protected URLs) must survive even in long input.
                        // Candidates are already bounded by MaxCandidates and the
                        // DP keeps only MaxRoutes paths per input position.
                        var path = new[] { edge.Span }.Concat(tail.EnglishSpans).ToArray();
                        offers.Add(new BoundaryRoute(path, edge.Score + tail.Score - perSpanCost));
                    }
                }
            dp[index] = offers.OrderByDescending(x => x.Score)
                .ThenBy(x => string.Join(";", x.EnglishSpans.Select(s => $"{s.Start:D4}-{s.End:D4}")),
                    StringComparer.Ordinal)
                .DistinctBy(x => string.Join(";", x.EnglishSpans.Select(s => $"{s.Start}-{s.End}")))
                .Take(MaxRoutes).ToList();
        }
        if (dp[0].Count == 0) throw new ArgumentException("No path satisfies the mandatory ranges");
        return dp[0];
    }

    private static void CheckSpan(string raw, Utf16Span span)
    {
        if (span.Start < 0 || span.Start >= span.End || span.End > raw.Length)
            throw new ArgumentException("Invalid UTF-16 range");
        static bool SplitsPair(string value, int i) =>
            i > 0 && i < value.Length && char.IsHighSurrogate(value[i - 1]) && char.IsLowSurrogate(value[i]);
        if (SplitsPair(raw, span.Start) || SplitsPair(raw, span.End))
            throw new ArgumentException("Range splits a surrogate pair");
    }
}
