// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// Rejected Phase 3 D2 research candidate (tests only, NOT linked into the
/// production H11 decoder). Preserves List.Sort semantics whenever a tie can
/// affect retained nodes; isolated speedup did not hold in full Analyze.
/// </summary>
internal static class H11BeamSelection
{
    // The H11 pruning trigger is strictly 2 * width + 1 (33 entries).
    // A fixed small stack buffer avoids heap allocations and unbounded stack use.
    internal static bool TryPruneUnique(List<H11BoundaryPrototype.Node> nodes, int width)
    {
        int count = nodes.Count;
        if (width < 1 || count != 2 * width + 1 || count > 33)
            return false;

        Span<int> selected = stackalloc int[33];
        selected = selected[..count];
        for (int i = 0; i < count; i++) selected[i] = i;

        // Descending Quickselect by double.CompareTo, NOT ordinary < / >:
        // the legacy sorter places NaNs and signed zero according to CompareTo.
        int lo = 0, hi = count - 1;
        int rank = width - 1;
        while (lo < hi)
        {
            double pivot = nodes[selected[lo + (hi - lo) / 2]].Score;
            int better = lo, current = lo, worse = hi;
            while (current <= worse)
            {
                int compare = nodes[selected[current]].Score.CompareTo(pivot);
                if (compare > 0)
                {
                    Swap(selected, better++, current++);
                }
                else if (compare < 0)
                {
                    Swap(selected, current, worse--);
                }
                else current++;
            }
            if (rank < better) hi = better - 1;
            else if (rank > worse) lo = worse + 1;
            else break;
        }

        // Sort only the selected top 'width' entries, without touching nodes.
        for (int i = 1; i < width; i++)
        {
            int item = selected[i];
            int j = i - 1;
            while (j >= 0 &&
                nodes[item].Score.CompareTo(nodes[selected[j]].Score) > 0)
            {
                selected[j + 1] = selected[j];
                j--;
            }
            selected[j + 1] = item;
        }

        // List<T>.Sort is not stable with equal scores; only use this faster
        // route if the entire retained order and the cutoff are unambiguous.
        for (int i = 1; i < width; i++)
            if (nodes[selected[i - 1]].Score.CompareTo(
                    nodes[selected[i]].Score) == 0)
                return false;
        double cutoff = nodes[selected[width - 1]].Score;
        for (int i = width; i < count; i++)
            if (nodes[selected[i]].Score.CompareTo(cutoff) == 0)
                return false;

        // Reorder references in-place. The two stack arrays track the swap
        // permutation; no Node is recreated and backpointers remain alive.
        Span<int> originalAtPosition = stackalloc int[33];
        Span<int> positionOfOriginal = stackalloc int[33];
        for (int i = 0; i < count; i++)
        {
            originalAtPosition[i] = i;
            positionOfOriginal[i] = i;
        }
        for (int i = 0; i < width; i++)
        {
            int original = selected[i];
            int position = positionOfOriginal[original];
            if (position == i) continue;
            var temporarilyDisplaced = nodes[i];
            nodes[i] = nodes[position];
            nodes[position] = temporarilyDisplaced;

            int displacedOriginal = originalAtPosition[i];
            originalAtPosition[i] = original;
            originalAtPosition[position] = displacedOriginal;
            positionOfOriginal[original] = i;
            positionOfOriginal[displacedOriginal] = position;
        }
        nodes.RemoveRange(width, count - width);
        return true;
    }

    private static void Swap(Span<int> indices, int a, int b)
    {
        int temp = indices[a];
        indices[a] = indices[b];
        indices[b] = temp;
    }
}
