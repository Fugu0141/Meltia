// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
namespace Meltype;

/// <summary>
/// Isolates the preview's persistent state before any shared Meltype.Core
/// code reads AppPaths. Unlike the original installer, preview never migrates
/// old Meltype/AutoIME data, registers a TIP, or schedules updates.
/// </summary>
internal static class MeltiaPreviewSafety
{
    public static void Initialize()
    {
        string local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        string defaultPath = Path.Combine(local, "Meltia", "Preview");
        string directory = Environment.GetEnvironmentVariable("MELTIA_DATA_DIR")
            is { Length: > 0 } custom ? custom : defaultPath;
        var normalized = Path.GetFullPath(directory);
        var meltype = Path.GetFullPath(Path.Combine(local, "Meltype"));
        var normalizedMeltype = meltype.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (string.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar),
                meltype.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(normalizedMeltype, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Meltia Preview cannot use Meltype's data directory or any child folder.");
        // This setting is intentionally process-local, never written to the
        // user's Windows environment. Original Meltype remains unchanged.
        Environment.SetEnvironmentVariable("MELTYPE_DATA_DIR", normalized);
    }
}
