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

internal sealed record H11BaselineMetadata(
    string Schema, string DictionaryResource, string DictionarySha256,
    int CaseCount, Dictionary<string, int> CorpusCounts);

internal static class H11Snapshot
{
    // The corpus names and counts are deliberately NOT inferred from JSONL.
    // Unexpected rows must fail, including rows for another corpus.
    private static readonly Dictionary<string, int> FrozenCorpusCounts = new(StringComparer.Ordinal)
    {
        ["issue_intents_v2.jsonl"] = 47,
        ["issue_intents_extra_20261014.jsonl"] = 56
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(x => "0123456789abcdef".Contains(x));

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

    internal static string EmbeddedDictionarySha()
    {
        using var stream = typeof(H11BoundaryPrototype).Assembly
            .GetManifestResourceStream("Meltia.H11EnglishWords")
            ?? throw new InvalidOperationException("H11 embedded English corpus missing");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static H11CaseSnapshot[] ReadRows(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Frozen H11 baseline absent", path);
        var all = File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => JsonSerializer.Deserialize<H11CaseSnapshot>(x)
                ?? throw new InvalidDataException("Null frozen baseline row")).ToArray();
        if (all.Length != 103 ||
            all.Any(x => string.IsNullOrWhiteSpace(x.Corpus) ||
                !FrozenCorpusCounts.ContainsKey(x.Corpus) ||
                string.IsNullOrWhiteSpace(x.Id) ||
                !IsSha256(x.CorpusSha256) || !IsSha256(x.RawSha256) ||
                !IsSha256(x.CandidateAndRouteSha256) || !IsSha256(x.ScoresSha256) ||
                x.Top1 is null || x.TopK is null || x.TopK.Length is < 1 or > 8 ||
                x.TopK.Any(y => y is null) ||
                x.TopK[0] != x.Top1 ||
                x.EdgeCount < 0 || x.EnglishCandidateCount < 0 ||
                x.EnglishCandidateCount > x.EdgeCount) ||
            all.Select(x => (x.Corpus, x.Id)).Distinct().Count() != all.Length ||
            FrozenCorpusCounts.Any(x => all.Count(row => row.Corpus == x.Key) != x.Value) ||
            all.GroupBy(x => x.Corpus).Any(g =>
                g.Select(x => x.CorpusSha256).Distinct().Count() != 1))
            throw new InvalidDataException(
                "Frozen H11 baseline must contain exactly 47+56 distinct, valid rows");
        return all;
    }

    private static void VerifyMetadata(string baselinePath)
    {
        string metadataPath = Path.ChangeExtension(baselinePath, ".meta.json")!;
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException("Frozen H11 dictionary metadata absent", metadataPath);
        var metadata = JsonSerializer.Deserialize<H11BaselineMetadata>(
            File.ReadAllText(metadataPath)) ??
            throw new InvalidDataException("Null frozen H11 metadata");
        if (metadata.Schema != "meltia-h11-baseline-metadata-v1" ||
            metadata.DictionaryResource != "Meltia.H11EnglishWords" ||
            !IsSha256(metadata.DictionarySha256) ||
            metadata.CaseCount != 103 ||
            metadata.CorpusCounts is null ||
            metadata.CorpusCounts.Count != FrozenCorpusCounts.Count ||
            FrozenCorpusCounts.Any(x =>
                !metadata.CorpusCounts.TryGetValue(x.Key, out int count) ||
                count != x.Value))
            throw new InvalidDataException("Invalid frozen H11 baseline metadata");
        string actualDictionarySha = EmbeddedDictionarySha();
        if (metadata.DictionarySha256 != actualDictionarySha)
            throw new InvalidDataException(
                $"H11 embedded dictionary SHA-256 changed: baseline={metadata.DictionarySha256}, " +
                $"current={actualDictionarySha}. Explain and review dictionary updates explicitly.");
    }

    internal static void Verify(
        string path, string corpus, IReadOnlyList<H11CaseSnapshot> actual,
        bool emitSuccess = true)
    {
        // Every invocation validates the entire 103-case file and metadata,
        // not only rows for the caller's corpus.
        var all = ReadRows(path);
        VerifyMetadata(path);
        if (!FrozenCorpusCounts.TryGetValue(corpus, out int corpusSize))
            throw new InvalidDataException("Unknown frozen H11 corpus: " + corpus);
        var expected = all.Where(x => x.Corpus == corpus).ToDictionary(x => x.Id);
        if (actual.Count != corpusSize || actual.Any(x => x.Corpus != corpus) ||
            actual.Select(x => x.Id).Distinct().Count() != actual.Count)
            throw new InvalidDataException("Invalid H11 observed rows: " + corpus);
        var observed = actual.ToDictionary(x => x.Id);
        if (expected.Count != observed.Count ||
            expected.Keys.Except(observed.Keys).Any() ||
            observed.Keys.Except(expected.Keys).Any())
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
                row.TopK is null || !old.TopK.SequenceEqual(row.TopK) ||
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
        if (emitSuccess)
            Console.WriteLine($"H11_FROZEN_BASELINE_PASS {corpus}: {actual.Count} exact case fingerprints");
    }

    // Verifier regression tests are independent of Analyze: mutate frozen
    // fixture values one at a time and assert that Verify rejects each.
    internal static void ValidatorSelfTest(string path)
    {
        var all = ReadRows(path);
        VerifyMetadata(path);
        const string corpus = "issue_intents_extra_20261014.jsonl";
        var good = all.Where(x => x.Corpus == corpus).ToArray();
        Verify(path, corpus, good, emitSuccess: false);

        static void MustReject(Action run, string scenario)
        {
            try { run(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException(
                "H11 validator accepted corrupted baseline: " + scenario);
        }

        void Mutate(string scenario, Func<H11CaseSnapshot, H11CaseSnapshot> change)
        {
            var changed = good.ToArray();
            changed[0] = change(changed[0]);
            MustReject(() => Verify(path, corpus, changed, emitSuccess: false), scenario);
        }

        Mutate("candidate graph", x => x with
            { CandidateAndRouteSha256 = new string('0', 64) });
        Mutate("scores", x => x with { ScoresSha256 = new string('0', 64) });
        Mutate("raw", x => x with { RawSha256 = new string('0', 64) });
        Mutate("corpus hash", x => x with { CorpusSha256 = new string('0', 64) });
        Mutate("Top1", x => x with { Top1 = x.Top1 + ";999:1000" });
        Mutate("Top-K ordering", x => x with { TopK = x.TopK.Reverse().ToArray() });
        Mutate("candidate count", x => x with { EdgeCount = x.EdgeCount + 1 });
        Mutate("English count", x => x with
            { EnglishCandidateCount = x.EnglishCandidateCount + 1 });
        Mutate("confirmation", x => x with
            { NeedsConfirmation = !x.NeedsConfirmation });
        Mutate("auto commit", x => x with
            { AutoCommitAllowed = !x.AutoCommitAllowed });

        // Mutations to the snapshot file itself must also be rejected, even
        // if they are outside the corpus currently being verified.
        var temp = Directory.CreateTempSubdirectory("meltia-h11-validator-");
        try
        {
            string badPath = Path.Combine(temp.FullName, "test.jsonl");
            string badMetadata = Path.ChangeExtension(badPath, ".meta.json")!;
            File.Copy(Path.ChangeExtension(path, ".meta.json")!, badMetadata);

            void CheckRows(string scenario, IEnumerable<H11CaseSnapshot> rows)
            {
                File.WriteAllLines(badPath, rows.Select(x => JsonSerializer.Serialize(x)));
                MustReject(() => Verify(badPath, corpus, good, emitSuccess: false), scenario);
            }

            CheckRows("missing row", all.Skip(1));
            CheckRows("duplicated row", all.Append(all[0]));
            CheckRows("extra corpus row", all.Append(all[0] with
                { Corpus = "unexpected_corpus.jsonl", Id = "unexpected-row" }));
            CheckRows("renamed corpus", all.Select((x, i) => i == 0
                ? x with { Corpus = "unexpected_corpus.jsonl" } : x));

            // Restore the 103 lines and test dictionary version tampering.
            File.WriteAllLines(badPath, all.Select(x => JsonSerializer.Serialize(x)));
            var originalMetadata = JsonSerializer.Deserialize<H11BaselineMetadata>(
                File.ReadAllText(badMetadata))!;
            File.WriteAllText(badMetadata, JsonSerializer.Serialize(originalMetadata with
                { DictionarySha256 = new string('0', 64) }));
            MustReject(() => Verify(badPath, corpus, good, emitSuccess: false),
                "embedded dictionary hash");
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        Console.WriteLine("H11_VALIDATOR_SELFTEST_PASS: 15 mutation/structure/dictionary rejection checks");
    }

    internal static void Emit(IEnumerable<H11CaseSnapshot> rows)
    {
        foreach (var row in rows.OrderBy(x => x.Corpus, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal))
            Console.WriteLine("H11_SNAPSHOT " + JsonSerializer.Serialize(row));
    }
}
