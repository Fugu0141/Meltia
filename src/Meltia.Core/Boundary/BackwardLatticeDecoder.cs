// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// Model-owned suffix transition generation. Reads only positions greater than
/// the current one; score arithmetic and span semantics never enter this engine.
/// </summary>
internal interface IBackwardOfferProvider<TRoute>
{
    IReadOnlyList<TRoute> OffersAt(int position, IReadOnlyList<TRoute>[] suffixes);
}

/// <summary>
/// Model-owned retention (including deduplication, tie order and diversity).
/// A generic score-only beam would silently break the v0.8 invariants.
/// </summary>
internal interface IBackwardRouteRetention<TRoute>
{
    IReadOnlyList<TRoute> Select(int position, IReadOnlyList<TRoute> offered);
}

internal static class BackwardLatticeDecoder
{
    internal static IReadOnlyList<TRoute>[] Decode<TRoute>(
        int inputLength, IReadOnlyList<TRoute> terminal,
        IBackwardOfferProvider<TRoute> offers,
        IBackwardRouteRetention<TRoute> retention)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputLength);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(retention);

        var suffixes = new IReadOnlyList<TRoute>[inputLength + 1];
        suffixes[inputLength] = terminal;
        for (int i = inputLength - 1; i >= 0; --i)
        {
            var offered = offers.OffersAt(i, suffixes);
            ArgumentNullException.ThrowIfNull(offered);
            suffixes[i] = retention.Select(i, offered)
                ?? throw new InvalidOperationException("Null backward retained routes");
        }
        return suffixes;
    }
}
