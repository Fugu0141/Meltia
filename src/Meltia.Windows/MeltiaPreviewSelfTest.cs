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
            detector.BoundaryRefinement = MeltiaPreviewBoundary.Refine;

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
                Version = "1.0.0-dev",
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
