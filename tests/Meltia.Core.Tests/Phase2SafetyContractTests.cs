// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using Meltia.Core.Boundary;
using System.Text.RegularExpressions;

internal static class Phase2SafetyContractTests
{
    public static void Run(Action<bool, string> check)
    {
        var h11 = new H11BoundaryPrototype();
        var h10 = new H10BoundaryPrototype();
        var legacy = new MeltiaBoundaryEngine();
        static bool Rejected(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }

        const string urlWithEmail = "https://user@example.com";
        var entire = new Utf16Span(0, urlWithEmail.Length);
        var emailAt = urlWithEmail.IndexOf("user@example.com", StringComparison.Ordinal);
        var emailInsideUrl = new Utf16Span(emailAt, urlWithEmail.Length);
        var urlRegex = new Regex(@"https?://[^\s]+", RegexOptions.CultureInvariant);
        var emailRegex = new Regex(
            @"(?<![\w.@])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}",
            RegexOptions.CultureInvariant);

        // Fix both helper branches directly before moving H10/legacy to them.
        var skipAny = new List<Utf16Span>();
        ProtectedSpanDetector.AppendMatches(urlWithEmail, skipAny,
            ProtectionOverlapPolicy.SkipAnyOverlap, urlRegex, emailRegex);
        check(skipAny.SequenceEqual([entire]),
            "Phase2 helper SkipAnyOverlap keeps URL before nested email");

        var skipExact = new List<Utf16Span>();
        ProtectedSpanDetector.AppendMatches(urlWithEmail, skipExact,
            ProtectionOverlapPolicy.SkipExactDuplicate, urlRegex, emailRegex);
        check(skipExact.SequenceEqual([entire, emailInsideUrl]),
            "Phase2 helper SkipExactDuplicate keeps distinct nested email");
        ProtectedSpanDetector.AppendMatches(urlWithEmail, skipExact,
            ProtectionOverlapPolicy.SkipExactDuplicate, urlRegex, emailRegex);
        check(skipExact.SequenceEqual([entire, emailInsideUrl]),
            "Phase2 helper SkipExactDuplicate collapses exact matches");
        var repeatAny = new List<Utf16Span>();
        ProtectedSpanDetector.AppendMatches(urlWithEmail, repeatAny,
            ProtectionOverlapPolicy.SkipAnyOverlap, urlRegex, urlRegex, emailRegex);
        check(repeatAny.SequenceEqual([entire]),
            "Phase2 helper SkipAnyOverlap also collapses exact matches");

        // User hard ranges are collected before automatic URL/email detection.
        // A partially overlapping hard range suppresses BOTH auto-matches.
        var manualPart = new Utf16Span(0, urlWithEmail.Length - 2);
        var skipAgainstManual = new List<Utf16Span> { manualPart };
        ProtectedSpanDetector.AppendMatches(urlWithEmail, skipAgainstManual,
            ProtectionOverlapPolicy.SkipAnyOverlap, urlRegex, emailRegex);
        check(skipAgainstManual.SequenceEqual([manualPart]),
            "Phase2 helper preserves partial manual lock before URL/email");

        foreach (var manualEvidence in new[] {
            new BoundaryEvidence(ExplicitEnglish: [manualPart]),
            new BoundaryEvidence(Protected: [manualPart])
        })
        {
            var analyzed = h11.Analyze(urlWithEmail, manualEvidence);
            check(analyzed.Raw == urlWithEmail &&
                analyzed.Edges.Where(e => e.Origin == "mandatory")
                    .Select(e => e.Span).SequenceEqual([manualPart]),
                "Phase2 H11 partial explicit/protected EN overrides auto URL");
        }

        static bool ThrowsWithMessage(Action action, string expected)
        {
            try { action(); return false; }
            catch (ArgumentException ex) { return ex.Message == expected; }
        }
        check(ThrowsWithMessage(() => h11.Analyze("😀a",
            new BoundaryEvidence(ExplicitEnglish: [new Utf16Span(1, 2)])),
            "Invalid UTF-16 language lock"),
            "Phase2 H11 retains exact invalid UTF-16 error message");
        check(ThrowsWithMessage(() => h11.Analyze(urlWithEmail,
            new BoundaryEvidence(
                ExplicitEnglish: [manualPart],
                Protected: [new Utf16Span(2, urlWithEmail.Length)])),
            "Contradictory language locks"),
            "Phase2 H11 partial manual EN/protected conflict retains exact error");
        check(ThrowsWithMessage(() => h11.Analyze(urlWithEmail,
            new BoundaryEvidence(ExplicitJapanese: [manualPart])),
            "Contradictory language locks"),
            "Phase2 H11 JA vs detected URL retains exact conflict message");

        check(h11.Analyze(urlWithEmail).Routes.All(r =>
            r.EnglishSpans.SequenceEqual([entire])),
            "Phase2 H11 URL has precedence over overlapping email");
        check(legacy.Analyze(urlWithEmail).Routes.All(r =>
            r.EnglishSpans.Contains(entire)),
            "Phase2 legacy URL/email overlap policy is unchanged");
        check(Rejected(() => h10.Analyze(urlWithEmail)),
            "Phase2 H10 retains distinct URL/email overlap rejection");

        var same = new Utf16Span(0, 5);
        check(h11.Analyze("tokyo", new BoundaryEvidence(
            ExplicitEnglish: [same, same], Protected: [same]))
            .Routes.All(r => r.EnglishSpans.SequenceEqual([same])),
            "Phase2 H11 duplicate EN/protected is idempotent");
        check(h11.Analyze("tokyo", new BoundaryEvidence(
            ExplicitJapanese: [same, same])).Routes[0].EnglishSpans.Count == 0,
            "Phase2 H11 duplicate Japanese lock is idempotent");
        check(Rejected(() => h11.Analyze("abcdef", new BoundaryEvidence(
            ExplicitEnglish: [new Utf16Span(0, 4), new Utf16Span(2, 6)]))),
            "Phase2 H11 rejects crossing English locks");
        check(Rejected(() => h11.Analyze("abcdef", new BoundaryEvidence(
            ExplicitEnglish: [new Utf16Span(0, 6), new Utf16Span(2, 4)]))),
            "Phase2 H11 rejects contained English lock");
        check(Rejected(() => h11.Analyze("abcdef", new BoundaryEvidence(
            ExplicitJapanese: [new Utf16Span(0, 4), new Utf16Span(2, 6)]))),
            "Phase2 H11 rejects crossing Japanese locks");
        check(Rejected(() => h11.Analyze("abcdef", new BoundaryEvidence(
            ExplicitEnglish: [new Utf16Span(0, 4)],
            ExplicitJapanese: [new Utf16Span(2, 6)]))),
            "Phase2 H11 rejects contradictory EN/JA locks");
        check(Rejected(() => h11.Analyze("abcdef", new BoundaryEvidence(
            Protected: [new Utf16Span(0, 6)],
            ExplicitJapanese: [new Utf16Span(2, 4)]))),
            "Phase2 H11 rejects Japanese within protected range");
        check(Rejected(() => h11.Analyze("https://example.com",
            new BoundaryEvidence(ExplicitJapanese: [new Utf16Span(0, 5)]))),
            "Phase2 H11 rejects Japanese overlapping auto-protected URL");

        const string emoji = "😀a";
        check(h11.Analyze(emoji).Edges.All(e =>
            e.Span.Start != 1 && e.Span.End != 1),
            "Phase2 H11 generated edges never split emoji");
        check(h11.Analyze(emoji, new BoundaryEvidence(
            ExplicitEnglish: [new Utf16Span(0, 2)]))
            .Routes.All(r => r.EnglishSpans.Contains(new Utf16Span(0, 2))),
            "Phase2 H11 complete surrogate pair can be explicitly locked");
        foreach (var invalid in new[] {
            new Utf16Span(-1, 2), new Utf16Span(0, 0),
            new Utf16Span(2, 1), new Utf16Span(0, 4),
            new Utf16Span(0, 1), new Utf16Span(1, 2)
        })
            check(Rejected(() => h11.Analyze(emoji,
                new BoundaryEvidence(ExplicitEnglish: [invalid]))),
                "Phase2 H11 rejects invalid or surrogate-splitting lock " +
                invalid.Start + ":" + invalid.End);

        // H11 ignores the host advisory input. Legacy validates its range.
        var invalidHost = new Utf16Span(1, 2);
        check(h11.Analyze(emoji, new BoundaryEvidence(
                MeltypeEnglish: [invalidHost])).Raw == emoji &&
              Rejected(() => legacy.Analyze(emoji,
                new BoundaryEvidence(MeltypeEnglish: [invalidHost]))),
            "Phase2 H11/legacy MeltypeEnglish contract stays distinct");
    }
}
