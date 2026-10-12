// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltia.Core.Boundary;

/// <summary>
/// v1.0 review of the existing Top-K lattice. "Stable" means agreement
/// among the *enumerated* competitive routes, not correctness, confidence,
/// or permission to commit user text. No probability is estimated here.
/// </summary>
public sealed record BoundaryReview(
    IReadOnlyList<Utf16Span> StableEnglishSpans,
    IReadOnlyList<Utf16Span> ContestedEnglishSpans,
    IReadOnlyList<Utf16Span> UnresolvedReadableEnglish,
    double? TopTwoScoreGap,
    int CompetitiveRouteCount,
    bool NeedsConfirmation,
    string Reason);

internal static class MeltiaUncertaintyReview
{
    // This scale is in the existing uncalibrated route-score units.
    // It is NOT a statistical confidence interval.
    private const double NearTieScoreDifference = 0.75;

    public static BoundaryReview Create(
        string raw, IReadOnlyList<BoundaryRoute> routes,
        IReadOnlyList<Utf16Span> mandatory,
        BoundaryEvidence evidence)
    {
        if (routes.Count == 0)
            throw new ArgumentException("The boundary lattice has no routes", nameof(routes));

        var best = routes[0];
        var near = routes.TakeWhile(r =>
            best.Score - r.Score <= NearTieScoreDifference + 1e-9).ToArray();
        var stable = best.EnglishSpans.Where(span =>
            mandatory.Contains(span) ||
            near.All(route => route.EnglishSpans.Contains(span))).ToArray();
        var contested = best.EnglishSpans.Where(span =>
            !stable.Contains(span)).ToArray();

        // A Latin spelling that is *also* valid Japanese romaji cannot be
        // selected as English from spelling alone. Only explicitly supplied
        // English lexicon/proper-name evidence records this unresolved intent.
        // This does not guess English from an unrelated string or a global
        // "this word might be English" dictionary.
        var ambiguousWords = (evidence.EnglishWords ?? [])
            .Concat(evidence.ProperNames ?? [])
            .Where(w => w is { Length: >= 2 and <= 72 })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ambiguousSpans = new List<Utf16Span>();
        foreach (var token in ambiguousWords)
        {
            if (!token.All(char.IsAsciiLetter) || !MeltiaRomaji.CanRead(token))
                continue;
            int index = 0;
            while (index < raw.Length)
            {
                int start = raw.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                index = start + 1;
                var span = new Utf16Span(start, start + token.Length);
                if (!mandatory.Any(x => x.Overlaps(span)) &&
                    !best.EnglishSpans.Any(x => x.Overlaps(span)) &&
                    !ambiguousSpans.Contains(span))
                    ambiguousSpans.Add(span);
            }
        }
        ambiguousSpans.Sort((a, b) =>
            a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        var gap = routes.Count >= 2
            ? best.Score - routes[1].Score : (double?)null;
        var needsReview = contested.Length > 0 || ambiguousSpans.Count > 0;
        var reason = contested.Length > 0
            ? "competing-route-boundaries"
            : ambiguousSpans.Count > 0
                ? "romanized-japanese-english-intent-unresolved"
                : "no-nearby-competing-boundary";

        return new BoundaryReview(
            stable, contested, ambiguousSpans, gap,
            near.Length, needsReview, reason);
    }
}
