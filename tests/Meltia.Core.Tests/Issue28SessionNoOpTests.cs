// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using Meltia.Core.Boundary;

// Issue #28: an empty insertion is not an edit and must not discard explicit locks.
// Run via the existing .NET 10 Core contract test executable.
internal static class Issue28SessionNoOpTests
{
    public static void Run(MeltiaBoundaryEngine engine, Action<bool, string> check)
    {
        static bool SameAnalysis(BoundaryResult left, BoundaryResult right)
        {
            if (left.Raw != right.Raw || left.Truncated != right.Truncated ||
                left.AutoCommitAllowed != right.AutoCommitAllowed ||
                !left.Candidates.SequenceEqual(right.Candidates) ||
                left.Routes.Count != right.Routes.Count)
                return false;

            for (int i = 0; i < left.Routes.Count; i++)
                if (!left.Routes[i].EnglishSpans.SequenceEqual(right.Routes[i].EnglishSpans) ||
                    !left.Routes[i].Score.Equals(right.Routes[i].Score))
                    return false;

            if (left.Review is null || right.Review is null)
                return left.Review is null && right.Review is null;

            return left.Review.StableEnglishSpans.SequenceEqual(right.Review.StableEnglishSpans) &&
                left.Review.ContestedEnglishSpans.SequenceEqual(right.Review.ContestedEnglishSpans) &&
                left.Review.UnresolvedReadableEnglish.SequenceEqual(right.Review.UnresolvedReadableEnglish) &&
                left.Review.TopTwoScoreGap == right.Review.TopTwoScoreGap &&
                left.Review.CompetitiveRouteCount == right.Review.CompetitiveRouteCount &&
                left.Review.NeedsConfirmation == right.Review.NeedsConfirmation &&
                left.Review.Reason == right.Review.Reason;
        }

        string[] tokyoLexicon = ["tokyo"];
        var english = new MeltiaSession(engine);
        english.Insert("tokyo");
        english.Lock(new Utf16Span(0, 5), english: true);
        var englishLocked = english.Analyze(tokyoLexicon);
        check(englishLocked.Routes.All(r => r.EnglishSpans.Contains(new Utf16Span(0, 5))) &&
            !SameAnalysis(englishLocked, engine.Analyze("tokyo",
                new BoundaryEvidence(EnglishWords: tokyoLexicon))),
            "issue #28 English lock is observable before no-op");

        english.MoveCaret(2);
        english.Insert("");
        check(english.Raw == "tokyo" && english.CaretUtf16 == 2 &&
            SameAnalysis(englishLocked, english.Analyze(tokyoLexicon)),
            "issue #28 empty insert at middle preserves English lock, raw and caret");

        english.MoveCaret(0);
        english.Backspace();
        check(english.CaretUtf16 == 0 && SameAnalysis(englishLocked, english.Analyze(tokyoLexicon)),
            "issue #28 Backspace at start is a no-op for locks");
        english.MoveCaret(english.Raw.Length);
        english.DeleteForward();
        check(english.CaretUtf16 == 5 && SameAnalysis(englishLocked, english.Analyze(tokyoLexicon)),
            "issue #28 DeleteForward at end is a no-op for locks");
        english.MoveCaret(0);
        check(SameAnalysis(englishLocked, english.Analyze(tokyoLexicon)),
            "issue #28 caret moves preserve explicit locks");

        var japanese = new MeltiaSession(engine);
        japanese.Insert("API");
        japanese.Lock(new Utf16Span(0, 3), english: false);
        var japaneseLocked = japanese.Analyze(["API"]);
        check(japaneseLocked.Routes.All(r => !r.EnglishSpans.Any()) &&
            !SameAnalysis(japaneseLocked, engine.Analyze("API",
                new BoundaryEvidence(EnglishWords: ["API"]))),
            "issue #28 Japanese lock is observable before no-op");

        japanese.MoveCaret(0);
        japanese.Insert("");
        japanese.MoveCaret(3);
        japanese.Insert("");
        check(japanese.Raw == "API" && japanese.CaretUtf16 == 3 &&
            SameAnalysis(japaneseLocked, japanese.Analyze(["API"])),
            "issue #28 empty inserts at start and end preserve Japanese lock");

        var editedEnglish = new MeltiaSession(engine);
        editedEnglish.Insert("tokyo");
        editedEnglish.Lock(new Utf16Span(0, 5), english: true);
        editedEnglish.Insert("!");
        check(editedEnglish.Raw == "tokyo!" && editedEnglish.CaretUtf16 == 6 &&
            SameAnalysis(editedEnglish.Analyze(tokyoLexicon),
                engine.Analyze("tokyo!", new BoundaryEvidence(EnglishWords: tokyoLexicon))),
            "issue #28 nonempty insertion still invalidates English lock");

        var editedJapanese = new MeltiaSession(engine);
        editedJapanese.Insert("API");
        editedJapanese.Lock(new Utf16Span(0, 3), english: false);
        editedJapanese.Insert("!");
        check(editedJapanese.Raw == "API!" && editedJapanese.CaretUtf16 == 4 &&
            SameAnalysis(editedJapanese.Analyze(["API"]),
                engine.Analyze("API!", new BoundaryEvidence(EnglishWords: ["API"]))),
            "issue #28 nonempty insertion still invalidates Japanese lock");

        var utf16 = new MeltiaSession(engine);
        utf16.Insert("😀tokyo");
        utf16.Lock(new Utf16Span(2, 7), english: true);
        utf16.MoveCaret(2);
        var utf16Locked = utf16.Analyze(tokyoLexicon);
        utf16.Insert("");
        check(utf16.Raw == "😀tokyo" && utf16.CaretUtf16 == 2 &&
            SameAnalysis(utf16Locked, utf16.Analyze(tokyoLexicon)),
            "issue #28 empty insert at UTF-16 boundary preserves lock and caret");

        bool splitsSurrogate = false;
        try { utf16.MoveCaret(1); }
        catch (ArgumentException) { splitsSurrogate = true; }
        check(splitsSurrogate && utf16.CaretUtf16 == 2 &&
            SameAnalysis(utf16Locked, utf16.Analyze(tokyoLexicon)),
            "issue #28 surrogate-splitting caret is still rejected");

        utf16.Insert("🧪");
        check(utf16.Raw == "😀🧪tokyo" && utf16.CaretUtf16 == 4 &&
            SameAnalysis(utf16.Analyze(tokyoLexicon),
                engine.Analyze("😀🧪tokyo", new BoundaryEvidence(EnglishWords: tokyoLexicon))),
            "issue #28 UTF-16 two-code-unit insert updates caret and clears locks");
        utf16.Backspace();
        check(utf16.Raw == "😀tokyo" && utf16.CaretUtf16 == 2 &&
            SameAnalysis(utf16.Analyze(tokyoLexicon),
                engine.Analyze("😀tokyo", new BoundaryEvidence(EnglishWords: tokyoLexicon))),
            "issue #28 UTF-16 backspace deletes a full surrogate pair");
        utf16.DeleteForward();
        check(utf16.Raw == "😀okyo" && utf16.CaretUtf16 == 2 &&
            SameAnalysis(utf16.Analyze(tokyoLexicon),
                engine.Analyze("😀okyo", new BoundaryEvidence(EnglishWords: tokyoLexicon))),
            "issue #28 DeleteForward after surrogate keeps a valid UTF-16 boundary");

        bool nullRejected = false;
        try { japanese.Insert(null!); }
        catch (ArgumentNullException) { nullRejected = true; }
        check(nullRejected && japanese.CaretUtf16 == 3 &&
            SameAnalysis(japaneseLocked, japanese.Analyze(["API"])),
            "issue #28 null insert remains invalid and does not clear locks");
    }
}
