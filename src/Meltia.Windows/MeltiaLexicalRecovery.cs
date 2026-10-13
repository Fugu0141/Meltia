// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using Meltype.Composition;

namespace Meltype;

/// <summary>
/// Preview-only lexical safety net for strong English spellings whose raw
/// keystrokes can otherwise be consumed by Japanese romaji normalization.
/// Unlike a global English dictionary switch, this requires either a
/// product name or a listed word with orthographic English evidence.
/// The inferred prefix may only be followed by a complete readable Japanese
/// particle phrase, and no user-learned Japanese decision may be displaced.
/// </summary>
internal static class MeltiaLexicalRecovery
{
    // Product identity is not yet present in the inherited English lexicon.
    // Ordinary English terms are resolved using Meltype's bundled dictionary.
    private static readonly HashSet<string> ProductNames =
        new(StringComparer.OrdinalIgnoreCase) { "meltia" };

    private static readonly string[] JapaneseParticles =
        ["wo", "ni", "de", "ga", "ha", "to", "no"];

    public static IReadOnlyList<CompositionSegment>? TryRecover(
        IReadOnlyList<CompositionUnit> units, string pending,
        IReadOnlyList<CompositionSegment> legacy, CompositionDetector detector)
    {
        if (units.Count == 0 || legacy.Count == 0 ||
            legacy.Any(x => x.IsProtected)) return null;

        var raw = string.Concat(units.Select(x => x.Raw)) + pending;
        if (raw.Length is < 5 or > 128 || !raw.All(char.IsAsciiLetter))
            return null;

        // A prior explicit Japanese correction always wins.
        if (detector.Memory?.Get(raw.ToLowerInvariant()) == false)
            return null;

        if (IsStrongEnglishWord(raw, detector))
        {
            // Preserve exactly what was typed, including capitalization.
            // A trailing standalone consonant can still be 'pending' here.
            if (legacy.Count == 1 && legacy[0].IsEnglish && legacy[0].Raw == raw)
                return null;
            return [new CompositionSegment(true, "", raw)];
        }

        // Do not eat an incomplete Japanese suffix. Only a finished particle
        // followed by fully readable romaji may be split away from English.
        if (pending.Length != 0) return null;
        var boundaries = new HashSet<int> { 0 };
        var position = 0;
        foreach (var unit in units)
        {
            position += unit.Raw.Length;
            boundaries.Add(position);
        }
        if (position != raw.Length) return null;

        // Prefer the longest strong English prefix. Do not discover it from
        // a guessed transliteration: it must be a real dictionary entry or
        // the application's own name.
        for (int end = raw.Length - 2; end >= 5; end--)
        {
            if (!boundaries.Contains(end)) continue;
            var prefix = raw[..end];
            if (!IsStrongEnglishWord(prefix, detector) ||
                detector.Memory?.Get(prefix.ToLowerInvariant()) == false)
                continue;

            var suffix = raw[end..];
            if (!JapaneseParticles.Any(p =>
                suffix.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            var reading = detector.Romaji.Analyze(suffix.ToLowerInvariant());
            if (!reading.IsValid || !string.IsNullOrEmpty(reading.Partial))
                continue;

            var kana = detector.Romaji.ConvertLenient(
                suffix.ToLowerInvariant(), final: true);
            if (kana.Length == 0 || kana.Any(char.IsAsciiLetter))
                continue;

            var result = new CompositionSegment[]
            {
                new(true, "", prefix),
                new(false, kana, suffix)
            };
            if (string.Concat(result.Select(x => x.Raw)) == raw &&
                !(legacy.Count == result.Length &&
                  legacy.Zip(result, (a, b) =>
                    a.IsEnglish == b.IsEnglish && a.Raw == b.Raw &&
                    a.Kana == b.Kana).All(x => x)))
                return result;
            return null;
        }

        return null;
    }

    private static bool IsStrongEnglishWord(
        string word, CompositionDetector detector)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length < 5 || !lower.All(char.IsAsciiLetterLower))
            return false;
        if (detector.Memory?.Get(lower) == false ||
            detector.IsCommonJapanese?.Invoke(lower) == true)
            return false;
        if (ProductNames.Contains(lower)) return true;

        // Unlike 'tokyo', 'sake', 'ime', etc., words containing English
        // orthographic clues remain recoverable when they are explicitly
        // listed in Meltype's dictionary. This intentionally avoids
        // treating all listed romanizable words as English.
        return detector.IsListedEnglishWord(lower) &&
            lower.Any(c => c is 'c' or 'l' or 'q' or 'v' or 'x');
    }
}
