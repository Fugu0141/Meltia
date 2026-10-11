// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltia.Core.Boundary;

/// <summary>Small, deterministic phonetic plausibility check. Not a replacement for Mozc.</summary>
public static class MeltiaRomaji
{
    private static readonly HashSet<string> Syllables = Build();

    private static HashSet<string> Build()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prefix in new[] { "", "k", "s", "t", "n", "h", "m", "r", "g", "z", "d", "b", "p", "f", "v" })
            foreach (var vowel in "aiueo")
                result.Add(prefix + vowel);
        foreach (var token in new[] {
            "shi","chi","tsu","fu","ji","si","ti","tu","hu","zi","nn",
            "sha","shu","sho","cha","chu","cho","ja","ju","jo","she","che",
            "ya","yu","yo","wa","wo","wi","we","xtu","ltu",
            "kya","kyu","kyo","nya","nyu","nyo","hya","hyu","hyo",
            "mya","myu","myo","rya","ryu","ryo","gya","gyu","gyo",
            "bya","byu","byo","pya","pyu","pyo","jya","jyu","jyo",
            "fa","fi","fe","fo" }) result.Add(token);
        return result;
    }

    /// <summary>True only when the entire ASCII input has a plausible Japanese romaji path.</summary>
    public static bool CanRead(string value)
    {
        if (value.Length == 0) return true;
        if (value.Length > 1024 || !value.All(char.IsAsciiLetter)) return false;
        var raw = value.ToLowerInvariant();
        var reachable = new bool[raw.Length + 1];
        reachable[0] = true;
        for (int i = 0; i < raw.Length; ++i)
        {
            if (!reachable[i]) continue;
            if (raw[i] == 'n' && (i + 1 == raw.Length || "aiueoy".IndexOf(raw[i + 1]) < 0))
                reachable[i + 1] = true;
            if (i + 1 < raw.Length && raw[i] == raw[i + 1] &&
                "aeioun".IndexOf(raw[i]) < 0) reachable[i + 1] = true;
            for (int length = 1; length <= 4 && i + length <= raw.Length; ++length)
                if (Syllables.Contains(raw.Substring(i, length))) reachable[i + length] = true;
        }
        return reachable[raw.Length];
    }

    /// <summary>Find phonetic islands without losing the original uppercase characters.</summary>
    public static IReadOnlyList<Utf16Span> Islands(string text)
    {
        var output = new List<Utf16Span>();
        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z]+"))
        {
            if (match.Length > 256) continue; // explicit complexity bound
            var cuts = new List<int> { match.Index };
            for (int index = match.Index + 1; index < match.Index + match.Length; ++index)
            {
                if (char.IsUpper(text[index]) && char.IsLower(text[index - 1]) ||
                    index >= match.Index + 2 && char.IsLower(text[index]) &&
                    char.IsUpper(text[index - 1]) && char.IsUpper(text[index - 2]))
                    cuts.Add(index);
            }
            cuts.Add(match.Index + match.Length);
            for (int i = 1; i < cuts.Count; ++i)
            {
                var part = new Utf16Span(cuts[i - 1], cuts[i]);
                if (part.Length >= 4 && CanRead(text.Substring(part.Start, part.Length)))
                    output.Add(part);
            }
        }
        return output;
    }
}
