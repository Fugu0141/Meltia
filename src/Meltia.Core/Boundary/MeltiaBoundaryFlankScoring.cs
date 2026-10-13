// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// v1.0 phase 4, default-off: scoring-only guard against an unknown-English
/// proposal absorbing an entire substantial Japanese romaji flank.
/// Does not create/delete candidate edges, is not a language classifier,
/// and never overrides manual locks.
/// </summary>
internal static class MeltiaBoundaryFlankScoring
{
    private const int MinJapaneseFlankLength = 7;
    private const int MinUnreadableRemainingLength = 5;
    private const double FlankPenalty = 3.0;

    public static double Adjustment(string raw, BoundaryCandidate candidate)
    {
        if (candidate.Mandatory ||
            candidate.Origin != MeltiaDiverseCandidateSelector.UnknownOrigin)
            return 0;

        var span = candidate.Span;
        if (span.Start < 0 || span.End > raw.Length ||
            span.Length < MinJapaneseFlankLength + MinUnreadableRemainingLength)
            return 0;

        string token = raw.Substring(span.Start, span.Length);
        if (!token.All(char.IsAsciiLetter) || MeltiaRomaji.CanRead(token))
            return 0;

        bool swallowedLeft = false, swallowedRight = false;
        // A 7+ letter prefix/suffix which is independently phonetic Japanese,
        // while the rest is 5+ letters not wholly Japanese, is suspicious.
        // Never rely on a bare 'no'/'to' substring for this penalty.
        for (int length = MinJapaneseFlankLength;
             length <= token.Length - MinUnreadableRemainingLength; length++)
        {
            if (!swallowedLeft &&
                MeltiaRomaji.CanRead(token[..length]) &&
                !MeltiaRomaji.CanRead(token[length..]))
                swallowedLeft = true;

            if (!swallowedRight &&
                MeltiaRomaji.CanRead(token[^length..]) &&
                !MeltiaRomaji.CanRead(token[..^length]))
                swallowedRight = true;

            if (swallowedLeft && swallowedRight) break;
        }

        // Bounded to -6 even if a long proposed Latin span contains several
        // internally readable Japanese substrings.
        return -FlankPenalty * ((swallowedLeft ? 1 : 0) +
                                (swallowedRight ? 1 : 0));
    }
}
