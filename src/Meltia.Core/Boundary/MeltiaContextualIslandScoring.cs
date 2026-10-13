// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltia.Core.Boundary;

/// <summary>
/// Meltia v0.9: non-lexical, local evidence for two English candidates
/// separated by one Japanese particle. This adjustment is ONLY applied
/// to a complete route containing both islands; simply mentioning a
/// word in the lattice never promotes an English interpretation.
/// </summary>
internal static class MeltiaContextualIslandScoring
{
    private static readonly HashSet<string> JapaneseConnectors =
        new(StringComparer.OrdinalIgnoreCase)
        { "no", "to", "de", "ga", "ni", "wo", "ha" };

    // Partial contextual evidence, smaller than one added span cost (4.5).
    // It deliberately does NOT outweigh a large difference in orthography
    // and cannot make a purely Japanese input suddenly English if it has
    // no qualifying English candidate islands.
    private const double SeparationEvidenceBonus = 3.0;
    // Short speculative fragments commonly arise by splitting a *single*
    // Latin word on an internal Japanese-looking pair (e.g. photo-ni-cswom
    // inside "photonicswomiru", or ocryp-to-graphy in "cryptography").
    // Two independently long Latin islands are stronger corroborating
    // evidence; rejecting short pairs avoids rewarding these false seams.
    // This is a conservative research-mode guard, not a lexical decision.
    private const int MinCorroboratedIslandLength = 7;

    public static double Between(string raw, Utf16Span left, Utf16Span right)
    {
        if (left.Length < MinCorroboratedIslandLength ||
            right.Length < MinCorroboratedIslandLength ||
            right.Start <= left.End || right.Start - left.End != 2 ||
            right.End > raw.Length || left.Start < 0)
            return 0;
        var connector = raw.Substring(left.End, 2);
        if (!JapaneseConnectors.Contains(connector)) return 0;

        // A Japanese particle inside a readable Japanese word does not
        // establish two English islands; require independent non-phonetic
        // evidence for both sides. Intentionally conservative on "tokyo".
        var a = raw.Substring(left.Start, left.Length);
        var b = raw.Substring(right.Start, right.Length);
        if (!a.All(char.IsAsciiLetter) || !b.All(char.IsAsciiLetter) ||
            MeltiaRomaji.CanRead(a) || MeltiaRomaji.CanRead(b))
            return 0;

        // v0.7 emits many plausible substrings. A proposal that begins
        // with a long phonetic Japanese prefix (e.g. arigatou + part of
        // an English word), or ends with a Japanese suffix, cannot act as
        // independent evidence of an English island. Without this check,
        // boosting every particle-looking gap badly harms one-island cases.
        if (ContainsLongJapaneseFlank(a) || ContainsLongJapaneseFlank(b))
            return 0;
        return SeparationEvidenceBonus;
    }

    private static bool ContainsLongJapaneseFlank(string token)
    {
        for (int n = 5; n < token.Length; n++)
            if (MeltiaRomaji.CanRead(token[..n]) ||
                MeltiaRomaji.CanRead(token[^n..]))
                return true;
        return false;
    }
}
