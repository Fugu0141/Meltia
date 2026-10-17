// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// Experimental v0.8/v0.9 backwards-search challenger: never wired to
/// MeltiaBoundaryEngine or the active Windows input path. The original
/// MeltiaPhoneticLatticeDecoder.Decode remains the compatibility oracle.
/// </summary>
internal static class MeltiaPhoneticBackwardComparison
{
    // Frozen independently for oracle comparison; identical to v0.8 constants.
    private const int MaxJapaneseEdgeLength = 8;
    private const double UnreadableAsciiCost = 3.5;
    private const double ReadableJapaneseCharReward = 0.25;

    private sealed class Offers(Func<int, IReadOnlyList<BoundaryRoute>[],
        IReadOnlyList<BoundaryRoute>> generator) : IBackwardOfferProvider<BoundaryRoute>
    {
        public IReadOnlyList<BoundaryRoute> OffersAt(int position,
            IReadOnlyList<BoundaryRoute>[] suffixes) => generator(position, suffixes);
    }

    private sealed class Retention(Func<int, IReadOnlyList<BoundaryRoute>,
        IReadOnlyList<BoundaryRoute>> selector) : IBackwardRouteRetention<BoundaryRoute>
    {
        public IReadOnlyList<BoundaryRoute> Select(int position,
            IReadOnlyList<BoundaryRoute> offered) => selector(position, offered);
    }

    internal static IReadOnlyList<BoundaryRoute> Decode(
        string raw,
        IReadOnlyList<BoundaryCandidate> candidates,
        IReadOnlyList<Utf16Span> mandatory,
        IReadOnlyList<Utf16Span> explicitJapanese,
        double englishSpanCost,
        int maxRoutes,
        bool contextualIslandScoring = false,
        bool experimentalJapaneseFlankScoring = false)
    {
        var starts = candidates.GroupBy(x => x.Span.Start)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var required = mandatory.ToDictionary(x => x.Start, x => x);
        // Compute the scoring-only adjustment once per candidate instead of
        // repeating the romaji DP for every expanded route.
        var flankAdjustments = experimentalJapaneseFlankScoring
            ? candidates.ToDictionary(x => x.Span,
                x => MeltiaBoundaryFlankScoring.Adjustment(raw, x))
            : null;
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
                    raw.AsSpan(i, length));
            }
        }


        var dp = BackwardLatticeDecoder.Decode(raw.Length,
            new BoundaryRoute[] { new([], 0) },
            new Offers((i, dp) =>
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
                        // Only a complete pair of islands with a short
                        // Japanese connector gets the v0.9 corroboration.
                        // v0.8 keeps its exact scoring when the switch is off.
                        var bridgeBonus = contextualIslandScoring &&
                            tail.EnglishSpans.Count > 0
                            ? MeltiaContextualIslandScoring.Between(
                                raw, edge.Span, tail.EnglishSpans[0], candidates) : 0;
                        offers.Add(new BoundaryRoute(spans,
                            edge.Score + tail.Score - englishSpanCost +
                            bridgeBonus +
                            (flankAdjustments is null ? 0 : flankAdjustments[edge.Span])));
                    }
                }
            return offers;
        }),
            new Retention((i, retainedOffers) =>
        {
            var offers = retainedOffers;
            // A pure score-only top-eight beam drops almost every two/three
            // English-island hypothesis in favor of small variations of a
            // single island. Keep one representative for each span-count
            // bucket before filling the remaining beam by overall score.
            var sorted = offers.OrderByDescending(x => x.Score)
                .ThenBy(x => string.Join(";", x.EnglishSpans.Select(
                        s => $"{s.Start:D4}-{s.End:D4}")), StringComparer.Ordinal)
                .DistinctBy(x => string.Join(";", x.EnglishSpans.Select(
                        s => $"{s.Start}-{s.End}"))).ToList();
            var selected = new List<BoundaryRoute>(maxRoutes);
            foreach (var group in sorted.GroupBy(r =>
                Math.Min(r.EnglishSpans.Count, 4)))
            {
                selected.Add(group.First());
                if (selected.Count == maxRoutes) break;
            }
            foreach (var offer in sorted)
            {
                if (selected.Count == maxRoutes) break;
                if (!selected.Contains(offer)) selected.Add(offer);
            }
            return selected.OrderByDescending(x => x.Score)
                .ThenBy(x => string.Join(";", x.EnglishSpans.Select(
                    s => $"{s.Start:D4}-{s.End:D4}")), StringComparer.Ordinal)
                .ToList();
        }));
        if (dp[0].Count == 0)
            throw new ArgumentException("No path satisfies mandatory ranges");
        return dp[0];
    }
}
