// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using System.Text.Json;
using Meltype.Composition;

namespace Meltype;

/// <summary>
/// Safe integration smoke tests: no keyboard hook, no Windows TSF
/// registration, no user input capture, and no text commit.
/// </summary>
internal static class MeltiaPreviewSelfTest
{
    public static int Run(string? outputPath)
    {
        var cases = new List<object>();
        bool original = MeltiaPreviewBoundary.Enabled;
        try
        {
            var detector = CompositionDetector.CreateDefault();
            detector.BoundaryRefinement = (units, pending, legacy) =>
                detector.Memory?.Get((string.Concat(units.Select(x => x.Raw)) +
                    pending).ToLowerInvariant()) is not null
                    ? null
                    : MeltiaLexicalRecovery.TryRecover(
                          units, pending, legacy, detector) ??
                      MeltiaPreviewBoundary.Refine(units, pending, legacy);

            Check("preview-keeps-separated-data", () =>
            {
                var path = Config.AppPaths.DataDirectory;
                var expected = Environment.GetEnvironmentVariable("MELTYPE_DATA_DIR");
                var meltype = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Meltype");
                return string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFullPath(path),
                        Path.GetFullPath(meltype), StringComparison.OrdinalIgnoreCase);
            });

            var raw = "nihongocompiler";
            var text = new CompositionText(detector);
            foreach (var c in raw) text.Append(c);
            var legacy = detector.Segment(text.Units, text.Pending);
            Check("roundtrip-raw-through-Meltype-composition", () =>
                text.Raw == raw &&
                string.Concat(legacy.Select(x => x.Raw)) == raw);

            var units = new List<CompositionUnit>
            {
                new("に", "ni"), new("ほ", "ho"), new("ん", "n"),
                new("ご", "go"), new("compiler", "compiler")
            };
            var host = new CompositionSegment[]
            {
                new(false, "にほんご", "nihongo"),
                new(true, "", "compiler")
            };
            MeltiaPreviewBoundary.Enabled = false;
            Check("default-off-returns-Meltype-result", () =>
                MeltiaPreviewBoundary.Refine(units, "", host) is null);
            MeltiaPreviewBoundary.Enabled = true;
            Check("pending-romaji-fails-closed", () =>
                MeltiaPreviewBoundary.Refine(units, "n", host) is null);
            Check("protected-input-fails-closed", () =>
                MeltiaPreviewBoundary.Refine(units, "",
                    [new CompositionSegment(true, "", "nihongocompiler")
                        { IsProtected = true }]) is null);
            Check("UTF16-original-source-roundtrip", () =>
            {
                var proposal = MeltiaPreviewBoundary.Refine(units, "", host);
                return proposal is null ||
                    string.Concat(proposal.Select(x => x.Raw)) == "nihongocompiler";
            });
            // These are actual CompositionText keystroke streams, not just
            // direct calls into the experimental boundary Core. The default
            // experimental toggle stays OFF for the lexical checks.
            MeltiaPreviewBoundary.Enabled = false;
            Check("commit-English-not-Japanese-romanization", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "commit") input.Append(c);
                var pieces = input.Segments(final: true);
                return pieces.Count == 1 && pieces[0].IsEnglish &&
                    pieces[0].Raw == "commit";
            });
            Check("commit-ha-English-then-particle", () =>
            {
                var input = new CompositionText(detector);
                var beforeH = "";
                var afterH = "";
                foreach (var c in "commitha")
                {
                    if (c == 'h')
                        beforeH = "beforeH[" + input.Pending + "] " +
                            string.Join("|", input.Units.Select(u => u.Raw + ":" + u.Kana)) +
                            " known(commit)=" + detector.IsKnownEnglishWord("commit");
                    input.Append(c);
                    if (c == 'h')
                        afterH = "afterH[" + input.Pending + "] " +
                            string.Join("|", input.Units.Select(u => u.Raw + ":" + u.Kana));
                }
                var pieces = input.Segments(final: true);
                var ok = pieces.Count == 2 &&
                    pieces[0].IsEnglish && pieces[0].Raw == "commit" &&
                    !pieces[1].IsEnglish && pieces[1].Raw == "ha" &&
                    pieces[1].Kana == "は" &&
                    input.RenderSegments(final: true, convert: null) == "commitは";
                if (!ok)
                    throw new InvalidOperationException(
                        beforeH + "; " + afterH + "; units=" + string.Join("|", input.Units.Select(u => u.Raw + ":" + u.Kana)) +
                        "; pending=" + input.Pending +
                        "; segments=" + string.Join("|", pieces.Select(p => p.Raw + ":" + p.Kana + ":" + (p.IsEnglish ? "EN" : "JA"))) +
                        "; display=" + input.RenderSegments(final: true, convert: null));
                return true;
            });
            Check("Ado-san-no-preserves-name-and-Japanese", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "Adosannno") input.Append(c);
                var pieces = input.Segments(final: true);
                return pieces.Count == 2 &&
                    pieces[0].IsEnglish && pieces[0].Raw == "Ado" &&
                    !pieces[1].IsEnglish && pieces[1].Raw == "sannno" &&
                    pieces[1].Kana == "さんの";
            });
            Check("API-raw-uppercase-does-not-turn-into-kana", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "API") input.Append(c);
                return input.RenderSegments(final: true, convert: null) == "API" &&
                    input.Raw == "API";
            });
            Check("meltia-ga-keystroke-roundtrip", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "meltiaga")
                {
                    input.Append(c);
                    if (string.Concat(input.Segments().Select(p => p.Raw)) != input.Raw)
                        return false;
                }
                var parts = input.Segments(final: true);
                return parts.Count == 2 &&
                    parts[0].IsEnglish && parts[0].Raw == "meltia" &&
                    !parts[1].IsEnglish && parts[1].Raw == "ga" &&
                    parts[1].Kana == "が";
            });
            Check("meltiawo-English-then-Japanese", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "meltiawotamesu") input.Append(c);
                var pieces = input.Segments(final: true);
                return pieces.Count == 2 &&
                    pieces[0].IsEnglish && pieces[0].Raw == "meltia" &&
                    !pieces[1].IsEnglish && pieces[1].Raw == "wotamesu" &&
                    pieces[1].Kana == "をためす";
            });
            Check("capitalized-Meltia-word-preserves-raw", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "Meltiawotamesu") input.Append(c);
                var pieces = input.Segments(final: true);
                return pieces.Count == 2 &&
                    pieces[0].IsEnglish && pieces[0].Raw == "Meltia" &&
                    !pieces[1].IsEnglish && pieces[1].Kana == "をためす";
            });
            // Issue #32: "change" is both a listed English word and fully
            // readable as Japanese romaji ("ちゃんげ"). The stable lexical
            // safeguard must preserve it before the complete Japanese "suru".
            foreach (var boundaryEnabled in new[] { false, true })
            {
                MeltiaPreviewBoundary.Enabled = boundaryEnabled;
                Check("issue32-change-suru-boundary-" +
                    (boundaryEnabled ? "experimental-on" : "experimental-off"), () =>
                {
                    var input = new CompositionText(detector);
                    foreach (var c in "changesuru")
                    {
                        input.Append(c);
                        if (string.Concat(input.Segments().Select(p => p.Raw)) != input.Raw)
                            return false;
                    }
                    // The reported regression is visible before Space/Enter.
                    // Verify the live composition after the final keystroke, not
                    // only the final/committed rendering.
                    var live = input.Segments(final: false);
                    if (live.Count != 2 ||
                        !live[0].IsEnglish || live[0].Raw != "change" ||
                        live[1].IsEnglish || live[1].Raw != "suru" ||
                        live[1].Kana != "する" ||
                        input.RenderSegments(final: false, convert: null) != "changeする")
                        return false;
                    var segments = input.Segments(final: true);
                    return input.Raw == "changesuru" &&
                        segments.Count == 2 &&
                        segments[0].IsEnglish && segments[0].Raw == "change" &&
                        !segments[1].IsEnglish && segments[1].Raw == "suru" &&
                        segments[1].Kana == "する" &&
                        input.RenderSegments(final: true, convert: null) == "changeする";
                });
            }
            MeltiaPreviewBoundary.Enabled = false;
            Check("issue32-capitalized-change-suru", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "Changesuru") input.Append(c);
                var segments = input.Segments(final: true);
                return segments.Count == 2 && segments[0].IsEnglish &&
                    segments[0].Raw == "Change" &&
                    !segments[1].IsEnglish && segments[1].Kana == "する" &&
                    input.RenderSegments(final: true, convert: null) == "Changeする";
            });
            Check("issue32-do-not-split-unrelated-japanese-or-incomplete-suffix", () =>
            {
                // Use a detector without the Preview refinement for the
                // baseline. Passing input.Segments() to TryRecover would
                // check the already-refined output and could falsely pass.
                var baselineDetector = CompositionDetector.CreateDefault();
                foreach (var rawInput in new[] {
                    "chakusuru", "nihongosuru", "tokyosuru",
                    "changesu", "changesur", "changesurux", "changezuru"
                })
                {
                    var input = new CompositionText(detector);
                    foreach (var c in rawInput) input.Append(c);
                    var baseline = baselineDetector.Segment(
                        input.Units, input.Pending, final: true);
                    if (MeltiaLexicalRecovery.TryRecover(
                        input.Units, input.Pending, baseline, detector) is not null)
                        return false;
                    if (!input.Segments(final: true).SequenceEqual(baseline))
                        return false;
                }
                return true;
            });
            Check("issue32-respects-explicit-Japanese-learning", () =>
            {
                var learned = CompositionDetector.CreateDefault();
                learned.Memory = new LanguageMemory(path: null);
                learned.Memory.Remember("change", english: false, explicitChoice: true);
                var input = new CompositionText(learned);
                foreach (var c in "changesuru") input.Append(c);
                return MeltiaLexicalRecovery.TryRecover(
                    input.Units, input.Pending, input.Segments(final: true),
                    learned) is null;
            });
            Check("issue32-respects-protected-spans", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "changesuru") input.Append(c);
                return MeltiaLexicalRecovery.TryRecover(
                    input.Units, input.Pending,
                    [new CompositionSegment(true, "", "changesuru")
                        { IsProtected = true }], detector) is null;
            });
            Check("Japanese-only-input-not-forced-to-English", () =>
            {
                var input = new CompositionText(detector);
                foreach (var c in "nihongowotamesu") input.Append(c);
                var pieces = input.Segments(final: true);
                return pieces.All(x => !x.IsEnglish) &&
                    string.Concat(pieces.Select(x => x.Raw)) == input.Raw;
            });
            Check("ambiguous-English-Japanese-words-not-forced", () =>
            {
                foreach (var word in new[] { "ime", "tokyo", "sake" })
                {
                    var input = new CompositionText(detector);
                    foreach (var c in word) input.Append(c);
                    if (MeltiaLexicalRecovery.TryRecover(
                        input.Units, input.Pending, input.Segments(final: true),
                        detector) is not null) return false;
                }
                return true;
            });
            foreach (var scenario in new[] {
                "ime", "tokyo", "japaneseno",
                "kyouhacompilernophotonicswotukau"
            })
            {
                var input = new CompositionText(detector);
                foreach (var c in scenario) input.Append(c);
                var next = detector.Segment(input.Units, input.Pending);
                Check("live-segment-roundtrip-" + scenario, () =>
                    string.Concat(next.Select(x => x.Raw)) == input.Raw);
            }
        }
        catch (Exception ex)
        {
            cases.Add(new { name = "unhandled-exception", passed = false,
                error = ex.GetType().Name + ": " + ex.Message });
        }
        finally
        {
            MeltiaPreviewBoundary.Enabled = original;
        }

        var failed = cases.Count(x =>
            x.GetType().GetProperty("passed")?.GetValue(x) is false);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(new {
                Product = "Meltia Windows Keyboard Preview",
                Version = "1.0.0-beta.1",
                Passed = failed == 0,
                Cases = cases
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failed == 0 ? 0 : 1;

        void Check(string name, Func<bool> assertion)
        {
            bool ok;
            string? error = null;
            try { ok = assertion(); }
            catch (Exception ex)
            {
                ok = false;
                error = ex.Message;
            }
            cases.Add(new { name, passed = ok, error });
        }
    }
}
