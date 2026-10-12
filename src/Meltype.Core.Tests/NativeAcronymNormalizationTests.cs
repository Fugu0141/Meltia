// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for the native Meltype input normalizer.
// The independent Meltia boundary engine does not drive the live IME here.

using Meltype.Composition;

namespace Meltype.Tests;

internal static class NativeAcronymNormalizationTests
{
    private static CompositionText Type(string raw)
    {
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in raw) text.Append(c);
        return text;
    }

    [Test]
    public static void AcronymVowelsAreRestoredWhenTheSecondUppercaseLetterArrives()
    {
        foreach (var raw in new[] { "IME", "API", "AI", "IDE", "AIM", "CPU", "UI" })
        {
            var text = Type(raw);
            Assert.Equal(raw, text.Raw, "typed input must remain lossless: " + raw);
            Assert.Equal(raw, string.Concat(text.Units.Select(unit => unit.Kana)) + text.Pending,
                "uppercase acronym must not keep an earlier kana vowel: " + raw);
            Assert.Equal(raw, string.Concat(text.Segments(final: true).Select(segment =>
                segment.IsEnglish ? segment.Raw : segment.Kana)),
                "automatic presentation must show the uppercase token: " + raw);
        }
    }

    [Test]
    public static void FirstUppercaseVowelIsNotPrematurelyLocked()
    {
        var text = Type("I");
        // The next keystroke may still make this a Japanese or English expression.
        Assert.Equal("I", text.Raw);
        text.Append('M');
        Assert.Equal("IM", text.Raw);
        Assert.Equal("IM", string.Concat(text.Units.Select(unit => unit.Kana)) + text.Pending);
        text.Append('E');
        Assert.Equal("IME", string.Concat(text.Units.Select(unit => unit.Kana)) + text.Pending);
    }

    [Test]
    public static void AcronymRawSurvivesFollowingJapaneseAndOriginalInputIsNotRewritten()
    {
        foreach (var raw in new[] { "IMEwotsukau", "APIde", "kyouhaIMEwotsukau", "kaAI" })
        {
            var text = Type(raw);
            Assert.Equal(raw, text.Raw, "boundary decisions must not change the typed key sequence");
            var composed = string.Concat(text.Units.Select(u => u.Raw)) + text.Pending;
            Assert.Equal(raw, composed);
            var acronym = raw.Contains("IME", StringComparison.Ordinal) ? "IME" : raw.Contains("API", StringComparison.Ordinal) ? "API" : "AI";
            var units = text.Units;
            var position = 0;
            var observed = "";
            foreach (var unit in units)
            {
                if (position >= raw.IndexOf(acronym, StringComparison.Ordinal) &&
                    position < raw.IndexOf(acronym, StringComparison.Ordinal) + acronym.Length)
                    observed += unit.Kana;
                position += unit.Raw.Length;
            }
            Assert.Equal(acronym, observed, "acronym must remain literal inside mixed input");
        }
    }
}
