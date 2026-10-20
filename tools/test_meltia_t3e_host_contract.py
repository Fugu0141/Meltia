#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T3e offline-host safety and lifecycle contract (does not replace Windows IPC E2E)."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
HOST_DIR = ROOT / "src/Meltia.TipHost.Experimental"


class T3eHostContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.host = (HOST_DIR / "OfflineHost.cs").read_text(encoding="utf-8")
        cls.program = (HOST_DIR / "Program.cs").read_text(encoding="utf-8")
        cls.probe = (HOST_DIR / "OfflineProbe.cs").read_text(encoding="utf-8")
        cls.pipe = (ROOT / "src/Meltype/Tip/TipServer.cs").read_text(encoding="utf-8")
        cls.preview = (ROOT / "src/Meltia.Windows/Meltia.Windows.csproj").read_text(encoding="utf-8")
        cls.ci = (ROOT / ".github/workflows/meltia-windows-preview-ci.yml").read_text(encoding="utf-8")

    def test_explicit_opt_in_and_no_os_input_capture(self):
        self.assertIn('args[0] == "--offline-host"', self.program)
        self.assertIn('args[0] == "--probe"', self.program)
        self.assertIn("return 2;", self.program)
        for path in HOST_DIR.glob("*.cs"):
            source = path.read_text(encoding="utf-8")
            for forbidden in (
                "Register-Tip", "DllRegisterServer", "regsvr32", "Registry.",
                "ActivateForSession(", "InputHook(", "MeltypeEngine(",
                "Startup.Set(", "Settings.Save(", "SendInput(", "InstallHook(",
            ):
                self.assertNotIn(forbidden, source, f"{path.name}: {forbidden}")
        self.assertNotIn("Meltia.TipHost.Experimental", self.preview)
        self.assertIn("MELTIA_PREVIEW", self.preview)

    def test_single_instance_sandbox_and_real_listener_readiness(self):
        self.assertIn('Local\\Meltia.TipHost.Experimental.', self.host)
        self.assertIn("mutex.WaitOne(0)", self.host)
        self.assertIn("AbandonedMutexException", self.host)
        self.assertIn("MELTYPE_DATA_DIR", self.host)
        self.assertIn("UnderTemp(dataDirectory)", self.host)
        self.assertIn("FirstListenerReady.WaitAsync", self.host)
        self.assertIn("_firstListener.TrySetResult(true)", self.pipe)
        self.assertIn("_firstListener.TrySetResult(false)", self.pipe)
        self.assertLess(self.pipe.index("_firstListener.TrySetResult(true)"), self.pipe.index("await pipe.WaitForConnectionAsync"))
        self.assertIn("meltiaExperimental: true", self.host)

    def test_real_ipc_probe_and_windows_process_gate(self):
        self.assertIn("NamedPipeClientStream", self.probe)
        self.assertIn("MeltiaTipIdentity.PipePrefix", self.probe)
        self.assertIn('GetString() != "か"', self.probe)
        harness = (ROOT / "tools/test-meltia-t3e-host.ps1").read_text(encoding="utf-8")
        for fragment in ("Wait-Ready", "Probe-Host", "Stop-OfflineHost",
                         "'duplicate'", "ExitCode -ne 3", ".Kill()", "'after-crash'"):
            self.assertIn(fragment, harness)
        self.assertIn("test-meltia-t3e-host.ps1", self.ci)


if __name__ == "__main__":
    unittest.main()
