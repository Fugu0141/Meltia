// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Reflection;
using System.Text.RegularExpressions;

namespace Meltia.Core.Boundary;

/// <summary>
/// H11 offline word/phoneme joint-segmentation experiment.
/// Unlike H10, whole English lexical spans compete against Japanese phonetic
/// runs in one Viterbi search. It never writes to the IME, TSF or host.
/// The lexicon is the PRE-EXISTING Meltype English dictionary, not Issue gold.
/// </summary>
public sealed partial class H11BoundaryPrototype
{
    public const int MaxInputLength = H10BoundaryPrototype.MaxInputLength;
    // Bound cubic phonetic work: long inputs compose adjacent segments.
    private const int MaxEdge = 26;
    private const int Width = 16;
    private static readonly Regex Url = new(@"https?://[^\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"(?<![\w.@])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Structure = new(
        @"(?<![\p{L}\p{N}_])(?:\.[A-Za-z][A-Za-z0-9_-]+|[A-Za-z][A-Za-z0-9_-]*\.[A-Za-z][A-Za-z0-9_-]+|[A-Za-z]+[0-9]+[A-Za-z0-9]*|[A-Za-z]+-[A-Za-z]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Lexicon = LoadLexicon();
    private static readonly H10CharacterEvidence Model = new();
    private static readonly string[] JapaneseSeams =
        ["ga", "de", "no", "wo", "ni", "to", "ya", "ha", "suru",
         "shita", "sareta", "noni", "kara", "made"];

    // Alternate Latin-key layouts are evidence for Japanese, not converted
    // strings. Keep the original raw and offsets at every decoding stage.
    private static bool RomajiReadable(string value) =>
        RomajiReadable(value.AsSpan());

    private static bool RomajiReadable(ReadOnlySpan<char> value)
    {
        if (MeltiaRomaji.CanRead(value)) return true;
        foreach (char c in value)
            if (!char.IsAsciiLetter(c)) return false;

        // Keep the exact alternate-input rewrite order and case semantics.
        // Only the slow fallback creates a normalized string.
        var normalized = value.ToString().ToLowerInvariant()
            .Replace("cye", "che", StringComparison.Ordinal)
            .Replace("cyu", "chu", StringComparison.Ordinal)
            .Replace("cyo", "cho", StringComparison.Ordinal)
            .Replace("ci", "shi", StringComparison.Ordinal);
        return !normalized.AsSpan().Equals(value, StringComparison.OrdinalIgnoreCase) &&
            MeltiaRomaji.CanRead(normalized.AsSpan());
    }
    private static bool Consonant(char letter) =>
        char.IsAsciiLetter(letter) && "aeiou".IndexOf(char.ToLowerInvariant(letter)) < 0;

    // A single nonstandard romaji keystroke should not force the whole
    // Japanese reading into English. This is a costed *alternate FST path*,
    // never a rewrite of the original keystrokes.
    private static int RomajiFallbackCost(string word)
    {
        var costs = Enumerable.Repeat(99, word.Length + 1).ToArray();
        costs[0] = 0;
        for (int i = 0; i < word.Length; i++)
        {
            if (costs[i] == 99) continue;
            costs[i + 1] = Math.Min(costs[i + 1], costs[i] + 1);
            for (int n = 1; n <= 4 && i + n <= word.Length; n++)
                if (RomajiReadable(word.AsSpan(i, n)))
                    costs[i + n] = Math.Min(costs[i + n], costs[i]);
        }
        return costs[^1];
    }

    internal enum Lang { Ja, En, Neutral }
    internal sealed record Edge(Utf16Span Span, Lang Lang, double Score, string Origin);
    internal sealed record Node(double Score, Lang Last, Edge? Edge, Node? Prev);
    public sealed record Analysis(string Raw, IReadOnlyList<H10BoundaryPrototype.Edge> Edges,
        IReadOnlyList<BoundaryRoute> Routes, bool NeedsConfirmation)
    {
        public bool AutoCommitAllowed => false;
    }

    private static HashSet<string> LoadLexicon()
    {
        using var stream = typeof(H11BoundaryPrototype).Assembly
            .GetManifestResourceStream("Meltia.H11EnglishWords")
            ?? throw new InvalidOperationException("Missing bundled baseline English corpus");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var clean = line.Split('#', 2)[0];
            foreach (string w in clean.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (w.Length <= MaxEdge && w.All(char.IsAsciiLetter))
                    set.Add(w.ToLowerInvariant());
        }
        return set;
    }
    private static bool PairSplit(string raw, int offset) =>
        BoundarySpanValidator.SplitsSurrogatePair(raw, offset);

    public Analysis Analyze(string raw, BoundaryEvidence? evidence = null) =>
        AnalyzeCore(raw, evidence, diagnostics: null);

    private Analysis AnalyzeCore(string raw, BoundaryEvidence? evidence,
        TraceCollector? diagnostics, bool useForwardDecoder = false,
        bool useRouteProjector = false)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > MaxInputLength) throw new ArgumentOutOfRangeException(nameof(raw));
        evidence ??= new BoundaryEvidence();
        var explicitJa = (evidence.ExplicitJapanese ?? []).Distinct().ToArray();
        var hard = (evidence.ExplicitEnglish ?? []).Concat(evidence.Protected ?? [])
            .Distinct().ToList();
        // Preserve H11's URL-first policy: nested email matches must not
        // replace a longer already-protected span.
        ProtectedSpanDetector.AppendMatches(raw, hard,
            ProtectionOverlapPolicy.SkipAnyOverlap, Url, Email);
        foreach (var x in hard.Concat(explicitJa))
            if (!BoundarySpanValidator.IsValid(raw, x))
                throw new ArgumentException("Invalid UTF-16 language lock");
        if (BoundarySpanValidator.HasDistinctOverlap(hard) ||
            BoundarySpanValidator.HasCrossOverlap(hard, explicitJa) ||
            BoundarySpanValidator.HasDistinctOverlap(explicitJa))
            throw new ArgumentException("Contradictory language locks");

        // E2: one model-owned candidate builder for the legacy oracle and
        // experimental forward paths; preserve the original buckets and order.
        var graph = new H11CandidateGenerator().Generate(
            new H11GenerationRequest(raw, hard, explicitJa, diagnostics));

        List<Node>[,] dp;
        if (useForwardDecoder)
        {
            // Phase 4/E1: explicit test-only challenger. The same candidates,
            // scorer arithmetic, beam policy and output projection are used.
            dp = ForwardLatticeDecoder.Decode(raw.Length, 3, (int)Lang.Neutral,
                new Node(0, Lang.Neutral, null, null),
                graph, new H11ForwardTransition(),
                new H11OrderedBeam(diagnostics));
        }
        else
        {
            dp = new List<Node>[raw.Length + 1, 3];
            for (int i = 0; i <= raw.Length; i++)
                for (int lang = 0; lang < 3; lang++) dp[i, lang] = [];
            dp[0, (int)Lang.Neutral].Add(new Node(0, Lang.Neutral, null, null));
            for (int i = 0; i < raw.Length; i++)
            {
                foreach (Lang stateLang in Enum.GetValues<Lang>())
                // Every legal edge moves forward (End > i), so no transition
                // can append to the list currently being iterated. Avoid the
                // per-state snapshot allocation without changing visit order.
                foreach (var previous in dp[i, (int)stateLang])
                foreach (var candidate in graph.At(i))
                {
                    bool swap = previous.Last != Lang.Neutral &&
                        candidate.Lang != Lang.Neutral && previous.Last != candidate.Lang;
                    var last = candidate.Lang == Lang.Neutral ? previous.Last : candidate.Lang;
                    double transition = swap ? -1.6 : 0;
                    // Close a lexical English island before a phonetic Japanese
                    // suffix; do not give a generic reward to all English seams.
                    if (candidate.Lang == Lang.Ja && swap && previous.Edge is { } prior &&
                        prior.Lang == Lang.En && prior.Origin == "baseline-lexicon-word" &&
                        candidate.Span.Length >= 2) transition += 0.9;
                    var next = new Node(previous.Score + candidate.Score + transition,
                        last, candidate, previous);
                    var destination = dp[candidate.Span.End, (int)last];
                    destination.Add(next);
                    if (destination.Count > Width * 2)
                    {
                        // The D2 small-beam selector regressed full Analyze on long
                        // workloads; keep the legacy ordering for every trace mode.
                        destination.Sort((a, b) => b.Score.CompareTo(a.Score));
                        diagnostics?.Drop(candidate.Span.End, last, destination.Skip(Width),
                            destination[Width - 1].Score);
                        destination.RemoveRange(Width, destination.Count - Width);
                    }
                }
            }
        }
        // E2 is opt-in: the legacy projection remains an independent oracle.
        if (useRouteProjector)
            return new H11RouteProjector(diagnostics).Project(raw, dp, graph);

        var finalists = Enum.GetValues<Lang>()
            .SelectMany(lang => dp[raw.Length, (int)lang])
            .OrderByDescending(x => x.Score).Take(Width * 3).ToArray();
        if (finalists.Length == 0)
            throw new ArgumentException("No legal H11 boundary path");
        var results = new List<BoundaryRoute>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finalist in finalists)
        {
            var stack = new Stack<Edge>();
            for (var node = finalist; node.Edge is not null; node = node.Prev!)
                stack.Push(node.Edge);
            var en = new List<Utf16Span>();
            foreach (var edge in stack)
            {
                if (edge.Lang != Lang.En) continue;
                if (en.Count > 0 && en[^1].End == edge.Span.Start)
                    en[^1] = new Utf16Span(en[^1].Start, edge.Span.End);
                else en.Add(edge.Span);
            }
            string signature = string.Join(";", en.Select(x => $"{x.Start}:{x.End}"));
            if (!seen.Add(signature)) continue;
            results.Add(new BoundaryRoute(en, finalist.Score));
            diagnostics?.Selected(finalist);
            if (results.Count == 8) break;
        }
        diagnostics?.Finalists(Enum.GetValues<Lang>()
            .SelectMany(lang => dp[raw.Length, (int)lang]));
        bool uncertainty = results.Count > 1 &&
            results[0].Score - results[1].Score < 1.5;
        return new Analysis(raw, graph.InStartPositionOrder()
            .Select(x => new H10BoundaryPrototype.Edge(x.Span,
                x.Lang == Lang.En ? "English" :
                x.Lang == Lang.Ja ? "Japanese" : "Neutral",
                x.Origin, x.Score)).ToArray(), results, uncertainty);
    }
    // Candidate generation is model-specific. Its exact original loop body,
    // insertion order, rejection trace and score operations are retained.
    private static List<Edge>[] BuildCandidateEdges(H11GenerationRequest request)
    {
        var raw = request.Raw;
        var hard = request.Hard;
        var explicitJa = request.ExplicitJa;
        var diagnostics = request.Diagnostics;
        var edges = new List<Edge>[raw.Length + 1];
        for (int i = 0; i <= raw.Length; i++) edges[i] = [];
        void Add(Utf16Span x, Lang lang, double score, string origin,
            int? englishRunEnd = null)
        {
            if (!BoundarySpanValidator.IsValid(raw, x))
            {
                diagnostics?.Reject("add-rejected/invalid-utf16-or-empty-span");
                return;
            }
            if (hard.Any(y => y.Overlaps(x) && y != x))
            {
                diagnostics?.Reject("add-rejected/overlap-protected-or-english-lock");
                return;
            }
            if (hard.Contains(x) && lang != Lang.En)
            {
                diagnostics?.Reject("add-rejected/lock-requires-english");
                return;
            }
            if (lang == Lang.En && explicitJa.Any(y => y.Overlaps(x)))
            {
                diagnostics?.Reject("add-rejected/english-in-japanese-lock");
                return;
            }
            var candidate = new Edge(x, lang, score, origin);
            edges[x.Start].Add(candidate);
            diagnostics?.Accept(candidate, englishRunEnd);
        }
        foreach (var x in hard) Add(x, Lang.En, 100, "mandatory");
        foreach (Match m in Structure.Matches(raw))
        {
            var hyphen = m.Value.Split('-', 2);
            if (hyphen.Length == 2 && RomajiReadable(hyphen[0]) &&
                RomajiReadable(hyphen[1]))
            {
                diagnostics?.Reject("generation-skipped/structural-japanese-hyphen");
                continue; // Japanese readings like sha-nashi: punctuation is not English.
            }
            Add(new Utf16Span(m.Index, m.Index + m.Length), Lang.En,
                15.0 + Math.Min(m.Length, 16) * 0.3, "structural");
        }

        // Romanization and spelling scores are cached independently of the
        // path search; unlike H10, no pairwise rerun of the full decoder.
        for (int i = 0; i < raw.Length; i++)
        {
            if (PairSplit(raw, i) || hard.Any(x => i >= x.Start && i < x.End))
                continue;
            char letter = raw[i];
            if (!char.IsAsciiLetter(letter))
            {
                int width = char.IsHighSurrogate(letter) && i + 1 < raw.Length &&
                    char.IsLowSurrogate(raw[i + 1]) ? 2 : 1;
                // Japanese script already present is a language anchor.
                // Non-letter literals are passed through unchanged.
                var kind = char.IsLetter(letter) ? Lang.Ja : Lang.Neutral;
                Add(new Utf16Span(i, i + width), kind,
                    kind == Lang.Ja ? 0.85 : -0.03, "literal");
                continue;
            }
            int endRun = i;
            while (endRun < raw.Length && char.IsAsciiLetter(raw[endRun]) &&
                !hard.Any(x => endRun >= x.Start && endRun < x.End)) endRun++;
            if (i == 0 || !char.IsAsciiLetter(raw[i - 1]))
            {
                // An alternative Japanese path represents a full local typing
                // intent without inventing English inside a near-phonetic word.
                // Only 6-14 lowercase letters with <=1 unsupported romaji
                // keystroke and a plausible Japanese final vowel/n qualify.
                // No Issue-specific word list or output correction is used.
                int size = endRun - i;
                if (size is >= 6 and <= 14)
                {
                    var entire = raw[i..endRun];
                    bool endsLikeJapanese = "aeioun".Contains(entire[^1]);
                    if (entire.All(char.IsLower) && endsLikeJapanese &&
                        !Lexicon.Contains(entire))
                    {
                        int fallbackCost = RomajiFallbackCost(entire);
                        if (fallbackCost <= 1)
                            Add(new Utf16Span(i, endRun), Lang.Ja,
                                0.68 * size + 5.8 - 2.0 * fallbackCost,
                                "robust-full-romaji");
                    }
                }
            }
            Add(new Utf16Span(i, i + 1), Lang.Ja, -2.65, "unfinished-romaji");
            // Fully spelled Japanese syllables must be representable even when
            // an English word inside the same run is an attractive hypothesis.
            for (int end = i + 1; end <= Math.Min(endRun, i + MaxEdge); end++)
            {
                string word = raw[i..end];
                bool readable = RomajiReadable(word);
                double difference = Model.Difference(raw.AsSpan(i, end - i));
                if (readable)
                {
                    // Reward a *complete* Japanese phonetic segment, not each
                    // arbitrary way of partitioning the same syllables.
                    double jaScore = 0.39 * word.Length - 0.62 -
                        difference * 0.5 * Math.Min(word.Length, 12);
                    Add(new Utf16Span(i, end), Lang.Ja, jaScore, "phonetic-run");
                }
                // One-letter English only when the script has a structural
                // reason for it; 'a' and 'I' are word tokens in spaced text.
                bool isolatedOne = word.Length == 1 &&
                    (word.Equals("a", StringComparison.OrdinalIgnoreCase) ||
                     word == "I") &&
                    i > 0 && char.IsWhiteSpace(raw[i - 1]) &&
                    (end == raw.Length || char.IsWhiteSpace(raw[end]));
                if (word.Length < 2 && !isolatedOne)
                {
                    diagnostics?.Reject("generation-skipped/english-single-letter-without-context");
                    continue;
                }
                bool lexical = Lexicon.Contains(word.ToLowerInvariant());
                bool naturalWord = word.Length >= 4 &&
                    (Lexicon.Contains(word[..^1].ToLowerInvariant()) && word.EndsWith('s') ||
                     word.EndsWith("ing", StringComparison.OrdinalIgnoreCase) &&
                        Lexicon.Contains(word[..^3].ToLowerInvariant()));
                // Productive two-word compounds are evidence even when the
                // concatenated spelling has never been entered into a list.
                bool compound = !lexical && word.Length >= 6 &&
                    Enumerable.Range(3, word.Length - 5).Any(split =>
                        Lexicon.Contains(word[..split].ToLowerInvariant()) &&
                        Lexicon.Contains(word[split..].ToLowerInvariant()));
                bool derivedPrefix = !lexical && word.Length >= 6 &&
                    new[] { "dis", "re", "pre", "un", "sub" }.Any(prefix =>
                        word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        word.Length >= prefix.Length + 4 &&
                        Lexicon.Contains(word[prefix.Length..].ToLowerInvariant()));
                double lexBonus = lexical ? 2.3 + Math.Min(word.Length, 12) * .38 :
                    compound ? 5.5 : derivedPrefix ? 3.5 :
                    naturalWord ? 2.2 : 0;
                double capsBonus = char.IsUpper(word[0]) ? 1.55 : 0;
                if (word.Length <= 6 && word.All(char.IsUpper)) capsBonus += 1.4;
                // Many Japanese readings contain short English-looking
                // substrings. A full, dictionary-unconfirmed phonetic word
                // does not win from spelling noise alone.
                double readablePenalty = readable ? 2.25 : 0;
                double spell = -1.45 + difference * Math.Min(word.Length, 14) * 0.70 +
                    0.11 * word.Length + lexBonus + capsBonus - readablePenalty;
                // The right seam is Japanese phonotactics, not a list of
                // examples. A lexical/nonphonetic word should end BEFORE a
                // particle rather than consume its first letter.
                if (end < endRun && word.Length >= 2 && (!readable || lexical) &&
                    JapaneseSeams.Any(p => raw.AsSpan(end, endRun - end)
                        .StartsWith(p.AsSpan(), StringComparison.OrdinalIgnoreCase)))
                    spell += !readable && Consonant(word[^1]) ? 5.7 : 2.8;
                if (end == endRun && word.Any(char.IsUpper) &&
                    word.Any(char.IsLower) && word.Skip(1).Any(char.IsUpper))
                    spell += 5.8;
                if (word.Length >= 2 && word.All(char.IsUpper) && end < endRun &&
                    char.IsLower(raw[end]) && (lexical || word.Length >= 3))
                    spell += 5.5; // acronym followed by an unspaced JA reading
                if (end == endRun && i > 0 && char.IsWhiteSpace(raw[i - 1]) &&
                    char.IsUpper(word[0]) && word.Length >= 3) spell += 2.8;
                if (end == endRun && lexical && !readable) spell += 2.1;
                // Latin word immediately adjacent to existing Japanese script
                // is a stronger English island than an arbitrary substring.
                if (end == endRun && end < raw.Length &&
                    char.IsLetter(raw[end]) && !char.IsAsciiLetter(raw[end]) &&
                    (lexical || char.IsUpper(word[0]))) spell += 3.2;
                if (isolatedOne) spell += 2.0;
                Add(new Utf16Span(i, end), Lang.En, spell, lexical
                    ? "baseline-lexicon-word" : "orthographic-word", endRun);
            }
        }
        // Numeric+Latin terms: 50cc, 3D, 100GB. Their *whole* digit-prefixed
        // span must exist; H10 produced only the ASCII suffix.
        for (int i = 0; i < raw.Length; i++)
        {
            if (!char.IsAsciiDigit(raw[i]) ||
                i > 0 && char.IsAsciiDigit(raw[i - 1])) continue;
            int j = i;
            while (j < raw.Length && char.IsAsciiDigit(raw[j])) j++;
            int beginLetters = j;
            while (j < raw.Length && char.IsAsciiLetter(raw[j]) && j - i < MaxEdge) j++;
            for (int end = beginLetters + 1; end <= j; end++)
            {
                bool seam = end < j && Consonant(raw[end]) && end + 1 < j &&
                    "aeiou".Contains(char.ToLowerInvariant(raw[end + 1]));
                bool doubles = end >= beginLetters + 2 &&
                    raw[end - 1] == raw[end - 2];
                Add(new Utf16Span(i, end), Lang.En,
                    4.5 + 0.34 * (end - i) + (seam && doubles ? 5.0 : 0),
                    "numeric-latin");
            }
        }

        return edges;
    }

}
