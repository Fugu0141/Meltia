// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
using System.Diagnostics;
using Meltia.Core.Boundary;

internal static class Phase3D2BeamTests
{
    private const int Width = 16;
    private const int Count = Width * 2 + 1;
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static void Run(Action<bool, string> check)
    {
        var seed = new Random(20261017);
        var fixtures = new List<H11BoundaryPrototype.Node[]>();
        for (int sample = 0; sample < 2800; sample++)
        {
            var scores = new double[Count];
            for (int i = 0; i < Count; i++)
            {
                // Exercise unique, heavily tied and partly tied beams.
                scores[i] = sample % 5 switch
                {
                    0 => i * 0.001 + seed.NextDouble() / 2000,
                    1 => seed.Next(0, 7),
                    2 => seed.Next(-10000, 10001) / 64.0,
                    3 => 100 - i,
                    _ => seed.NextDouble() * 150 - 75
                };
            }
            Shuffle(scores, seed);
            fixtures.Add(Make(scores));
        }
        fixtures.Add(Make(Enumerable.Repeat(4.0, Count).ToArray()));
        fixtures.Add(Make(Enumerable.Range(0, Count)
            .Select(i => i >= Width ? 1.0 : i + 100.0).ToArray()));
        fixtures.Add(Make(Enumerable.Range(0, Count)
            .Select(i => i == Width ? Width - 1.0 : (double)i).ToArray()));
        fixtures.Add(Make(Enumerable.Range(0, Count)
            .Select(i => i == 0 ? double.NaN : (double)i).ToArray()));
        fixtures.Add(Make(Enumerable.Range(0, Count)
            .Select(i => i == 0 ? -0.0 : i == 1 ? 0.0 : (double)i).ToArray()));
        fixtures.Add(Make(Enumerable.Range(0, Count)
            .Select(i => i == 0 ? double.PositiveInfinity :
                i == 1 ? double.NegativeInfinity : (double)i).ToArray()));
        fixtures.Add(Make(Enumerable.Range(0, Count)
            .Select(i => i % 7 == 0 ? double.NaN : (double)i).ToArray()));

        int chosenFast = 0, fallback = 0;
        foreach (var fixture in fixtures)
        {
            var baseline = new List<H11BoundaryPrototype.Node>(fixture);
            LegacyPrune(baseline);
            var actual = new List<H11BoundaryPrototype.Node>(fixture);
            if (H11BeamSelection.TryPruneUnique(actual, Width)) chosenFast++;
            else
            {
                fallback++;
                // The selector must not move a single node before declining.
                for (int i = 0; i < fixture.Length; i++)
                    if (!ReferenceEquals(actual[i], fixture[i]))
                        throw new Exception("D2 modified original order before fallback");
                LegacyPrune(actual);
            }
            if (baseline.Count != actual.Count ||
                !baseline.Zip(actual).All(x => ReferenceEquals(x.First, x.Second)))
                throw new Exception("D2 changed List.Sort + RemoveRange retained order");
        }
        check(chosenFast > 0 && fallback > 0,
            "H11 D2 legacy beam and guarded top16 yield identical references/order across random and tie cases");

        var smaller = Make(Enumerable.Range(1, Count - 1).Select(i => (double)i).ToArray());
        foreach (int size in new[] { 0, 1, Width, Width * 2 })
        {
            var untouched = new List<H11BoundaryPrototype.Node>(smaller.Take(size));
            var original = untouched.ToArray();
            if (H11BeamSelection.TryPruneUnique(untouched, Width) ||
                !untouched.SequenceEqual(original))
                throw new Exception("D2 should not prune before reaching 33 entries");
        }
        check(true, "H11 D2 does not prematurely trim states with <=32 entries");

        // Same-process, same-order synthetic microbenchmark; informational only.
        // List<Node> creation is intentionally included in BOTH measurements.
        foreach (var mode in new[] { "unique", "tie" })
        {
            var cases = fixtures.Where((_, i) =>
                mode == "tie" ? i % 5 == 1 : i % 5 == 3).Take(160).ToArray();
            const int loops = 64; // 10,240 independently reconstructed beams per trial
            _ = Measure(cases, fast: false, loops);
            _ = Measure(cases, fast: true, loops);
            var oldResults = new List<(double Milliseconds, long AllocatedBytes, ulong Checksum)>();
            var newResults = new List<(double Milliseconds, long AllocatedBytes, ulong Checksum)>();
            for (int trial = 0; trial < 6; trial++)
            {
                // Alternate order to reduce warmed-code and scheduler order bias.
                if (trial % 2 == 0)
                {
                    oldResults.Add(Measure(cases, fast: false, loops));
                    newResults.Add(Measure(cases, fast: true, loops));
                }
                else
                {
                    newResults.Add(Measure(cases, fast: true, loops));
                    oldResults.Add(Measure(cases, fast: false, loops));
                }
            }
            ulong expected = oldResults[0].Checksum;
            if (oldResults.Concat(newResults).Any(x => x.Checksum != expected))
                throw new Exception("D2 benchmark outputs differ; score/order hash mismatch");
            static double Median(IEnumerable<double> values)
            {
                var ordered = values.Order().ToArray();
                return (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;
            }
            Console.WriteLine($"H11_D2_BENCH mode={mode} " +
                $"beams_per_trial={cases.Length * loops} width={Width} trials=6 " +
                $"legacy_median_ms={Median(oldResults.Select(x => x.Milliseconds)):F3} " +
                $"fast_median_ms={Median(newResults.Select(x => x.Milliseconds)):F3} " +
                $"legacy_alloc_bytes={oldResults[0].AllocatedBytes} " +
                $"fast_alloc_bytes={newResults[0].AllocatedBytes} " +
                $"checksum={expected:X16}");
        }
        check(true, "H11 D2 ordered benchmark hash matches original comparator for distinct and tied scores");
    }

    private static H11BoundaryPrototype.Node[] Make(double[] scores)
    {
        var nodes = new H11BoundaryPrototype.Node[scores.Length];
        for (int i = 0; i < scores.Length; i++)
        {
            var edge = new H11BoundaryPrototype.Edge(
                new Utf16Span(i, i + 1), H11BoundaryPrototype.Lang.En,
                scores[i], "d2-synthetic");
            nodes[i] = new H11BoundaryPrototype.Node(
                scores[i], H11BoundaryPrototype.Lang.En, edge, null);
        }
        return nodes;
    }

    private static void Shuffle(double[] data, Random random)
    {
        for (int i = data.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (data[i], data[j]) = (data[j], data[i]);
        }
    }

    private static void LegacyPrune(List<H11BoundaryPrototype.Node> nodes)
    {
        nodes.Sort((a, b) => b.Score.CompareTo(a.Score));
        nodes.RemoveRange(Width, nodes.Count - Width);
    }

    private static (double Milliseconds, long AllocatedBytes, ulong Checksum)
        Measure(H11BoundaryPrototype.Node[][] fixtures, bool fast, int loops)
    {
        ulong checksum = Offset;
        long startAlloc = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int iteration = 0; iteration < loops; iteration++)
        foreach (var fixture in fixtures)
        {
            var nodes = new List<H11BoundaryPrototype.Node>(fixture);
            if (!fast || !H11BeamSelection.TryPruneUnique(nodes, Width))
                LegacyPrune(nodes);
            foreach (var node in nodes)
            {
                checksum = unchecked((checksum ^ (ulong)BitConverter.DoubleToInt64Bits(
                    node.Score)) * Prime);
                checksum = unchecked((checksum ^ (ulong)node.Edge!.Span.Start) * Prime);
            }
        }
        long end = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - startAlloc;
        return ((end - start) * 1000.0 / Stopwatch.Frequency, allocated, checksum);
    }
}
