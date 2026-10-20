// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Explicit T3e offline host only. No TSF registration, activation, hook, or persistence.
using Meltype.Tip;

namespace Meltia.TipHost.Experimental;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("T3e offline TIP host requires Windows.");
            return 2;
        }

        if (args.Length == 1 && args[0] == "--probe")
            return OfflineProbe.Run();

        // Test-only, malformed hello replies served over a real private pipe.
        if (args.Length == 1 && args[0] == "--test-health-replies")
            return OfflineHealthReplyTests.Run();

        if (args.Length == 2 && args[0] == "--supervise")
            return OfflineSupervisor.Run(args[1]);

        // Explicit integration-only fault injection: never wire into Preview.
        if (args.Length == 2 && args[0] == "--supervise-test-assign-failure")
            return OfflineSupervisor.Run(args[1], testAssignFailure: true);

        if (args.Length == 3 && args[0] == "--offline-host")
            return OfflineHost.Run(args[1], args[2]);

        if (args.Length == 4 && args[0] == "--offline-host")
            return OfflineHost.Run(args[1], args[2], args[3]);

        // T3g supervisor worker: marker AND verified membership in this exact
        // per-launch Job Object must succeed before it can open a Pipe.
        if (args.Length == 6 && args[0] == "--offline-host")
            return OfflineHost.Run(args[1], args[2], args[3], args[4], args[5]);

        // Refuse ordinary launches: this is not the shipping Meltia.exe or a TSF IME.
        Console.Error.WriteLine("T3e requires explicit --offline-host <ready-marker> <stop-marker> or --probe.");
        return 2;
    }
}
