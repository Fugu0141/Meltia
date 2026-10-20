// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T3g: Windows Job Object containment for explicitly launched test workers only.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Meltia.TipHost.Experimental;

/// <summary>
/// Owns a private, unnamed, non-inheritable Job Object. Closing its last handle
/// terminates the assigned worker, even if the experimental supervisor crashes.
/// This is never used for arbitrary processes or the shipping Preview.
/// </summary>
internal sealed class OfflineWorkerJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectQuery = 0x0004;
    private readonly SafeFileHandle _handle;

    internal string Name { get; }

    private OfflineWorkerJob(SafeFileHandle handle, string name)
    {
        _handle = handle;
        Name = name;
    }

    internal static OfflineWorkerJob Create()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("T3g Job Objects require Windows.");

        // A per-launch name allows the child to verify membership in THIS job,
        // rather than trusting a writable start.allowed marker alone.
        var name = @"Local\Meltia.TipHost.Experimental.Worker." + Guid.NewGuid().ToString("N");
        var handle = CreateJobObject(IntPtr.Zero, name);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "T3g could not create a private worker job.");
        }

        try
        {
            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass,
                    ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "T3g could not enable kill-on-job-close.");
            return new OfflineWorkerJob(handle, name);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal void Assign(Process worker)
    {
        // Never adopt an external PID. The caller passes the Process returned
        // by its own Process.Start and must terminate it if assignment fails.
        if (!AssignProcessToJobObject(_handle, worker.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "T3g worker job assignment failed; refusing an uncontained worker.");
    }

    /// <summary>
    /// A worker independently checks membership in the exact job named by its
    /// supervisor. A pre-created start marker is not sufficient for activation.
    /// The query handle is closed immediately; it must not keep the job alive
    /// when its supervisor terminates.
    /// </summary>
    internal static bool IsCurrentProcessInJob(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !name.StartsWith(@"Local\Meltia.TipHost.Experimental.Worker.", StringComparison.Ordinal))
            return false;

        using var job = OpenJobObject(JobObjectQuery, false, name);
        if (job.IsInvalid) return false;
        using var current = Process.GetCurrentProcess();
        return IsProcessInJob(current.Handle, job, out var assigned) && assigned;
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJobObject(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(
        IntPtr process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job, int infoClass, ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
