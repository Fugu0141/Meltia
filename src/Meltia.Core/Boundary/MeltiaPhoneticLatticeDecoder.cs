// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltia.Core.Boundary;

/// <summary>
/// v0.8 experimental decoder: a bounded lattice whose Japanese paths must
/// follow romaji-readable substrings instead of skipping arbitrary ASCII.
/// Unknown letters are still possible at a substantial explicit cost.
/// No UI, conversion, learning, mutation, or auto-commit occurs here.
/// </summary>
internal static class MeltiaPhoneticLatticeDecoder
{
    private const int MaxJapaneseEdgeLength = 8;
    private const double UnreadableAsciiCost = 3.5;
    // Japanese syllables left intact are positive evidence, not merely free.
    // A small per-character reward disfavors an English edge that swallows
    // the beginning of an otherwise phonetic Japanese suffix.
    private const double ReadableJapaneseCharReward = 0.25;

    public static IReadOnlyList<BoundaryRoute> Decode(
        string raw,
        IReadOnlyList<BoundaryCandidate> candidates,
        IReadOnlyList<Utf16Span> mandatory,
        IReadOnlyList<Utf16Span> explicitJapanese,
        double englishSpanCost,
        int maxRoutes)
    {
        var starts = candidates.GroupBy(x => x.Span.Start)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var required = mandatory.ToDictionary(x => x.Start, x => x);
        var dp = new List<BoundaryRoute>[raw.Length + 1];
        dp[raw.Length] = [new BoundaryRoute([], 0)];
        // Mandatory ranges must never be consumed by a phonetic transition.
        var blocked = new bool[raw.Length];
        foreach (var span in mandatory)
            for (int i = span.Start; i < span.End; i++) blocked[i] = true;
        var exempt = new bool[raw.Length];
        foreach (var span in explicitJapanese)
            for (int i = span.Start; i < span.End; i++) exempt[i] = true;

        // Cache plausibility: the same start and length may otherwise be
        // visited repeatedly in an input with many English candidates.
        var readable = new bool[raw.Length, MaxJapaneseEdgeLength + 1];
        for (int i = 0; i < raw.Length; i++)
        {
            if (blocked[i] || !char.IsAsciiLetter(raw[i])) continue;
            for (int length = 1; length <= MaxJapaneseEdgeLength &&
                i + length <= raw.Length && !blocked[i + length - 1]; length++)
            {
                if (!char.IsAsciiLetter(raw[i + length - 1])) break;
                readable[i, length] = MeltiaRomaji.CanRead(
                    raw.Substring(i, length));
            }
        }

        for (int i = raw.Length - 1; i >= 0; i--)
        {
            var offers = new List<BoundaryRoute>();
            var forced = required.TryGetValue(i, out var exact);
            if (!blocked[i])
            {
                // Each Japanese transition preserves text but not an English
                // span, and costs zero. Unicode punctuation and paired UTF-16
                // surrogate code units are passed through as a single unit.
                if (!char.IsAsciiLetter(raw[i]))
                {
                    var step = i + 1 < raw.Length &&
                        char.IsHighSurrogate(raw[i]) &&
                        char.IsLowSurrogate(raw[i + 1]) ? 2 : 1;
                    if (i + step <= raw.Length && !blocked[i + step - 1])
                        offers.AddRange(dp[i + step]);
                }
                else
                {
                    for (int length = 1;
                        length <= MaxJapaneseEdgeLength && i + length <= raw.Length;
                        length++)
                    {
                        if (!readable[i, length]) continue;
                        foreach (var tail in dp[i + length])
                            offers.Add(new BoundaryRoute(tail.EnglishSpans,
                                tail.Score + length * ReadableJapaneseCharReward));
                    }
                    // A mistyped Japanese syllable or as-yet-unrecognized
                    // Latin letter remains representable; never throw or
                    // lose the literal raw string.
                    foreach (var tail in dp[i + 1])
                        offers.Add(new BoundaryRoute(tail.EnglishSpans,
                            tail.Score - (exempt[i] ? 0 : UnreadableAsciiCost)));
                }
            }
            if (starts.TryGetValue(i, out var local))
                foreach (var edge in local)
                {
                    if (forced && edge.Span != exact) continue;
                    foreach (var tail in dp[edge.Span.End])
                    {
                        var spans = new[] { edge.Span }.Concat(tail.EnglishSpans).ToArray();
                        offers.Add(new BoundaryRoute(spans,
                            edge.Score + tail.Score - englishSpanCost));
                    }
                }
            dp[i] = offers.OrderByDescending(x => x.Score)
                .ThenBy(x => string.Join(";", x.EnglishSpans.Select(
                        s => $"{s.Start:D4}-{s.End:D4}")), StringComparer.Ordinal)
                .DistinctBy(x => string.Join(";", x.EnglishSpans.Select(
                        s => $"{s.Start}-{s.End}")))
                .Take(maxRoutes).ToList();
        }
        if (dp[0].Count == 0)
            throw new ArgumentException("No path satisfies mandatory ranges");
        return dp[0];
    }
}
