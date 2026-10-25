// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>Issue #100: dotted code names must survive the real keyboard composition path.</summary>
internal static class MeltiaIssue100Tests
{
    [Test]
    public static void DottedCodeNames_KeepRawInPreviewAndEnterCommit()
    {
        // In particular, lower-case "node" is also readable as Japanese "ので".
        // The suffix provides additional evidence that the entire identifier is Latin.
        foreach (var raw in new[] { "node.js", "Node.js", "react.js", "node.ts", "node.json" })
        {
            var keyboard = new CompositionTests.Keyboard { SigilWords = false };
            keyboard.Type(raw);
            Assert.Equal(raw, keyboard.Showing, "preedit: " + raw);
            keyboard.Type("\n");
            Assert.Equal(raw, keyboard.Host.Document, "Enter commit: " + raw);
        }
    }

    [Test]
    public static void DottedCodeNames_RawUtf16AndSegmentCoverageArePreserved()
    {
        var detector = CompositionDetector.CreateDefault();
        foreach (var raw in new[] { "node.js", "Node.js", "react.js", "node.ts" })
        {
            var composition = new CompositionText(detector);
            foreach (var key in raw) composition.Append(key);
            Assert.Equal(raw, composition.Raw, "raw: " + raw);
            Assert.Equal(raw, composition.Display(final: false), "live display: " + raw);
            Assert.Equal(raw, composition.Display(final: true), "final display: " + raw);
            Assert.Equal(raw, string.Concat(composition.Segments(final: true).Select(s => s.Raw)), "coverage: " + raw);
            Assert.True(composition.Segments(final: true).All(s => s.IsEnglish), "English segments: " + raw);
        }
    }

    [Test]
    public static void DottedCodeNames_DotAloneDoesNotForceEnglish()
    {
        // The stem before "." is ambiguous, so do not change an unrelated Japanese
        // sentence or convert every occurrence of "node" into an English word.
        var japanese = new CompositionText(CompositionDetector.CreateDefault());
        foreach (var key in "kyouha.") japanese.Append(key);
        Assert.Equal("きょうは。", japanese.Display(final: true));
    }
}
