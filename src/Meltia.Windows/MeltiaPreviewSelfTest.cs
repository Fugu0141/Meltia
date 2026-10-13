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
                foreach (var c in "commitha") input.Append(c);
                var pieces = input.Segments(final: true);
                return pieces.Count == 2 &&
                    pieces[0].IsEnglish && pieces[0].Raw == "commit" &&
                    !pieces[1].IsEnglish && pieces[1].Raw == "ha" &&
                    pieces[1].Kana == "は" &&
                    input.RenderSegments(final: true, convert: null) == "commitは";
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
            try { ok = assertion(); }
            catch { ok = false; }
            cases.Add(new { name, passed = ok });
        }
    }
}
