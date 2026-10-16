// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meltia.Core.Boundary;

// An exact, reviewable characterization of the existing H11 implementation.
// This file is a test-only observer: no hints, labels or scores enter Analyze.
internal sealed record H11CaseSnapshot(
    string Corpus, string Id, string CorpusSha256, string RawSha256,
    string CandidateAndRouteSha256, string ScoresSha256,
    string Top1, string[] TopK, int EdgeCount,
    int EnglishCandidateCount, bool NeedsConfirmation, bool AutoCommitAllowed);

internal static class H11Snapshot
{
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Spans(IReadOnlyList<Utf16Span> spans) =>
        string.Join(";", spans.Select(s => $"{s.Start}:{s.End}"));

    // We serialize numeric scores invariantly, without rounding away changes.
    // The relative order and the scoring bits are frozen separately from the
    // boundary decisions so a new platform's float differences are visible.
    internal static H11CaseSnapshot Capture(
        string corpus, string id, string corpusSha, H11BoundaryPrototype.Analysis result)
    {
        var structure = new StringBuilder();
        var scores = new StringBuilder();
        foreach (var edge in result.Edges)
        {
            structure.Append(edge.Span.Start).Append(':').Append(edge.Span.End)
                .Append('|').Append(edge.Language).Append('|').Append(edge.Origin)
                .Append('\n');
            scores.Append(edge.LocalScore.ToString("R", CultureInfo.InvariantCulture))
                .Append('\n');
        }
        structure.Append("ROUTES\n");
        scores.Append("ROUTES\n");
        foreach (var route in result.Routes)
        {
            structure.Append(Spans(route.EnglishSpans)).Append('\n');
            scores.Append(route.Score.ToString("R", CultureInfo.InvariantCulture))
                .Append('\n');
        }
        structure.Append("CONFIRM:").Append(result.NeedsConfirmation)
            .Append(";COMMIT:").Append(result.AutoCommitAllowed);
        return new H11CaseSnapshot(
            corpus, id, corpusSha, Hash(result.Raw),
            Hash(structure.ToString()), Hash(scores.ToString()),
            Spans(result.Routes[0].EnglishSpans),
            result.Routes.Select(x => Spans(x.EnglishSpans)).ToArray(),
            result.Edges.Count, result.Edges.Count(x => x.Language == "English"),
            result.NeedsConfirmation, result.AutoCommitAllowed);
    }

    internal static string CorpusSha(string path) =>
        Hash(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));

    internal static void Verify(string path, string corpus, IReadOnlyList<H11CaseSnapshot> actual)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Frozen H11 baseline absent", path);
        var all = File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => JsonSerializer.Deserialize<H11CaseSnapshot>(x)
                ?? throw new InvalidDataException("Null frozen baseline row")).ToArray();
        if (all.Any(x => string.IsNullOrWhiteSpace(x.Corpus) ||
            string.IsNullOrWhiteSpace(x.Id) ||
            string.IsNullOrWhiteSpace(x.CandidateAndRouteSha256) ||
            string.IsNullOrWhiteSpace(x.ScoresSha256)) ||
            all.Select(x => (x.Corpus, x.Id)).Distinct().Count() != all.Length)
            throw new InvalidDataException("Invalid or duplicated frozen baseline rows");

        var expected = all.Where(x => x.Corpus == corpus).ToDictionary(x => x.Id);
        var observed = actual.ToDictionary(x => x.Id);
        if (expected.Count != observed.Count ||
            expected.Keys.Except(observed.Keys).Any() || observed.Keys.Except(expected.Keys).Any())
            throw new InvalidDataException(
                $"H11 baseline corpus IDs changed: {corpus}; expected {expected.Count}, actual {observed.Count}");

        foreach (var row in actual)
        {
            var old = expected[row.Id];
            if (old.CorpusSha256 != row.CorpusSha256 ||
                old.RawSha256 != row.RawSha256 ||
                old.CandidateAndRouteSha256 != row.CandidateAndRouteSha256 ||
                old.ScoresSha256 != row.ScoresSha256 ||
                old.Top1 != row.Top1 ||
                !old.TopK.SequenceEqual(row.TopK) ||
                old.EdgeCount != row.EdgeCount ||
                old.EnglishCandidateCount != row.EnglishCandidateCount ||
                old.NeedsConfirmation != row.NeedsConfirmation ||
                old.AutoCommitAllowed != row.AutoCommitAllowed)
                throw new InvalidDataException(
                    $"H11 regression {corpus}/{row.Id}: old Top1=[{old.Top1}], current=[{row.Top1}]; " +
                    $"old graph={old.CandidateAndRouteSha256}, current graph={row.CandidateAndRouteSha256}; " +
                    $"old scores={old.ScoresSha256}, current scores={row.ScoresSha256}. " +
                    "Investigate the change; do not refresh the frozen baseline for a refactor.");
        }
        Console.WriteLine($"H11_FROZEN_BASELINE_PASS {corpus}: {actual.Count} exact case fingerprints");
    }

    internal static void Emit(IEnumerable<H11CaseSnapshot> rows)
    {
        foreach (var row in rows.OrderBy(x => x.Corpus, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal))
            Console.WriteLine("H11_SNAPSHOT " + JsonSerializer.Serialize(row));
    }
}
