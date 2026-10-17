// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Diagnostics;
using System.Reflection;
using Meltia.Core.Boundary;

internal static class Phase3D1bCharacterEvidenceTests
{
    // Frozen pre-D1b string-based likelihood algorithm. Its frequency tables
    // are read from the unmodified seed Build() fields to avoid duplicating
    // copyrightable corpus text or accidentally changing training counts.
    private static double LegacyLikelihood(string word, Dictionary<string, int> model)
    {
        string normalized = "^^" + word.ToLowerInvariant() + "$";
        double sum = 0;
        for (int i = 2; i < normalized.Length; i++)
        {
            string history = normalized.Substring(i - 2, 2);
            string eventKey = history + normalized[i];
            double hits = model.GetValueOrDefault(eventKey);
            double prefix = model.GetValueOrDefault(history);
            sum += Math.Log((hits + 0.4) / (prefix + 27 * 0.4));
        }
        return sum;
    }

    private static double LegacyDifference(string raw,
        Dictionary<string, int> en, Dictionary<string, int> ja)
    {
        double english = LegacyLikelihood(raw, en);
        double japanese = LegacyLikelihood(raw, ja);
        return Math.Clamp((english - japanese) / Math.Max(1, raw.Length),
            -2.0, 2.0);
    }

    private static (long Bytes, double Milliseconds, long Checksum) Measure(
        Func<string, double> score, string[] samples, int repetitions)
    {
        long checksum = 0;
        for (int n = 0; n < 3; n++)
            foreach (var sample in samples)
                checksum ^= BitConverter.DoubleToInt64Bits(score(sample));
        checksum = 0;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (int n = 0; n < repetitions; n++)
            foreach (var sample in samples)
                checksum ^= BitConverter.DoubleToInt64Bits(score(sample));
        clock.Stop();
        return (GC.GetAllocatedBytesForCurrentThread() - before,
            clock.Elapsed.TotalMilliseconds, checksum);
    }

    public static void Run(Action<bool, string> check)
    {
        var model = new H10CharacterEvidence();
        var fields = BindingFlags.NonPublic | BindingFlags.Instance;
        var english = (Dictionary<string, int>)typeof(H10CharacterEvidence)
            .GetField("_en", fields)!.GetValue(model)!;
        var japanese = (Dictionary<string, int>)typeof(H10CharacterEvidence)
            .GetField("_ja", fields)!.GetValue(model)!;
        check(english.Count > 100 && japanese.Count > 100,
            "Phase3 D1b original two fixed ngram dictionaries unchanged");

        bool Exactly(string raw)
        {
            long expected = BitConverter.DoubleToInt64Bits(
                LegacyDifference(raw, english, japanese));
            long actualString = BitConverter.DoubleToInt64Bits(model.Difference(raw));
            long actualSpan = BitConverter.DoubleToInt64Bits(model.Difference(
                ("!!" + raw + "!!").AsSpan(2, raw.Length)));
            return expected == actualString && expected == actualSpan;
        }

        check(new[] { "", "a", "A", "n", "nn", "Nn", "cye",
            "commit", "CoMmIt", "nihongo", "Konnichiwa", "KURU",
            "999", "a9b", "a.b", "a-b", "^^", "$", "^$", "$$",
            "\0", "\0a", "a\0b", "a\t", "\r\n", "こんにちは",
            "😀", "n😀i", "Österreich", "İstanbul", "ß",
            "\uD800", "\uDC00" }.All(Exactly),
            "Phase3 D1b curated ASCII/nonASCII, padding chars, NUL, case and surrogate");
        bool nullThrows = false;
        try { _ = model.Difference((string)null!); }
        catch (NullReferenceException) { nullThrows = true; }
        check(nullThrows,
            "Phase3 D1b Difference(string) keeps null exception contract");

        // All [a-z] values of length 1, 2 and 3, and the empty string.
        bool exhaustive = Exactly("");
        int count = 1;
        foreach (char a in "abcdefghijklmnopqrstuvwxyz")
        {
            exhaustive &= Exactly(a.ToString());
            count++;
            foreach (char b in "abcdefghijklmnopqrstuvwxyz")
            {
                exhaustive &= Exactly(string.Concat(a, b));
                count++;
                foreach (char c in "abcdefghijklmnopqrstuvwxyz")
                {
                    exhaustive &= Exactly(string.Concat(a, b, c));
                    count++;
                }
            }
        }
        check(exhaustive && count == 18279,
            "Phase3 D1b 18,279 exhaustive 0-3-letter inputs match floating bits");

        var rng = new Random(20261017);
        const string symbols = "aAbBcCdDeEfFgGhHiIjJkKlLmMnNoOpPqQrRsStTuUvVwWxXyYzZ01239^$.-_:/@\0";
        bool seeded = true;
        for (int n = 0; n < 3000; n++)
        {
            int len = n % 7 == 0 ? rng.Next(0, 1025) : rng.Next(0, 45);
            var chars = new char[len];
            for (int k = 0; k < len; k++)
                chars[k] = symbols[rng.Next(symbols.Length)];
            if (!Exactly(new string(chars)))
            {
                seeded = false;
                break;
            }
        }
        seeded &= Exactly(new string('a', 1024)) &&
            Exactly(new string('N', 1024)) &&
            Exactly(new string('a', 1025));
        check(seeded,
            "Phase3 D1b 3,000 mixedcase/ASCII seeded cases, 1024/1025 lengths");

        var perf = new[] {
            "kyouhacommitdesu", "kinouhastrategywakaru", "deployer",
            "Konnichiwa", "README", "foobar", "nihongowotamesu",
            "ci", "vscode", "meltia", "nonnn", "API"
        };
        const int repetitions = 1200;
        var oldRun = Measure(s => LegacyDifference(s, english, japanese), perf, repetitions);
        var newRun = Measure(model.Difference, perf, repetitions);
        check(oldRun.Checksum == newRun.Checksum,
            "Phase3 D1b microbenchmark old/new score checksum identical");
        Console.WriteLine("PHASE3_D1B_EVIDENCE_ALLOC: samples=" + perf.Length +
            " repeat=" + repetitions +
            " legacy_bytes=" + oldRun.Bytes +
            " span_bytes=" + newRun.Bytes +
            " legacy_ms=" + oldRun.Milliseconds.ToString("F3",
                System.Globalization.CultureInfo.InvariantCulture) +
            " span_ms=" + newRun.Milliseconds.ToString("F3",
                System.Globalization.CultureInfo.InvariantCulture) +
            " (single runner; not an IME latency or performance SLA)");
    }
}
