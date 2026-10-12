// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using System.Text;
using Meltype.Composition;
using Meltia.CompositionBridge;
using Meltia.Core.Boundary;

namespace Meltype;

/// <summary>
/// Opt-in live segmentation only (no automatic text commit). The Windows
/// Meltype keyboard remains the source of key handling, kana units, Kanji
/// conversion and all manual corrections. Falls back on every uncertainty,
/// protection or UTF-16 alignment problem.
/// </summary>
internal static class MeltiaPreviewBoundary
{
    // One process, UI-thread preview. OFF unless the user explicitly enables it.
    public static bool Enabled { get; set; } =
        Environment.GetEnvironmentVariable("MELTIA_BOUNDARY_PREVIEW") == "1";

    private static readonly MeltiaBoundaryEngine Engine = new(
        new MeltiaBoundaryOptions(
            ExperimentalUnknownLatinCandidates: true,
            ExperimentalPhoneticLatticeDecoder: true,
            ExperimentalContextualIslandScoring: true,
            ExperimentalUncertaintyReview: true,
            ExperimentalLongRunWindowing: true,
            ExperimentalDiverseCandidatePruning: true,
            ExperimentalJapaneseFlankScoring: true));

    public static IReadOnlyList<CompositionSegment>? Refine(
        IReadOnlyList<CompositionUnit> units, string pending,
        IReadOnlyList<CompositionSegment> legacy)
    {
        // Do not interpret a pending romaji consonant, kana-keyboard input,
        // punctuation, URL, IME commands or unbounded composition.
        if (!Enabled || pending.Length != 0 || units.Count < 1 ||
            legacy.Count == 0 || legacy.Any(x => x.IsProtected)) return null;
        var raw = string.Concat(units.Select(u => u.Raw));
        if (raw.Length is < 5 or > 128 || !raw.All(char.IsAsciiLetter))
            return null;

        // Host suggestions are evidence, not mandatory locks. Manual
        // override/protected sources are handled separately and never changed.
        var comparison = MeltypeBoundaryShadowBridge.CompareSegments(
            raw, legacy, Engine);
        var result = comparison.Meltia;
        if (comparison.TopRouteMatchesMeltype || result.Truncated ||
            result.Review is not { NeedsConfirmation: false } review ||
            review.TopTwoScoreGap is null or < 1.5 ||
            result.Routes.Count == 0) return null;

        var route = result.Routes[0].EnglishSpans;
        if (route.Count == 0) return null;

        // Never split a Japanese kana unit (e.g. "kyo" -> "きょ") or a UTF-16
        // surrogate pair: all span edges must align to existing unit boundaries.
        var boundaries = new int[units.Count + 1];
        for (var i = 0; i < units.Count; i++)
            boundaries[i + 1] = boundaries[i] + units[i].Raw.Length;
        if (boundaries[^1] != raw.Length) return null;
        var boundarySet = boundaries.ToHashSet();
        if (route.Any(x => x.Start >= x.End || x.Start < 0 ||
            x.End > raw.Length || !boundarySet.Contains(x.Start) ||
            !boundarySet.Contains(x.End))) return null;

        var proposed = new List<CompositionSegment>();
        var pos = 0;
        foreach (var span in route)
        {
            if (span.Start < pos) return null;
            if (!AppendPiece(pos, span.Start, false) ||
                !AppendPiece(span.Start, span.End, true)) return null;
            pos = span.End;
        }
        if (!AppendPiece(pos, raw.Length, false)) return null;
        if (string.Concat(proposed.Select(x => x.Raw)) != raw)
            return null;

        return proposed;

        bool AppendPiece(int start, int end, bool english)
        {
            if (start == end) return true;
            int from = Array.BinarySearch(boundaries, start);
            int to = Array.BinarySearch(boundaries, end);
            if (from < 0 || to < 0 || from >= to) return false;
            var text = new StringBuilder();
            var kana = new StringBuilder();
            for (var i = from; i < to; i++)
            {
                text.Append(units[i].Raw);
                if (!english) kana.Append(units[i].Kana);
            }
            proposed.Add(new CompositionSegment(english, kana.ToString(),
                text.ToString()));
            return true;
        }
    }
}
