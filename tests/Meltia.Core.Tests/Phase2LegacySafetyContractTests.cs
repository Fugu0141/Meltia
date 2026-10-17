// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using Meltia.Core.Boundary;

internal static class Phase2LegacySafetyContractTests
{
    public static void Run(Action<bool, string> check)
    {
        var legacy = new MeltiaBoundaryEngine();
        static bool ThrowsExactly(Action action, string expected)
        {
            try { action(); return false; }
            catch (ArgumentException error)
            {
                return error.GetType() == typeof(ArgumentException) &&
                    error.Message == expected;
            }
        }
        static bool IsSingleMandatory(BoundaryResult result, Utf16Span span) =>
            result.Candidates.Where(c => c.Mandatory)
                .Select(c => c.Span).SequenceEqual([span]) &&
            result.Routes.All(r => r.EnglishSpans.Contains(span)) &&
            !result.AutoCommitAllowed;

        const string url = "https://example.com";
        const string nestedEmail = "https://user@example.com";
        var whole = new Utf16Span(0, url.Length);
        var partial = new Utf16Span(0, url.Length - 2);
        var nestedWhole = new Utf16Span(0, nestedEmail.Length);

        check(IsSingleMandatory(legacy.Analyze(nestedEmail), nestedWhole),
            "Phase2 C3 legacy keeps URL priority over contained email");
        check(IsSingleMandatory(legacy.Analyze(url), whole),
            "Phase2 C3 legacy protects entire URL as mandatory");
        check(IsSingleMandatory(legacy.Analyze("hello@example.com"),
            new Utf16Span(0, "hello@example.com".Length)),
            "Phase2 C3 legacy auto-protects standalone email");
        check(IsSingleMandatory(legacy.Analyze(url,
            new BoundaryEvidence(ExplicitEnglish: [whole, whole],
                Protected: [whole])), whole),
            "Phase2 C3 legacy exact EN/protected/auto duplicate is idempotent");
        check(IsSingleMandatory(legacy.Analyze(url,
            new BoundaryEvidence(ExplicitEnglish: [partial])), partial),
            "Phase2 C3 legacy manual partial English suppresses automatic URL");
        check(IsSingleMandatory(legacy.Analyze(url,
            new BoundaryEvidence(Protected: [partial])), partial),
            "Phase2 C3 legacy manual partial protected suppresses automatic URL");
        check(IsSingleMandatory(legacy.Analyze(url,
            new BoundaryEvidence(ExplicitEnglish: [partial],
                Protected: [partial])), partial),
            "Phase2 C3 legacy identical partial EN/protected stays one lock");

        check(ThrowsExactly(() => legacy.Analyze("abcdef",
            new BoundaryEvidence(ExplicitEnglish:
                [new Utf16Span(0, 4), new Utf16Span(2, 6)])),
            "Overlapping or contradictory mandatory ranges"),
            "Phase2 C3 legacy rejects crossing EN locks with exact message");
        check(ThrowsExactly(() => legacy.Analyze("abcdef",
            new BoundaryEvidence(ExplicitEnglish:
                [new Utf16Span(0, 6), new Utf16Span(2, 4)])),
            "Overlapping or contradictory mandatory ranges"),
            "Phase2 C3 legacy rejects contained EN lock");
        check(ThrowsExactly(() => legacy.Analyze("abcdef",
            new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
                ExplicitJapanese: [new Utf16Span(2, 6)])),
            "Overlapping or contradictory mandatory ranges"),
            "Phase2 C3 legacy rejects English/Japanese crossing");
        check(ThrowsExactly(() => legacy.Analyze(url,
            new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)])),
            "Overlapping or contradictory mandatory ranges"),
            "Phase2 C3 legacy rejects auto-URL and Japanese lock conflict");

        // This difference from H10/H11 must not be silently normalized.
        var crossingJapanese = legacy.Analyze("abcdef",
            new BoundaryEvidence(ExplicitJapanese:
                [new Utf16Span(0, 4), new Utf16Span(2, 6)]));
        check(crossingJapanese.Raw == "abcdef" &&
            crossingJapanese.Candidates.All(c => !c.Mandatory) &&
            crossingJapanese.Routes.Count > 0,
            "Phase2 C3 legacy does not reject overlapping Japanese-only locks");
        var containingJapanese = legacy.Analyze("abcdef",
            new BoundaryEvidence(ExplicitJapanese:
                [new Utf16Span(0, 6), new Utf16Span(2, 4)]));
        check(containingJapanese.Raw == "abcdef" &&
            containingJapanese.Routes.Count > 0,
            "Phase2 C3 legacy permits containing Japanese-only locks");

        const string emoji = "😀a";
        var emojiWhole = new Utf16Span(0, 2);
        check(IsSingleMandatory(legacy.Analyze(emoji,
            new BoundaryEvidence(ExplicitEnglish: [emojiWhole])), emojiWhole),
            "Phase2 C3 legacy preserves complete emoji UTF-16 pair");
        foreach (var invalid in new[] {
            new Utf16Span(-1, 2), new Utf16Span(0, 0),
            new Utf16Span(2, 1), new Utf16Span(0, 4)
        })
            check(ThrowsExactly(() => legacy.Analyze(emoji,
                new BoundaryEvidence(ExplicitEnglish: [invalid])),
                "Invalid UTF-16 range"),
                "Phase2 C3 legacy invalid numeric lock retains exact message");
        foreach (var split in new[] {
            new Utf16Span(0, 1), new Utf16Span(1, 2),
            new Utf16Span(1, 3)
        })
            check(ThrowsExactly(() => legacy.Analyze(emoji,
                new BoundaryEvidence(ExplicitJapanese: [split])),
                "Range splits a surrogate pair"),
                "Phase2 C3 legacy surrogate splitting keeps exact message");

        check(ThrowsExactly(() => legacy.Analyze(emoji,
            new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(-1, 2)],
                ExplicitEnglish: [new Utf16Span(0, 1)])),
            "Invalid UTF-16 range"),
            "Phase2 C3 legacy validates Japanese before mandatory English");
        check(ThrowsExactly(() => legacy.Analyze(emoji,
            new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 1)],
                ExplicitEnglish: [new Utf16Span(-1, 2)])),
            "Range splits a surrogate pair"),
            "Phase2 C3 legacy first invalid Japanese surrogate precedes EN range");

        // Host advisory evidence is validated even when it is only weak and
        // cannot affect auto-commit or override a mandatory user lock.
        check(ThrowsExactly(() => legacy.Analyze(emoji,
            new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(1, 2)])),
            "Range splits a surrogate pair"),
            "Phase2 C3 legacy validates advisory MeltypeEnglish UTF-16");
        var host = legacy.Analyze("commit",
            new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 6)]));
        check(host.Candidates.Any(c => c.Span == new Utf16Span(0, 6) &&
                c.Origin == "meltype-advisory" && !c.Mandatory) &&
            !host.AutoCommitAllowed,
            "Phase2 C3 legacy preserves default weak host advisory");
        var hostStrong = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
            ExperimentalStrongHostAdvisory: true)).Analyze("IME",
            new BoundaryEvidence(MeltypeEnglish: [new Utf16Span(0, 3)]));
        check(hostStrong.Candidates.Any(c => c.Span == new Utf16Span(0, 3) &&
                c.Origin == "meltype-strong-advisory" && !c.Mandatory) &&
            !hostStrong.AutoCommitAllowed,
            "Phase2 C3 legacy keeps opt-in strong host advisory");
        var manualOverridesHost = legacy.Analyze("commit",
            new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 6)],
                MeltypeEnglish: [new Utf16Span(0, 6)]));
        check(IsSingleMandatory(manualOverridesHost, new Utf16Span(0, 6)),
            "Phase2 C3 legacy user lock retains precedence over host advisory");

        bool tooLongFirst;
        try
        {
            legacy.Analyze(new string('a', MeltiaBoundaryEngine.MaxInputLength + 1),
                new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 0)]));
            tooLongFirst = false;
        }
        catch (ArgumentOutOfRangeException)
        {
            tooLongFirst = true;
        }
        check(tooLongFirst,
            "Phase2 C3 legacy max raw length guard precedes lock validation");

        // Use identical evidence in multiple engine modes: C3 must not
        // change candidate enumeration, score order or opt-in behavior.
        var modes = new[] {
            new MeltiaBoundaryOptions(),
            new MeltiaBoundaryOptions(ExperimentalStrongHostAdvisory: true,
                ExperimentalFragmentationPenalty: true),
            new MeltiaBoundaryOptions(ExperimentalUnknownLatinCandidates: true),
            new MeltiaBoundaryOptions(ExperimentalUnknownLatinCandidates: true,
                ExperimentalPhoneticLatticeDecoder: true,
                ExperimentalContextualIslandScoring: true,
                ExperimentalJapaneseFlankScoring: true)
        };
        bool deterministic = true;
        foreach (var options in modes)
        {
            var engine = new MeltiaBoundaryEngine(options);
            foreach (var raw in new[] { "tokyo", "commit", "kyouhaAPIde",
                "https://example.com", "😀a", "kinouhastrategywakaru" })
            {
                var a = engine.Analyze(raw);
                var b = engine.Analyze(raw);
                deterministic &= a.Raw == raw && b.Raw == raw &&
                    a.Candidates.SequenceEqual(b.Candidates) &&
                    a.Truncated == b.Truncated &&
                    a.Routes.Count == b.Routes.Count &&
                    a.Routes.Zip(b.Routes, (x, y) =>
                        x.Score.Equals(y.Score) &&
                        x.EnglishSpans.SequenceEqual(y.EnglishSpans)).All(x => x) &&
                    !a.AutoCommitAllowed && !b.AutoCommitAllowed;
                if (!deterministic) break;
            }
            if (!deterministic) break;
        }
        check(deterministic,
            "Phase2 C3 legacy optional modes preserve deterministic candidates/routes");
    }
}
