// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Text.RegularExpressions;

namespace Meltia.Core.Boundary;

/// <summary>
/// Shared UTF-16 safety primitives. The caller remains responsible for
/// input-length limits, policy differences and exception messages.
/// </summary>
internal static class BoundarySpanValidator
{
    public static bool SplitsSurrogatePair(string raw, int offset) =>
        offset > 0 && offset < raw.Length &&
        char.IsHighSurrogate(raw[offset - 1]) && char.IsLowSurrogate(raw[offset]);

    public static bool IsValid(string raw, Utf16Span span) =>
        span.Start >= 0 && span.Start < span.End && span.End <= raw.Length &&
        !SplitsSurrogatePair(raw, span.Start) &&
        !SplitsSurrogatePair(raw, span.End);

    // Exact duplicate spans are harmless. Distinct overlapping locks are not.
    public static bool HasDistinctOverlap(IReadOnlyList<Utf16Span> spans) =>
        spans.Any(x => spans.Any(y => x != y && x.Overlaps(y)));

    public static bool HasCrossOverlap(IReadOnlyList<Utf16Span> left,
        IReadOnlyList<Utf16Span> right) =>
        left.Any(x => right.Any(x.Overlaps));
}

internal enum ProtectionOverlapPolicy
{
    SkipAnyOverlap,
    SkipExactDuplicate
}

/// <summary>
/// Accumulate protected regex matches without changing caller-defined
/// regex options, evaluation order or overlap precedence.
/// </summary>
internal static class ProtectedSpanDetector
{
    public static void AppendMatches(string raw, List<Utf16Span> ranges,
        ProtectionOverlapPolicy policy, params Regex[] patterns)
    {
        foreach (var expression in patterns)
        foreach (Match match in expression.Matches(raw))
        {
            var span = new Utf16Span(match.Index, match.Index + match.Length);
            bool skip = policy == ProtectionOverlapPolicy.SkipAnyOverlap
                ? ranges.Any(x => x.Overlaps(span))
                : ranges.Contains(span);
            if (!skip) ranges.Add(span);
        }
    }
}
