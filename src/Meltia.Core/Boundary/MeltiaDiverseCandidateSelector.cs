// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// v1.0 phase 3: bounded, deterministic positional diversity for speculative
/// Latin edges when the 128-candidate budget would otherwise discard whole
/// regions. A coverage heuristic, NOT evidence that a span is English.
/// </summary>
internal static class MeltiaDiverseCandidateSelector
{
    internal const string UnknownOrigin = "v0.7-unknown-latin-provisional";
    private const int RegionWidth = 24;

    public static IReadOnlyList<BoundaryCandidate> Select(
        IEnumerable<BoundaryCandidate> nonMandatory, int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        // The same comparison as the v0.7-v1.0 default ensures that we are
        // strictly observationally equivalent when the candidate cap is not
        // hit. Explicit locks are retained by the caller before this point.
        var ranked = nonMandatory
            .OrderBy(x => x.Origin == UnknownOrigin ? 1 : 0)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Span.Start).ThenBy(x => x.Span.End)
            .ToArray();

        if (ranked.Length <= capacity)
            return ranked;
        if (capacity == 0)
            return [];

        // Trusted lexical/host evidence has the exact same priority as in
        // the baseline: never spend coverage slots ahead of these candidates.
        var stable = ranked.TakeWhile(x => x.Origin != UnknownOrigin)
            .Take(capacity).ToList();
        if (stable.Count == capacity) return stable;

        int slots = capacity - stable.Count;
        var speculative = ranked.Skip(stable.Count).ToArray();
        if (speculative.Length <= slots)
        {
            stable.AddRange(speculative);
            return stable;
        }

        // Retain the strongest half globally to bound regressions to high
        // scoring proposals. Diversify only the *remaining* speculative slots.
        int strongCount = (slots + 1) / 2;
        stable.AddRange(speculative.Take(strongCount));
        int diversitySlots = slots - strongCount;
        if (diversitySlots == 0) return stable;

        // Absolute UTF-16 24-unit start bands, not window-local coordinates.
        // Round-robin keeps proposals from distant positions alive rather than
        // filling the entire cap from a single dense prefix. Within each band
        // the inherited score/range ordering is still used.
        var regions = speculative.Skip(strongCount)
            .GroupBy(x => x.Span.Start / RegionWidth)
            .OrderBy(g => g.Key)
            .Select(g => new Queue<BoundaryCandidate>(g))
            .ToArray();
        while (diversitySlots > 0)
        {
            bool progressed = false;
            foreach (var region in regions)
            {
                if (diversitySlots == 0) break;
                if (region.Count == 0) continue;
                stable.Add(region.Dequeue());
                diversitySlots--;
                progressed = true;
            }
            if (!progressed) break;
        }

        // If the ranked set still has slots (e.g. no candidates in a region),
        // use the original total order as an unambiguous fallback.
        if (stable.Count < capacity)
        {
            var selected = stable.Select(x => x.Span).ToHashSet();
            foreach (var edge in speculative)
            {
                if (stable.Count == capacity) break;
                if (selected.Add(edge.Span)) stable.Add(edge);
            }
        }
        return stable;
    }
}
