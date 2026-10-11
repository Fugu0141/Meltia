// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Read-only adapter. Never commits text or changes the active Meltype session.
using Meltype.Composition;
using Meltia.Core.Boundary;

namespace Meltia.CompositionBridge;

/// <summary>Diagnostic comparison only. This result cannot be used to commit text.</summary>
public sealed record BoundaryShadowComparison(
    BoundaryResult Meltia,
    IReadOnlyList<Utf16Span> MeltypeEnglish,
    bool TopRouteMatchesMeltype)
{
    public bool AutoCommitAllowed => false;
}

/// <summary>
/// Adapts ACTUAL Meltype.CompositionSegment records to Meltia's provisional
/// boundary evidence without changing Meltype's preview or key handling.
/// All offsets are UTF-16 half-open ranges, including around emoji.
/// </summary>
public static class MeltypeBoundaryShadowBridge
{
    public static BoundaryEvidence FromSegments(
        string raw,
        IReadOnlyList<CompositionSegment> meltypeSegments,
        IReadOnlyList<string>? englishWords = null,
        IReadOnlyList<string>? properNames = null,
        IReadOnlyList<Utf16Span>? explicitEnglish = null,
        IReadOnlyList<Utf16Span>? explicitJapanese = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(meltypeSegments);

        var suggestions = new List<Utf16Span>();
        var protectedSpans = new List<Utf16Span>();
        var offset = 0;

        // The input to the two engines must be *identical*. Otherwise this
        // diagnostic comparison is invalid and must fail, not guess offsets.
        foreach (var segment in meltypeSegments)
        {
            if (segment.Raw is null || segment.Raw.Length > raw.Length - offset ||
                !raw.AsSpan(offset, segment.Raw.Length).SequenceEqual(segment.Raw.AsSpan()))
                throw new ArgumentException("Meltype segments do not match raw text", nameof(meltypeSegments));

            var end = offset + segment.Raw.Length;
            if (!IsUtf16Boundary(raw, offset) || !IsUtf16Boundary(raw, end))
                throw new ArgumentException("Meltype segment splits a surrogate pair", nameof(meltypeSegments));

            if (end > offset)
            {
                var span = new Utf16Span(offset, end);
                if (segment.IsProtected)
                    protectedSpans.Add(span);
                else if (segment.IsEnglish)
                    suggestions.Add(span);
            }
            offset = end;
        }
        if (offset != raw.Length)
            throw new ArgumentException("Meltype segments do not cover raw text", nameof(meltypeSegments));

        return new BoundaryEvidence(
            EnglishWords: englishWords,
            ProperNames: properNames,
            MeltypeEnglish: suggestions,
            ExplicitEnglish: explicitEnglish,
            ExplicitJapanese: explicitJapanese,
            Protected: protectedSpans);
    }

    public static BoundaryShadowComparison CompareSegments(
        string raw,
        IReadOnlyList<CompositionSegment> meltypeSegments,
        MeltiaBoundaryEngine? engine = null,
        IReadOnlyList<string>? englishWords = null,
        IReadOnlyList<string>? properNames = null,
        IReadOnlyList<Utf16Span>? explicitEnglish = null,
        IReadOnlyList<Utf16Span>? explicitJapanese = null)
    {
        var evidence = FromSegments(raw, meltypeSegments, englishWords, properNames,
            explicitEnglish, explicitJapanese);
        var result = (engine ?? new MeltiaBoundaryEngine()).Analyze(raw, evidence);
        // Keep the host's actual English decision separate from the advisory
        // evidence we feed to Meltia. Protected English spans are mandatory and
        // therefore absent from MeltypeEnglish, but MUST be included here when
        // comparing full host output to the best Meltia route.
        var host = new List<Utf16Span>();
        var hostOffset = 0;
        foreach (var segment in meltypeSegments)
        {
            var end = hostOffset + segment.Raw.Length;
            if (segment.IsEnglish && end > hostOffset)
                host.Add(new Utf16Span(hostOffset, end));
            hostOffset = end;
        }
        // An exact ordered comparison: adjacent words, missing spans and
        // larger-than-intended English spans must not be treated as equal.
        var exact = result.Routes.Count > 0 &&
            result.Routes[0].EnglishSpans.SequenceEqual(host);
        return new BoundaryShadowComparison(result, host, exact);
    }

    /// <summary>
    /// Calls the real Meltype detector but never modifies a CompositionText,
    /// CompositionController, TSF session, user memory or conversion result.
    /// The caller supplies its existing units/pending values without consuming keys.
    /// </summary>
    public static BoundaryShadowComparison CompareDetector(
        CompositionDetector detector,
        IReadOnlyList<CompositionUnit> units,
        string pending,
        MeltiaBoundaryEngine? engine = null,
        IReadOnlyList<string>? englishWords = null,
        IReadOnlyList<string>? properNames = null,
        IReadOnlyList<Utf16Span>? explicitEnglish = null,
        IReadOnlyList<Utf16Span>? explicitJapanese = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(pending);
        var raw = string.Concat(units.Select(u => u.Raw)) + pending;
        var segments = detector.Segment(units, pending);
        return CompareSegments(raw, segments, engine, englishWords, properNames,
            explicitEnglish, explicitJapanese);
    }

    private static bool IsUtf16Boundary(string raw, int index) =>
        index <= 0 || index >= raw.Length ||
        !(char.IsHighSurrogate(raw[index - 1]) && char.IsLowSurrogate(raw[index]));
}
