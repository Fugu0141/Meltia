// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// Raw romaji units must not absorb a word-final English consonant into the
/// first Japanese syllable. Preview segmentation is verified separately.
/// </summary>
internal static class NativeMixedBoundaryRegressionTests
{
    [Test]
    private static void KnownEnglishFinalT_DoesNotJoinFollowingHaOnThirdKey()
    {
        var detector = CompositionDetector.CreateDefault();
        var text = new CompositionText(detector);
        foreach (var c in "commith")
            text.Append(c);

        Assert.True(text.Units.Any(u => u.Raw == "t" && u.Kana == "t"),
            "English final t must remain independent before typing a");
        text.Append('a');

        Assert.Equal("commitha", text.Raw);
        Assert.True(text.Units.Any(u => u.Raw == "t" && u.Kana == "t"),
            "The final t was reabsorbed when the next vowel arrived");
        Assert.True(text.Units.Any(u => u.Raw == "ha" && u.Kana == "は"),
            "The Japanese particle must be a separate phonetic unit");
    }

    [Test]
    private static void OrdinaryJapaneseTha_StillParsesAsOneRomajiUnit()
    {
        var detector = CompositionDetector.CreateDefault();
        var text = new CompositionText(detector);
        foreach (var c in "tha") text.Append(c);
        Assert.Equal("tha", text.Raw);
        Assert.True(!text.Units.Any(u => u.Raw == "t" && u.Kana == "t"),
            "The English-only guard must not trigger for Japanese-only tha");
    }

    [Test]
    private static void UppercaseArtistNameIsPresentInProperNounEvidence()
    {
        var detector = CompositionDetector.CreateDefault();
        Assert.True(detector.ProperNouns.Contains("ado"),
            "Ado should be available as a proper name without forcing lowercase ambiguity");
    }
}
