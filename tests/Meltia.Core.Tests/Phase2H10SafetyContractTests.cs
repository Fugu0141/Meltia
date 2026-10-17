// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using Meltia.Core.Boundary;

internal static class Phase2H10SafetyContractTests
{
    public static void Run(Action<bool, string> check)
    {
        var h10 = new H10BoundaryPrototype();
        static bool ThrowsExactly(Action action, string message)
        {
            try { action(); return false; }
            catch (ArgumentException error)
            {
                return error.GetType() == typeof(ArgumentException) &&
                    error.Message == message;
            }
        }
        static bool HasMandatory(H10BoundaryPrototype.Analysis analysis,
            params Utf16Span[] spans) =>
            analysis.Edges.Where(x => x.Origin == "mandatory-latin")
                .Select(x => x.Span).SequenceEqual(spans);

        const string url = "https://example.com";
        const string urlWithEmail = "https://user@example.com";
        var whole = new Utf16Span(0, url.Length);
        var partial = new Utf16Span(0, url.Length - 2);

        // Critical intentional difference from H11: nested URL/email
        // produces different, overlapping mandatory ranges and is rejected.
        check(ThrowsExactly(() => h10.Analyze(urlWithEmail),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 nested email in URL retains conflict semantics");
        check(HasMandatory(h10.Analyze(url), whole),
            "Phase2 C2 H10 standalone URL retains mandatory span");
        check(HasMandatory(h10.Analyze(url,
            new BoundaryEvidence(ExplicitEnglish: [whole])), whole),
            "Phase2 C2 H10 exact EN lock and detected URL are idempotent");
        check(HasMandatory(h10.Analyze(url,
            new BoundaryEvidence(Protected: [whole, whole])), whole),
            "Phase2 C2 H10 exact protected/auto URL duplicates collapse");
        check(HasMandatory(h10.Analyze(url,
            new BoundaryEvidence(ExplicitEnglish: [whole],
                Protected: [whole])), whole),
            "Phase2 C2 H10 manual EN plus protected exact duplicates collapse");

        // H10 keeps the auto-protected candidate even when a manual lock
        // already partly covers its range, and then rejects the conflict.
        check(ThrowsExactly(() => h10.Analyze(url,
            new BoundaryEvidence(ExplicitEnglish: [partial])),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 partial EN vs URL remains conflicting");
        check(ThrowsExactly(() => h10.Analyze(url,
            new BoundaryEvidence(Protected: [partial])),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 partial protected vs URL remains conflicting");
        check(ThrowsExactly(() => h10.Analyze(url,
            new BoundaryEvidence(ExplicitJapanese: [partial])),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 Japanese vs URL remains conflicting");
        check(ThrowsExactly(() => h10.Analyze("abcdef",
            new BoundaryEvidence(ExplicitEnglish:
                [new Utf16Span(0, 4), new Utf16Span(2, 6)])),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 crossing English locks rejected");
        check(ThrowsExactly(() => h10.Analyze("abcdef",
            new BoundaryEvidence(ExplicitEnglish:
                [new Utf16Span(0, 6), new Utf16Span(2, 4)])),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 contained English lock rejected");
        check(ThrowsExactly(() => h10.Analyze("abcdef",
            new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(0, 4)],
                ExplicitJapanese: [new Utf16Span(2, 6)])),
            "Conflicting mandatory spans"),
            "Phase2 C2 H10 English/Japanese crossing rejected");
        check(ThrowsExactly(() => h10.Analyze("abcdef",
            new BoundaryEvidence(ExplicitJapanese:
                [new Utf16Span(0, 4), new Utf16Span(2, 6)])),
            "Overlapping Japanese locks"),
            "Phase2 C2 H10 Japanese/Japanese crossing keeps distinct error");
        check(ThrowsExactly(() => h10.Analyze("abcdef",
            new BoundaryEvidence(ExplicitJapanese:
                [new Utf16Span(0, 6), new Utf16Span(2, 4)])),
            "Overlapping Japanese locks"),
            "Phase2 C2 H10 Japanese/Japanese containment rejected");
        var japaneseDuplicated = h10.Analyze("tokyo",
            new BoundaryEvidence(ExplicitJapanese:
                [new Utf16Span(0, 5), new Utf16Span(0, 5)]));
        check(japaneseDuplicated.Routes[0].EnglishSpans.Count == 0,
            "Phase2 C2 H10 identical Japanese locks remain idempotent");

        const string emoji = "😀abc";
        var emojiWhole = new Utf16Span(0, 2);
        var emojiLocked = h10.Analyze(emoji,
            new BoundaryEvidence(ExplicitEnglish: [emojiWhole]));
        check(HasMandatory(emojiLocked, emojiWhole) &&
            emojiLocked.Routes.Any(x => x.EnglishSpans.Contains(emojiWhole)),
            "Phase2 C2 H10 full UTF-16 surrogate pair lock survives");
        foreach (var invalid in new[] {
            new Utf16Span(-1, 2), new Utf16Span(0, 0),
            new Utf16Span(3, 2), new Utf16Span(0, 6)
        })
            check(ThrowsExactly(() => h10.Analyze(emoji,
                new BoundaryEvidence(ExplicitEnglish: [invalid])),
                "Invalid H10 span"),
                "Phase2 C2 H10 invalid numeric range keeps exact message");
        foreach (var split in new[] {
            new Utf16Span(0, 1), new Utf16Span(1, 2),
            new Utf16Span(1, 3)
        })
            check(ThrowsExactly(() => h10.Analyze(emoji,
                new BoundaryEvidence(ExplicitJapanese: [split])),
                "H10 span splits UTF-16 surrogate pair"),
                "Phase2 C2 H10 surrogate split keeps exact message");
        check(ThrowsExactly(() => h10.Analyze(emoji,
            new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(-1, 2)],
                ExplicitEnglish: [new Utf16Span(0, 1)])),
            "Invalid H10 span"),
            "Phase2 C2 H10 numeric JA invalidity precedes EN surrogate error");

        // H10 never uses the host advisory list, unlike the old Core.
        check(h10.Analyze(emoji, new BoundaryEvidence(
            MeltypeEnglish: [new Utf16Span(1, 2)])).Raw == emoji,
            "Phase2 C2 H10 ignores unsupported MeltypeEnglish evidence");
        var samples = new[] {
            "", "tokyo", "nihongo", "kyouhaGitHubde", "ABCniiku",
            "https://example.com", "hello@example.com", "😀a",
            "Are you sushi?", "handanwodelegatesuru", "yaml", "50ccgenntuki"
        };
        bool deterministic = true;
        foreach (var raw in samples)
        {
            var a = h10.Analyze(raw);
            var b = h10.Analyze(raw);
            deterministic &= a.Raw == raw && b.Raw == raw &&
                a.Edges.SequenceEqual(b.Edges) &&
                a.Routes.Count == b.Routes.Count &&
                a.Routes.Zip(b.Routes, (x, y) => x.Score.Equals(y.Score) &&
                    x.EnglishSpans.SequenceEqual(y.EnglishSpans)).All(x => x) &&
                a.NeedsConfirmation == b.NeedsConfirmation &&
                !a.AutoCommitAllowed && !b.AutoCommitAllowed;
            if (!deterministic) break;
        }
        check(deterministic,
            "Phase2 C2 H10 representative samples preserve deterministic edges/routes");
    }
}
