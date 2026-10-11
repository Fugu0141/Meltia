// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltia.Core.Boundary;

/// <summary>
/// Meltia v0.7 research: bounded, dictionary-independent *proposal* generator.
/// It does not infer user intent or alter the current Japanese/English route.
/// A romaji-readable stream is a strong negative control; no English spans
/// are generated solely from substring spelling in such a stream.
/// </summary>
internal static class MeltiaUnknownLatinCandidates
{
    private const int MinLatinLength = 5;
    private const int MaxLatinLength = 24;
    private const int MaxScanRun = 96;

    // Only seam hints, not "known English words". These are common Japanese
    // particles and inflections in romanized input; collisions are expected.
    private static readonly string[] LeftSeams =
        ["ha", "ga", "de", "wo", "ni", "no", "to", "shita", "suru", "desu"];
    private static readonly string[] RightSeams =
        ["ga", "de", "wo", "ni", "no", "to", "ha", "suru", "shita", "desu"];

    public static IEnumerable<BoundaryCandidate> Generate(string raw)
    {
        foreach (System.Text.RegularExpressions.Match run in
            System.Text.RegularExpressions.Regex.Matches(raw, "[A-Za-z]+"))
        {
            // Explicit complexity limit for one contiguous ASCII segment.
            // Longer streams remain reversible and keep their existing paths.
            if (run.Length < MinLatinLength || run.Length > MaxScanRun) continue;

            var chunk = raw.Substring(run.Index, run.Length);
            // This prevents speculative Latin edges from fragmenting ordinary
            // phonetic Japanese even when a substring looks English-like.
            if (MeltiaRomaji.CanRead(chunk)) continue;

            var leftReadable = new bool[chunk.Length + 1];
            var rightReadable = new bool[chunk.Length + 1];
            var leftSeam = new bool[chunk.Length + 1];
            var rightSeam = new bool[chunk.Length + 1];
            leftReadable[0] = true;
            rightReadable[chunk.Length] = true;

            for (var i = 1; i <= chunk.Length; i++)
            {
                leftReadable[i] = MeltiaRomaji.CanRead(chunk[..i]);
                leftSeam[i] = LeftSeams.Any(p =>
                    chunk[..i].EndsWith(p, StringComparison.OrdinalIgnoreCase));
            }
            for (var i = 0; i < chunk.Length; i++)
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

                    // Keep these edges below the independent experimental
                    // 4.5/span routing cost. Detection != high-confidence
                    // commitment to an English interpretation.
                    double score = 0.5;
                    if (start > 0 && leftReadable[start]) score += 0.65;
                    if (end < chunk.Length && rightReadable[end]) score += 0.65;
                    if (start > 0 && leftSeam[start]) score += 0.35;
                    if (end < chunk.Length && rightSeam[end]) score += 0.35;
                    score += Math.Min(word.Length, 18) * 0.035;
                    score -= Math.Abs(word.Length - 9) * 0.04;

                    yield return new BoundaryCandidate(
                        new Utf16Span(run.Index + start, run.Index + end),
                        "v0.7-unknown-latin-provisional", score);
                }
            }
        }
    }
}
