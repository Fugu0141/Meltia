// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

public sealed partial class H11BoundaryPrototype
{
    // This is intentionally an internal comparison path. Analyze/AnalyzeWithTrace
    // keep executing the unchanged legacy H11 beam loop in Phase 4/E1.
    internal Analysis AnalyzeWithSharedDecoder(string raw,
        BoundaryEvidence? evidence = null) =>
        AnalyzeCore(raw, evidence, diagnostics: null, useForwardDecoder: true);

    internal DiagnosticAnalysis AnalyzeWithSharedDecoderTrace(string raw,
        BoundaryEvidence? evidence = null)
    {
        var diagnostics = new TraceCollector();
        var result = AnalyzeCore(raw, evidence, diagnostics, useForwardDecoder: true);
        return new DiagnosticAnalysis(result, diagnostics.Build(raw, result));
    }

    internal Analysis AnalyzeWithPipeline(string raw,
        BoundaryEvidence? evidence = null) =>
        AnalyzeCore(raw, evidence, diagnostics: null,
            useForwardDecoder: true, useRouteProjector: true);

    internal DiagnosticAnalysis AnalyzeWithPipelineTrace(string raw,
        BoundaryEvidence? evidence = null)
    {
        var diagnostics = new TraceCollector();
        var result = AnalyzeCore(raw, evidence, diagnostics,
            useForwardDecoder: true, useRouteProjector: true);
        return new DiagnosticAnalysis(result, diagnostics.Build(raw, result));
    }

    private sealed record H11GenerationRequest(string Raw,
        IReadOnlyList<Utf16Span> Hard, IReadOnlyList<Utf16Span> ExplicitJa,
        TraceCollector? Diagnostics);

    private sealed class H11CandidateGenerator :
        IForwardCandidateGenerator<H11GenerationRequest, Edge>
    {
        public IForwardCandidateGraph<Edge> Generate(H11GenerationRequest request) =>
            new H11OrderedCandidates(BuildCandidateEdges(request));
    }

    private sealed class H11OrderedCandidates(List<Edge>[] candidates)
        : IForwardCandidateGraph<Edge>
    {
        public IReadOnlyList<Edge> At(int utf16Position) => candidates[utf16Position];

        public IEnumerable<Edge> InStartPositionOrder() =>
            candidates.SelectMany(bucket => bucket);
    }

    private sealed class H11ForwardTransition : IForwardTransition<Node, Edge>
    {
        public int End(Edge edge) => edge.Span.End;

        public (Node Node, int NextState) Advance(Node previous, Edge candidate)
        {
            bool swap = previous.Last != Lang.Neutral &&
                candidate.Lang != Lang.Neutral && previous.Last != candidate.Lang;
            var last = candidate.Lang == Lang.Neutral ? previous.Last : candidate.Lang;
            double transition = swap ? -1.6 : 0;
            // Keep floating-point addition order byte-for-byte identical to H11.
            if (candidate.Lang == Lang.Ja && swap && previous.Edge is { } prior &&
                prior.Lang == Lang.En && prior.Origin == "baseline-lexicon-word" &&
                candidate.Span.Length >= 2) transition += 0.9;
            var next = new Node(previous.Score + candidate.Score + transition,
                last, candidate, previous);
            return (next, (int)last);
        }
    }

    private sealed class H11OrderedBeam(TraceCollector? diagnostics)
        : IForwardBeamRetention<Node>
    {
        public int PruneAboveCount => Width * 2;

        public void Prune(List<Node> destination, int endPosition, int nextState)
        {
            // List.Sort is deliberately unstable for tied Score: never replace
            // it with a "nicer" tie-breaking rule during an equivalence migration.
            destination.Sort((a, b) => b.Score.CompareTo(a.Score));
            diagnostics?.Drop(endPosition, (Lang)nextState,
                destination.Skip(Width), destination[Width - 1].Score);
            destination.RemoveRange(Width, destination.Count - Width);
        }
    }
}
