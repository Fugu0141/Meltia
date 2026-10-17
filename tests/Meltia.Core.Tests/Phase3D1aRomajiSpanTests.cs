// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Diagnostics;
using System.Text.RegularExpressions;
using Meltia.Core.Boundary;

internal static class Phase3D1aRomajiSpanTests
{
    // Frozen implementation copied from pre-D1a main (c5479724).
    // It deliberately allocates; never use it in the production hot path.
    private static readonly HashSet<string> LegacySyllables = BuildLegacySyllables();

    private static HashSet<string> BuildLegacySyllables()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prefix in new[] { "", "k", "s", "t", "n", "h", "m", "r",
            "g", "z", "d", "b", "p", "f", "v" })
            foreach (var vowel in "aiueo")
                result.Add(prefix + vowel);
        foreach (var token in new[] {
            "shi","chi","tsu","fu","ji","si","ti","tu","hu","zi","nn",
            "sha","shu","sho","cha","chu","cho","ja","ju","jo","she","che",
            "ya","yu","yo","wa","wo","wi","we","xtu","ltu",
            "kya","kyu","kyo","nya","nyu","nyo","hya","hyu","hyo",
            "mya","myu","myo","rya","ryu","ryo","gya","gyu","gyo",
            "bya","byu","byo","pya","pyu","pyo","jya","jyu","jyo",
            "fa","fi","fe","fo" })
            result.Add(token);
        return result;
    }

    private static bool LegacyCanRead(string value)
    {
        if (value.Length == 0) return true;
        if (value.Length > 1024 || !value.All(char.IsAsciiLetter)) return false;
        var raw = value.ToLowerInvariant();
        var reachable = new bool[raw.Length + 1];
        reachable[0] = true;
        for (int i = 0; i < raw.Length; ++i)
        {
            if (!reachable[i]) continue;
            if (raw[i] == 'n' &&
                (i + 1 == raw.Length || "aiueoy".IndexOf(raw[i + 1]) < 0))
                reachable[i + 1] = true;
            if (i + 1 < raw.Length && raw[i] == raw[i + 1] &&
                "aeioun".IndexOf(raw[i]) < 0)
                reachable[i + 1] = true;
            for (int length = 1; length <= 4 && i + length <= raw.Length; ++length)
                if (LegacySyllables.Contains(raw.Substring(i, length)))
                    reachable[i + length] = true;
        }
        return reachable[raw.Length];
    }

    private static IReadOnlyList<Utf16Span> LegacyIslands(string text)
    {
        var result = new List<Utf16Span>();
        foreach (Match match in Regex.Matches(text, "[A-Za-z]+"))
        {
            if (match.Length > 256) continue;
            var cuts = new List<int> { match.Index };
            for (int i = match.Index + 1; i < match.Index + match.Length; i++)
            {
                if (char.IsUpper(text[i]) && char.IsLower(text[i - 1]) ||
                    i >= match.Index + 2 && char.IsLower(text[i]) &&
                    char.IsUpper(text[i - 1]) && char.IsUpper(text[i - 2]))
                    cuts.Add(i);
            }
            cuts.Add(match.Index + match.Length);
            for (int i = 1; i < cuts.Count; i++)
            {
                var part = new Utf16Span(cuts[i - 1], cuts[i]);
                if (part.Length >= 4 &&
                    LegacyCanRead(text.Substring(part.Start, part.Length)))
                    result.Add(part);
            }
        }
        return result;
    }

    private static bool Equivalent(string sample)
    {
        bool baseline = LegacyCanRead(sample);
        return MeltiaRomaji.CanRead(sample) == baseline &&
            MeltiaRomaji.CanRead(sample.AsSpan()) == baseline &&
            MeltiaRomaji.CanRead(("!!" + sample + "!!")
                .AsSpan(2, sample.Length)) == baseline;
    }

    private static (long Bytes, double Milliseconds, int TrueCount) Measure(
        Func<string, bool> decoder, string[] samples, int iterations)
    {
        for (int r = 0; r < 4; r++)
            foreach (string sample in samples) _ = decoder(sample);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        int trueCount = 0;
        for (int repeat = 0; repeat < iterations; repeat++)
            foreach (string sample in samples)
                if (decoder(sample)) trueCount++;
        watch.Stop();
        return (GC.GetAllocatedBytesForCurrentThread() - before,
            watch.Elapsed.TotalMilliseconds, trueCount);
    }

    public static void Run(Action<bool, string> check)
    {
        check(Equivalent(""), "Phase3 D1a legacy empty string and Span agree");
        bool nullLegacy = false, nullNew = false;
        try { _ = LegacyCanRead(null!); }
        catch (NullReferenceException) { nullLegacy = true; }
        try { _ = MeltiaRomaji.CanRead((string)null!); }
        catch (NullReferenceException) { nullNew = true; }
        check(nullLegacy && nullNew,
            "Phase3 D1a null string preserves existing exception type");

        var interesting = new[] {
            "n", "nn", "nna", "na", "nya", "KYA", "KURU", "shinjuku",
            "konnichiwa", "xtu", "ltu", "shi", "chi", "tsu",
            "ci", "cye", "cyu", "cyo", "sShi", "NN", "nnn", "kk",
            "aaa", "i", "aiueo", "aIuEo", "kya", "vyo",
            "Tokyo", "TOkYo", "ABC", "abc", "new", "gameover",
            "https://example.com", "a9", "😀", "na😀", "ひらがな",
            "a\0", "\uD800", "\uDC00", "\t", " "
        };
        check(interesting.All(Equivalent),
            "Phase3 D1a curated ambiguity, uppercase, non-ASCII and malformed UTF-16");

        // All possible ASCII lower-case 1-, 2- and 3-letter strings;
        // ~18k exact checks against independent pre-refactor code.
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        bool exhaustive = true;
        int checkedCount = 0;
        foreach (char a in letters)
        {
            exhaustive &= Equivalent(a.ToString());
            checkedCount++;
            foreach (char b in letters)
            {
                exhaustive &= Equivalent(string.Concat(a, b));
                checkedCount++;
                foreach (char c in letters)
                {
                    exhaustive &= Equivalent(string.Concat(a, b, c));
                    checkedCount++;
                }
            }
        }
        check(exhaustive && checkedCount == 26 + 676 + 17576,
            "Phase3 D1a 18,278 exhaustive ASCII lengths 1-3 match frozen oracle");

        var rng = new Random(20261017);
        const string alphabet = "aAbBcCdDeEfFgGhHiIjJkKlLmMnNoOpPqQrRsStTuUvVwWxXyYzZ0.-";
        bool seeded = true;
        for (int n = 0; n < 5000; n++)
        {
            int length = n % 7 == 0 ? rng.Next(1, 1027) : rng.Next(0, 40);
            var text = new string(Enumerable.Range(0, length)
                .Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
            if (!Equivalent(text))
            {
                seeded = false;
                break;
            }
        }
        seeded &= Equivalent(new string('n', 1024)) &&
            Equivalent(new string('n', 1025)) &&
            Equivalent(new string('A', 1024)) &&
            Equivalent(new string('A', 1025));
        check(seeded,
            "Phase3 D1a 5,000 seeded lengths/mixedcase and 1024/1025 guard");

        var islandSamples = new[] {
            "", "nihongo", "aNIHONGOde", "TokyoTOkyo", "ABCdEfg",
            "kyouhaKingGnudeSPYAIRde", "watashi😀nihongo",
            "http://example.com", "nnihonn", "KURU", "aCiCyEde",
            new string('a', 257), new string('n', 1024)
        };
        check(islandSamples.All(x => LegacyIslands(x)
            .SequenceEqual(MeltiaRomaji.Islands(x))),
            "Phase3 D1a Islands remains identical across cuts and input limits");

        var perfSamples = new[] { "nihongo", "konnichiwa",
            "kyouhaKingGnude", "shinjuku", "xtu", "ci",
            "strategy", "GAMEOVER", "NNA", "KURU" };
        const int repetitions = 1500;
        var before = Measure(LegacyCanRead, perfSamples, repetitions);
        var after = Measure(MeltiaRomaji.CanRead, perfSamples, repetitions);
        check(before.TrueCount == after.TrueCount,
            "Phase3 D1a representative perf input count matches legacy oracle");
        Console.WriteLine("PHASE3_D1A_ROMAJI_ALLOC: " +
            "samples=" + perfSamples.Length + " repeat=" + repetitions +
            " legacy_bytes=" + before.Bytes + " span_bytes=" + after.Bytes +
            " legacy_ms=" + before.Milliseconds.ToString("F3",
                System.Globalization.CultureInfo.InvariantCulture) +
            " span_ms=" + after.Milliseconds.ToString("F3",
                System.Globalization.CultureInfo.InvariantCulture) +
            " (single runner, diagnostic only; not an SLA)");
    }
}
