// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
// Offline evaluation adapter only: always use the dictionaries from the same
// Meltype.Core assembly used by CompositionDetector. Never inject gold words.
using Meltype.Detection;
using Meltia.Core.Boundary;

namespace Meltia.BoundaryEvaluation;

internal static class MeltypeLexiconEvidence
{
    private static readonly string[] English = DictionarySource.Load("english.txt", null)
        .Where(w => w.Length is >= 2 and <= 72 && w.All(char.IsAsciiLetter))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal).ToArray();
    private static readonly string[] Names = ProperNouns.Load(null).LowercaseWords
        .Where(w => w.Length is >= 2 and <= 72 && w.All(char.IsAsciiLetter))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal).ToArray();

    public static int EnglishWordTypes => English.Length;
    public static int ProperNameTypes => Names.Length;

    // The current advisory engine checks whole-word strings by IndexOf.
    // Filter to matching entries BEFORE the bounded candidate engine so a
    // 4,096-term truncation does not silently depend on file order.
    // This is not learning from the gold label or exporting the full lexicon.
    public static BoundaryEvidence For(string raw)
    {
        var words = English.Where(w => raw.Contains(w, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var names = Names.Where(w => raw.Contains(w, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new BoundaryEvidence(EnglishWords: words, ProperNames: names);
    }
}
