// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

public sealed partial class H11BoundaryPrototype
{
    // E2 experimental output projector. The legacy AnalyzeCore projection
    // stays in place as an independent compatibility oracle.
    private sealed class H11RouteProjector(TraceCollector? diagnostics)
        : IForwardRouteProjector<Node, Edge, Analysis>
    {
        public Analysis Project(string raw, List<Node>[,] dp,
            IForwardCandidateGraph<Edge> graph)
        {
        var finalists = Enum.GetValues<Lang>()
            .SelectMany(lang => dp[raw.Length, (int)lang])
            .OrderByDescending(x => x.Score).Take(Width * 3).ToArray();
        if (finalists.Length == 0)
            throw new ArgumentException("No legal H11 boundary path");
        var results = new List<BoundaryRoute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finalist in finalists)
        {
            var stack = new Stack<Edge>();
            for (var node = finalist; node.Edge is not null; node = node.Prev!)
                stack.Push(node.Edge);
            var en = new List<Utf16Span>();
            foreach (var edge in stack)
            {
                if (edge.Lang != Lang.En) continue;
                if (en.Count > 0 && en[^1].End == edge.Span.Start)
                    en[^1] = new Utf16Span(en[^1].Start, edge.Span.End);
                else en.Add(edge.Span);
            }
            string signature = string.Join(";", en.Select(x => $"{x.Start}:{x.End}"));
            if (!seen.Add(signature)) continue;
            results.Add(new BoundaryRoute(en, finalist.Score));
            diagnostics?.Selected(finalist);
            if (results.Count == 8) break;
        }
        diagnostics?.Finalists(Enum.GetValues<Lang>()
            .SelectMany(lang => dp[raw.Length, (int)lang]));
        bool uncertainty = results.Count > 1 &&
            results[0].Score - results[1].Score < 1.5;
        return new Analysis(raw, graph.InStartPositionOrder()
            .Select(x => new H10BoundaryPrototype.Edge(x.Span,
                x.Lang == Lang.En ? "English" :
                x.Lang == Lang.Ja ? "Japanese" : "Neutral",
                x.Origin, x.Score)).ToArray(), results, uncertainty);
        }
    }
}
