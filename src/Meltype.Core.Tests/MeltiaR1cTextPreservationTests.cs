// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// R1c: source-characterization of raw UTF-16 and protected spans. No OS hooks.
using Meltype.Composition;

namespace Meltype.Tests;

internal static class MeltiaR1cTextPreservationTests
{
    private static CompositionText NewText() => new(CompositionTests.Detector);

    private static void AssertRawInvariant(CompositionText text, string expected)
    {
        Assert.Equal(expected, text.Raw, "Raw is the exact concatenation of typed UTF-16 units");
        Assert.Equal(expected, string.Concat(text.Units.Select(x => x.Raw)) + text.Pending,
            "CompositionUnit.Raw + Pending must not lose the original input");
    }

    [Test]
    public static void R1c_RomajiPrefixes_KeepOriginalRawWhileKanaInterpretationChanges()
    {
        foreach (var original in new[] { "kyouha", "commit", "API", "Google", "n", "ky", "tsu" })
        {
            var text = NewText();
            var raw = "";
            foreach (var c in original)
            {
                text.Append(c);
                raw += c;
                AssertRawInvariant(text, raw);
            }
            // Segmentation is observational: it must not rewrite Raw.
            _ = text.Segments(final: false);
            _ = text.Segments(final: true);
            AssertRawInvariant(text, original);
        }
    }

    [Test]
    public static void R1c_PartialRomaji_BackspaceRemovesPendingOneCodeUnitAtATime()
    {
        var text = NewText();
        text.Append('k');
        AssertRawInvariant(text, "k");
        text.Append('y');
        AssertRawInvariant(text, "ky");
        text.RemoveLast();
        AssertRawInvariant(text, "k");
        text.RemoveLast();
        AssertRawInvariant(text, "");
        Assert.True(text.IsEmpty, "the incomplete romaji must be fully removable");
        Assert.Equal("", text.Pending);
    }

    [Test]
    public static void R1c_NonAsciiKanaInput_RawTracksUtf16CodeUnitsWithoutNormalization()
    {
        // KanaInput appends raw keyboard characters exactly, without romanization.
        var text = NewText();
        text.KanaInput = true;
        var raw = "e\u0301😀日本語";
        foreach (var c in raw)
            text.AppendKana(c, c);
        AssertRawInvariant(text, raw);
        Assert.Equal(raw.Length, text.Units.Sum(x => x.Raw.Length),
            "length counts UTF-16 code units, not grapheme clusters");
        Assert.Equal("", text.Pending);
        Assert.Equal(raw.Length, text.Units.Count);
    }

    [Test]
    public static void R1c_LatinAccentAndSurrogate_RawDoesNotChangeDuringAppend()
    {
        // Test the actual Append(char) path as separate UTF-16 code units.
        foreach (var sample in new[] { "e\u0301", "😀", "日本語", "A😀B", "naïve" })
        {
            var text = NewText();
            var raw = "";
            foreach (var c in sample)
            {
                text.Append(c);
                raw += c;
                AssertRawInvariant(text, raw);
            }
            text.Clear();
            AssertRawInvariant(text, "");
        }
    }

    [Test]
    public static void R1c_UrlAndEmail_ProtectionSpansUseRawUtf16Offsets()
    {
        foreach (var sample in new[] { "https://example.com/path", "user@example.com" })
        {
            var text = NewText();
            foreach (var c in sample) text.Append(c);
            AssertRawInvariant(text, sample);
            var spans = text.ProtectedSpans();
            Assert.True(spans.Count > 0, "protected token must be located: " + sample);
            Assert.True(spans.Any(s => s.Start == 0 && s.End == sample.Length),
                "full URL/email must be protected: " + sample);
            Assert.True(text.IsProtectedRaw, "complete protected token is not subject to kana conversion");
            var segments = text.Segments();
            Assert.True(segments.Any(s => s.IsProtected && s.Raw == sample),
                "segmentation must preserve exact protected text");
            AssertRawInvariant(text, sample);
        }

        // A non-BMP character is TWO UTF-16 code units. Code-point-based
        // offsets would incorrectly report 1 instead of 2 here.
        const string email = "user@example.com";
        var embedded = "😀" + email;
        var embeddedText = NewText();
        foreach (var c in embedded) embeddedText.Append(c);
        AssertRawInvariant(embeddedText, embedded);
        var embeddedSpans = embeddedText.ProtectedSpans();
        Assert.True(embeddedSpans.Any(s =>
            s.Start == 2 && s.End == embedded.Length &&
            embedded.Substring(s.Start, s.Length) == email),
            "email must begin at UTF-16 offset 2 after the surrogate pair");
        Assert.True(embeddedText.Segments().Any(s => s.IsProtected && s.Raw == email),
            "protected email segment must retain its exact text");
        AssertRawInvariant(embeddedText, embedded);
    }

    [Test]
    public static void R1c_ProtectedUrl_BackspaceUsesSingleRawCharacterDeletion()
    {
        var text = NewText();
        var sample = "https://example.com";
        foreach (var c in sample) text.Append(c);
        Assert.True(text.HasProtectedTail, "protected URL reaches end of raw input");
        text.RemoveLast();
        AssertRawInvariant(text, sample[..^1]);
        text.Append('m');
        AssertRawInvariant(text, sample);
    }

    [Test]
    public static void R1c_SegmentsCacheAndFinalFlag_DoNotMutateOriginalInput()
    {
        var text = NewText();
        var sample = "kyouhagooglede";
        foreach (var c in sample) text.Append(c);
        var first = text.Segments(final: false).Select(s => (s.IsEnglish, s.Kana, s.Raw, s.IsProtected)).ToArray();
        var second = text.Segments(final: false).Select(s => (s.IsEnglish, s.Kana, s.Raw, s.IsProtected)).ToArray();
        Assert.Equal(string.Join("|", first), string.Join("|", second),
            "same Raw and options must yield stable segments");
        _ = text.Segments(final: true);
        AssertRawInvariant(text, sample);
    }

    [Test]
    public static void R1c_Clear_RemovesRawPendingAndResetsOverrides()
    {
        var text = NewText();
        text.Mode = DisplayMode.FullWidthAlphanumeric;
        text.Case = LetterCase.Upper;
        foreach (var c in "nky") text.Append(c);
        text.Clear();
        AssertRawInvariant(text, "");
        Assert.True(text.IsEmpty, "Clear must discard composition");
        Assert.Equal(DisplayMode.Auto, text.Mode);
        Assert.Equal(LetterCase.AsTyped, text.Case);
        Assert.Equal("", text.Pending);
        Assert.Equal(0, text.ProtectedSpans().Count);
    }
}
