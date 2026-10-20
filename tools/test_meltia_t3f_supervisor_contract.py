#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T3f offline-only supervisor safety and bounded-retry source contract."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
HOST = ROOT / "src/Meltia.TipHost.Experimental"


class T3fSupervisorContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.supervisor = (HOST / "OfflineSupervisor.cs").read_text(encoding="utf-8")
        cls.program = (HOST / "Program.cs").read_text(encoding="utf-8")
        cls.host = (HOST / "OfflineHost.cs").read_text(encoding="utf-8")
        cls.probe = (HOST / "OfflineProbe.cs").read_text(encoding="utf-8")
        cls.script = (ROOT / "tools/test-meltia-t3f-supervisor.ps1").read_text(encoding="utf-8")
        cls.ci = (ROOT / ".github/workflows/meltia-windows-preview-ci.yml").read_text(encoding="utf-8")
        cls.preview = (ROOT / "src/Meltia.Windows/Meltia.Windows.csproj").read_text(encoding="utf-8")

    def test_supervisor_is_explicit_and_unregistered(self):
        self.assertIn('args[0] == "--supervise"', self.program)
        self.assertIn('args[0] == "--offline-host"', self.program)
        self.assertIn("return 2;", self.program)
        self.assertNotIn("Meltia.TipHost.Experimental", self.preview)
        for path in HOST.glob("*.cs"):
            code = path.read_text(encoding="utf-8")
            for blocked in ("DllRegisterServer", "regsvr32", "ActivateForSession(",
                            "InputHook(", "MeltypeEngine(", "Startup.Set(",
                            "Settings.Save(", "SendInput(", "Registry."):
                self.assertNotIn(blocked, code, str(path))

    def test_watchdog_is_bounded_and_owned(self):
        for part in ("MaxLaunches = 3", "launch <= MaxLaunches",
                     "return 6;", "child.HasExited", "File.Exists(requestedStop)",
                     "healthFailures >= 2", "OfflineProbe.IsHealthy()",
                     "StopChild(child, stop)", "using var child = Launch",
                     "Meltia.TipHost.Experimental.Supervisor.", "WaitOne(0)",
                     "Path.GetFullPath(data)", "OfflineHost.UnderTemp(sandbox)"):
            self.assertIn(part, self.supervisor)
        self.assertIn("ArgumentList.Add(\"--offline-host\")", self.supervisor)
        self.assertIn("FirstListenerReady.WaitAsync", self.host)
        self.assertIn("server.Dispose();", self.host)
        self.assertIn("JsonValueKind.Null", self.probe)
        self.assertIn("root.TryGetProperty", self.probe)
        self.assertIn("version.TryGetInt32", self.probe)
        self.assertIn("return IsHealthyHello(hello.RootElement)", self.probe)
        self.assertIn('args[0] == "--test-health-replies"', self.program)

    def test_faults_are_tested_as_real_processes(self):
        for part in ("Wait-Generation", "Verify-RealIPC", "Start-Supervisor",
                     "Kill-Worker", "health.drop", "attempt-1",
                     "supervisor.stop", "ExitCode -ne 6",
                     "ExitCode -ne 3", "MELTYPE_DATA_DIR"):
            self.assertIn(part, self.script)
        self.assertIn("tools/test-meltia-t3f-supervisor.ps1", self.ci)
        self.assertIn("test_meltia_t3f_supervisor_contract.py", self.ci)


if __name__ == "__main__":
    unittest.main()
