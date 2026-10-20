#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T3g job containment: source safety, explicit startup gate and real Windows CI wiring."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
HOST = ROOT / "src/Meltia.TipHost.Experimental"


class T3gJobContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.job = (HOST / "OfflineWorkerJob.cs").read_text(encoding="utf-8")
        cls.supervisor = (HOST / "OfflineSupervisor.cs").read_text(encoding="utf-8")
        cls.host = (HOST / "OfflineHost.cs").read_text(encoding="utf-8")
        cls.program = (HOST / "Program.cs").read_text(encoding="utf-8")
        cls.script = (ROOT / "tools/test-meltia-t3g-job.ps1").read_text(encoding="utf-8")
        cls.ci = (ROOT / ".github/workflows/meltia-windows-preview-ci.yml").read_text(encoding="utf-8")
        cls.preview = (ROOT / "src/Meltia.Windows/Meltia.Windows.csproj").read_text(encoding="utf-8")

    def test_private_kill_on_close_job_and_owned_process_only(self):
        for fragment in (
            "CreateJobObject(IntPtr.Zero, null)", "JobObjectLimitKillOnJobClose = 0x00002000",
            "SetInformationJobObject(", "AssignProcessToJobObject(_handle, worker.Handle)",
            "SafeFileHandle", "handle.Dispose()", "public void Dispose()",
            "throw new Win32Exception", "OperatingSystem.IsWindows()",
        ):
            self.assertIn(fragment, self.job)
        for fragment in ("using var job = OfflineWorkerJob.Create()",
                         "using var child = Launch(", "job.Assign(child)",
                         "File.WriteAllText(startGate", "StopChild(child, stop)"):
            self.assertIn(fragment, self.supervisor)
        self.assertLess(self.supervisor.index("job.Assign(child)"),
                        self.supervisor.index('File.WriteAllText(startGate'))
        self.assertLess(self.supervisor.index("OfflineWorkerJob.Create()"),
                        self.supervisor.index("Launch(ready, stop, drop, startGate)"))
        self.assertNotIn("Process.GetProcesses", self.job)
        self.assertNotIn("OpenProcess(", self.job)

    def test_no_implicit_host_or_tsf_activation(self):
        self.assertIn('args.Length == 5 && args[0] == "--offline-host"', self.program)
        self.assertIn("return OfflineHost.Run(args[1], args[2], args[3], args[4]);", self.program)
        self.assertIn("startGateMarker is not null", self.host)
        self.assertIn("while (!File.Exists(startGateMarker))", self.host)
        self.assertIn("TimeSpan.FromSeconds(10)", self.host)
        self.assertIn("return 7;", self.host)
        self.assertLess(self.host.index("while (!File.Exists(startGateMarker))"),
                        self.host.index("new TipServer("))
        self.assertNotIn("Meltia.TipHost.Experimental", self.preview)
        for path in HOST.glob("*.cs"):
            code = path.read_text(encoding="utf-8")
            for blocked in ("DllRegisterServer", "regsvr32", "ActivateForSession(",
                            "InputHook(", "MeltypeEngine(", "Startup.Set(",
                            "Settings.Save(", "SendInput(", "Registry."):
                self.assertNotIn(blocked, code, str(path))

    def test_real_supervisor_death_and_recovery_are_in_ci(self):
        for fragment in ("$first.Process.Kill()", "$ownedWorker.WaitForExit(10000)",
                         "$second = Start-Supervisor", "Verify-RealIPC",
                         "ungated.WaitForExit(16000)", "ungated.ExitCode -ne 7",
                         "Test-Path -LiteralPath $ready", "MELTYPE_DATA_DIR",
                         "finally {", "$worker.Dispose()"):
            self.assertIn(fragment, self.script)
        self.assertIn("tools/test-meltia-t3g-job.ps1", self.ci)
        self.assertIn("tools/test_meltia_t3g_job_contract.py", self.ci)
        self.assertIn("tools/test-meltia-t3f-supervisor.ps1", self.ci)


if __name__ == "__main__":
    unittest.main()
