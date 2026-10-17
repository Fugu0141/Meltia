// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// Read-only ordered candidate edges for the current UTF-16 starting position.
/// Providers must preserve duplicates and the original insertion order.
/// </summary>
internal interface IForwardCandidateProvider<TEdge>
{
    IReadOnlyList<TEdge> At(int utf16Position);
}

/// <summary>
/// Model-owned language transition, score arithmetic and backpointer creation.
/// The shared search must not infer language semantics from the edge type.
/// </summary>
internal interface IForwardTransition<TNode, TEdge>
{
    int End(TEdge edge);
    (TNode Node, int NextState) Advance(TNode previous, TEdge edge);
}

/// <summary>
/// Model-owned beam retention: controls threshold, sort/tie semantics, trace,
/// and the order in which discarded paths are reported.
/// </summary>
internal interface IForwardBeamRetention<TNode>
{
    int PruneAboveCount { get; }
    void Prune(List<TNode> destination, int endPosition, int nextState);
}

/// <summary>
/// An ordered per-position graph. Enumeration for observed output is in
/// start-position bucket order, not chronological Add() order.
/// </summary>
internal interface IForwardCandidateGraph<TEdge> : IForwardCandidateProvider<TEdge>
{
    IEnumerable<TEdge> InStartPositionOrder();
}

/// <summary>
/// Model-specific input-to-graph generation. Validation and scoring semantics
/// belong to the model, not to the generic forward decoder.
/// </summary>
internal interface IForwardCandidateGenerator<in TRequest, TEdge>
{
    IForwardCandidateGraph<TEdge> Generate(TRequest request);
}

/// <summary>
/// Model-specific final-state reconstruction and output projection, including
/// deduplication and tie handling. No assumptions about route meaning here.
/// </summary>
internal interface IForwardRouteProjector<TNode, TEdge, TResult>
{
    TResult Project(string raw, List<TNode>[,] states,
        IForwardCandidateGraph<TEdge> graph);
}

/// <summary>
/// Reusable forward UTF-16 lattice traversal (H11 opt-in in Phase 4/E1).
/// Does not create candidates, compute transition weights, change pruning,
/// deduplicate paths or project final Top-K. The legacy H11 loop stays the
/// production oracle until all compatibility gates are independently reviewed.
/// </summary>
internal static class ForwardLatticeDecoder
{
    internal static List<TNode>[,] Decode<TNode, TEdge>(
        int inputLength, int stateCount, int initialState, TNode initialNode,
        IForwardCandidateProvider<TEdge> candidates,
        IForwardTransition<TNode, TEdge> transition,
        IForwardBeamRetention<TNode> retention)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stateCount);
        ArgumentOutOfRangeException.ThrowIfNegative(initialState);
        if (initialState >= stateCount)
            throw new ArgumentOutOfRangeException(nameof(initialState));
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(retention);
        if (retention.PruneAboveCount < 1)
            throw new ArgumentOutOfRangeException(nameof(retention));

        var states = new List<TNode>[inputLength + 1, stateCount];
        for (int i = 0; i <= inputLength; i++)
            for (int state = 0; state < stateCount; state++)
                states[i, state] = [];
        states[0, initialState].Add(initialNode);

        // Same traversal order as the H11 frozen oracle: position, language,
        // previous node, candidate. The current state's list is never appended
        // during its enumeration because every legal edge advances forward.
        for (int i = 0; i < inputLength; i++)
        {
            var at = candidates.At(i);
            ArgumentNullException.ThrowIfNull(at);
            for (int previousState = 0; previousState < stateCount; previousState++)
            foreach (var previous in states[i, previousState])
            foreach (var edge in at)
            {
                int end = transition.End(edge);
                if (end <= i || end > inputLength)
                    throw new ArgumentException("Non-forward lattice edge");
                var (next, nextState) = transition.Advance(previous, edge);
                if ((uint)nextState >= (uint)stateCount)
                    throw new ArgumentOutOfRangeException(nameof(transition),
                        "Transition produced an invalid state");
                var destination = states[end, nextState];
                destination.Add(next);
                if (destination.Count > retention.PruneAboveCount)
                    retention.Prune(destination, end, nextState);
            }
        }
        return states;
    }
}
