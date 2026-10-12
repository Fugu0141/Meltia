// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// Cross-platform contract for the *real* composition call site reused by
/// Meltia.Windows. Original Meltype callers never supply this delegate.
/// </summary>
internal static class MeltiaBoundaryRefinementTests
{
    private static CompositionUnit[] JapaneseUnits() => [
        new("に", "ni"), new("ほ", "ho"),
        new("ん", "n"), new("ご", "go")
    ];

    [Test]
    private static void DefaultAndNullHookPreserveMeltype()
    {
        var detector = CompositionDetector.CreateDefault();
        var units = JapaneseUnits();
        var expected = detector.Segment(units, "");
        detector.BoundaryRefinement = (_, _, _) => null;
        Assert.True(expected.SequenceEqual(detector.Segment(units, "")),
            "Null Meltia hook changed legacy Meltype Japanese preview");
    }

    [Test]
    private static void HookExceptionFallsBackToMeltype()
    {
        var detector = CompositionDetector.CreateDefault();
        var units = JapaneseUnits();
        var expected = detector.Segment(units, "");
        detector.BoundaryRefinement = (_, _, _) =>
            throw new InvalidOperationException("test-only boundary failure");
        Assert.True(expected.SequenceEqual(detector.Segment(units, "")),
            "Meltia error leaked into Meltype's composition route");
    }

    [Test]
    private static void HookCanRefineActualPreviewOnlyWhenRequested()
    {
        var detector = CompositionDetector.CreateDefault();
        var units = JapaneseUnits();
        int calls = 0;
        detector.BoundaryRefinement = (source, pending, legacy) =>
        {
            calls++;
            Assert.Equal("nihongo", string.Concat(source.Select(x => x.Raw)) + pending);
            Assert.True(legacy.Count > 0, "Meltype preview must be computed first");
            return [new CompositionSegment(true, "", "nihongo")];
        };
        var updated = detector.Segment(units, "", level: DetectionLevel.Balanced);
        Assert.True(calls > 0 && updated.Count == 1 &&
            updated[0].IsEnglish && updated[0].Raw == "nihongo",
            "Meltia hook did not reach actual Meltype composition output");
        int prior = calls;
        detector.Segment(units, "", level: DetectionLevel.Manual);
        Assert.Equal(prior, calls, "Manual user intent must skip experimental refinement");
    }
}
