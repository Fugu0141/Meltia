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

        if (args.Length == 2 && args[0] == "--supervise")
            return OfflineSupervisor.Run(args[1]);

        if (args.Length == 3 && args[0] == "--offline-host")
            return OfflineHost.Run(args[1], args[2]);

        // Refuse ordinary launches: this is not the shipping Meltia.exe or a TSF IME.
        Console.Error.WriteLine("T3e requires explicit --offline-host <ready-marker> <stop-marker> or --probe.");
        return 2;
    }
}
