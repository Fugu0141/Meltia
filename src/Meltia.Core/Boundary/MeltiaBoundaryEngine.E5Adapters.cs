// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

public sealed partial class MeltiaBoundaryEngine
{
    // Comparison-only pathway: the default Analyze always calls the original
    // BuildRoutes, and the Preview/host do not use this method.
    internal BoundaryResult AnalyzeWithSharedBackward(string raw,
        BoundaryEvidence? evidence = null) =>
        AnalyzeCore(raw, evidence, useSharedBackward: true);

    private sealed class LegacyBackwardOffers(
        IReadOnlyList<BoundaryCandidate> candidates,
        IReadOnlyList<Utf16Span> mandatory, double perSpanCost)
        : IBackwardOfferProvider<BoundaryRoute>
    {
        private readonly Dictionary<int, BoundaryCandidate[]> byStart =
            candidates.GroupBy(x => x.Span.Start)
                .ToDictionary(g => g.Key, g => g.ToArray());
        private readonly Dictionary<int, Utf16Span> requiredStart =
            mandatory.ToDictionary(s => s.Start, s => s);

        public IReadOnlyList<BoundaryRoute> OffersAt(int index,
            IReadOnlyList<BoundaryRoute>[] dp)
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
                        // Exact original BuildRoutes score and span construction.
                        var path = new[] { edge.Span }.Concat(tail.EnglishSpans).ToArray();
                        offers.Add(new BoundaryRoute(path, edge.Score + tail.Score - perSpanCost));
                    }
                }
            return offers;
        }
    }

    private sealed class LegacyBackwardRetention : IBackwardRouteRetention<BoundaryRoute>
    {
        public IReadOnlyList<BoundaryRoute> Select(int index,
            IReadOnlyList<BoundaryRoute> offers) =>
            offers.OrderByDescending(x => x.Score)
                .ThenBy(x => string.Join(";", x.EnglishSpans.Select(
                    s => $"{s.Start:D4}-{s.End:D4}")), StringComparer.Ordinal)
                .DistinctBy(x => string.Join(";", x.EnglishSpans.Select(
                    s => $"{s.Start}-{s.End}")))
                .Take(MaxRoutes).ToList();
    }

    private static class LegacyBackwardComparison
    {
        internal static IReadOnlyList<BoundaryRoute> Decode(int length,
            IReadOnlyList<BoundaryCandidate> candidates,
            IReadOnlyList<Utf16Span> mandatory, double perSpanCost)
        {
            var dp = BackwardLatticeDecoder.Decode(length,
                new BoundaryRoute[] { new([], 0) },
                new LegacyBackwardOffers(candidates, mandatory, perSpanCost),
                new LegacyBackwardRetention());
            if (dp[0].Count == 0)
                throw new ArgumentException("No path satisfies the mandatory ranges");
            return dp[0];
        }
    }
}
