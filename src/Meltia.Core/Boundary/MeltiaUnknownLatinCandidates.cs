// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// Bounded dictionary-independent *proposals*, never user-intent decisions.
/// The v1.0 phase-2 opt-in uses overlapping 96-character views so an
/// unseparated long Latin stream does not silently lose all unknown-English
/// hypotheses. The <=96-character legacy path is byte-for-byte equivalent.
/// </summary>
internal static class MeltiaUnknownLatinCandidates
{
    private const int MinLatinLength = 5;
    private const int MaxLatinLength = 24;
    private const int MaxScanRun = 96;
    // Any English edge of length <=24 fits completely within at least one
    // 96-letter window because successive windows overlap by >=24.
    private const int LongRunStride = MaxScanRun - MaxLatinLength;

    private static readonly string[] LeftSeams =
        ["ha", "ga", "de", "wo", "ni", "no", "to", "shita", "suru", "desu"];
    private static readonly string[] RightSeams =
        ["ga", "de", "wo", "ni", "no", "to", "ha", "suru", "shita", "desu"];

    public static IEnumerable<BoundaryCandidate> Generate(
        string raw, bool experimentalLongRunWindowing = false)
    {
        foreach (System.Text.RegularExpressions.Match run in
            System.Text.RegularExpressions.Regex.Matches(raw, "[A-Za-z]+"))
        {
            if (run.Length < MinLatinLength) continue;
            if (run.Length <= MaxScanRun)
            {
                foreach (var candidate in GenerateWindow(run.Value, run.Index))
                    yield return candidate;
                continue;
            }
            if (!experimentalLongRunWindowing) continue;

            // The *whole* original run, not an arbitrary window, must fail
            // phonetic Japanese. This preserves all-romaji long negatives.
            if (MeltiaRomaji.CanRead(run.Value)) continue;

            // A span may be hypothesized in two overlapping windows with
            // different nearby Japanese evidence. Do not make the result
            // depend on which arbitrary window happened to be visited first.
            // Keep the strongest *provisional* explanation for each exact
            // absolute span; this does not constitute user intent.
            var bestBySpan = new Dictionary<Utf16Span, BoundaryCandidate>();
            int previousStart = -1;
            for (int offset = 0; offset < run.Length; offset += LongRunStride)
            {
                int start = Math.Min(offset, run.Length - MaxScanRun);
                if (start == previousStart) break;
                previousStart = start;
                foreach (var candidate in GenerateWindow(
                    raw.Substring(run.Index + start, MaxScanRun),
                    run.Index + start))
                    if (!bestBySpan.TryGetValue(candidate.Span, out var old) ||
                        candidate.Score > old.Score)
                        bestBySpan[candidate.Span] = candidate;
            }
            foreach (var candidate in bestBySpan.Values
                .OrderBy(x => x.Span.Start).ThenBy(x => x.Span.End))
                yield return candidate;
        }
    }

    private static IEnumerable<BoundaryCandidate> GenerateWindow(
        string chunk, int absoluteStart)
    {
        if (MeltiaRomaji.CanRead(chunk)) yield break;

        var leftReadable = new bool[chunk.Length + 1];
        var rightReadable = new bool[chunk.Length + 1];
        var leftSeam = new bool[chunk.Length + 1];
        var rightSeam = new bool[chunk.Length + 1];
        leftReadable[0] = true;
        rightReadable[chunk.Length] = true;

        for (int i = 1; i <= chunk.Length; i++)
        {
            leftReadable[i] = MeltiaRomaji.CanRead(chunk[..i]);
            leftSeam[i] = LeftSeams.Any(p =>
                chunk[..i].EndsWith(p, StringComparison.OrdinalIgnoreCase));
        }
        for (int i = 0; i < chunk.Length; i++)
        {
            rightReadable[i] = MeltiaRomaji.CanRead(chunk[i..]);
            rightSeam[i] = RightSeams.Any(p =>
                chunk[i..].StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        for (var start = 0; start + MinLatinLength <= chunk.Length; start++)
        {
            if (!leftReadable[start] && !leftSeam[start]) continue;
            for (var end = start + MinLatinLength;
                 end <= Math.Min(chunk.Length, start + MaxLatinLength); end++)
            {
                if (!rightReadable[end] && !rightSeam[end]) continue;
                var word = chunk.Substring(start, end - start);
                if (MeltiaRomaji.CanRead(word)) continue;

                // Keep inherited v0.7 scores unchanged, preserving the
                // <=96-character experimental baseline exactly.
                double score = 0.5;
                if (start > 0 && leftReadable[start]) score += 0.65;
                if (end < chunk.Length && rightReadable[end]) score += 0.65;
                if (start > 0 && leftSeam[start]) score += 0.35;
                if (end < chunk.Length && rightSeam[end]) score += 0.35;
                score += Math.Min(word.Length, 18) * 0.035;
                score -= Math.Abs(word.Length - 9) * 0.04;

                yield return new BoundaryCandidate(
                    new Utf16Span(absoluteStart + start, absoluteStart + end),
                    "v0.7-unknown-latin-provisional", score);
            }
        }
    }
}
