#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""T4-B4: read-only PE architecture/hash audit for *unregistered* T3a DLLs.

No DLL loading, subprocess, TSF/COM/registry API, installation or file writes.
A valid PE header/hash is NOT proof that exported registration functions are
inert, that the binary came from this build, or that it may be registered.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "config/tsf/meltia-t2-identity.json"
ARCHITECTURES = (("x64", 0x8664, 0x20B), ("x86", 0x014C, 0x10B))
MAX_DLL_BYTES = 64 * 1024 * 1024
NOTICE = "STAGING_NOTICE.txt"


def validate_manifest(manifest):
    """Keep this collector bound to T2 design-only, never installation mode."""
    if not isinstance(manifest, dict) or manifest.get("phase") != "T2" or \
            manifest.get("status") != "design-only" or \
            manifest.get("registrationEnabled") is not False:
        raise ValueError("inactive T2 design manifest required")
    if not isinstance(manifest.get("distribution"), dict) or \
            manifest["distribution"].get("assemblyBaseName") != "MeltiaTip":
        raise ValueError("MeltiaTip assembly identity mismatch")
    deployment = manifest.get("deployment")
    expected = {"performRegistration", "modifyMeltypeRegistration",
                "enableTsfMode", "copyNativeArtifacts"}
    if not isinstance(deployment, dict) or set(deployment) != expected or \
            any(value is not False for value in deployment.values()):
        raise ValueError("deployment must stay strictly disabled")


def inspect_pe(data, expected_machine, expected_magic):
    """Minimal COFF/PE header check; does not execute or trust DLL exports."""
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("not a DOS/PE image")
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    if pe_offset < 0x40 or pe_offset + 24 > len(data) or \
            data[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise ValueError("missing or truncated PE signature")
    machine, section_count, _, _, _, optional_size, characteristics = struct.unpack_from(
        "<HHIIIHH", data, pe_offset + 4)
    if machine != expected_machine:
        raise ValueError("PE machine does not match expected architecture")
    if characteristics & 0x2000 == 0:
        raise ValueError("PE header is not marked as DLL")
    # IMAGE_OPTIONAL_HEADER32/64 has 96/112 fixed bytes BEFORE data
    # directories. Merely checking Magic (2 bytes) accepts truncated PE files.
    fixed_size = 96 if expected_magic == 0x10B else 112
    optional_start = pe_offset + 24
    optional_end = optional_start + optional_size
    sections_end = optional_end + 40 * section_count
    if not 1 <= section_count <= 96 or optional_size < fixed_size or \
            sections_end > len(data):
        raise ValueError("invalid or truncated PE optional/section headers")
    magic = struct.unpack_from("<H", data, optional_start)[0]
    if magic != expected_magic:
        raise ValueError("PE32/PE32+ magic does not match architecture")
    # NumberOfRvaAndSizes is the last DWORD of the fixed header.
    # Each claimed data directory requires eight additional header bytes.
    directory_count = struct.unpack_from("<I", data, optional_start + fixed_size - 4)[0]
    if directory_count > 16 or fixed_size + 8 * directory_count > optional_size:
        raise ValueError("PE data directory count exceeds optional header")
    return {"machine": f"0x{machine:04x}",
            "pe_format": "PE32+" if magic == 0x20B else "PE32"}


def verify_staging_directory(staging_root, manifest):
    """Read-only snapshot of T3a outputs. Does not attest build provenance."""
    validate_manifest(manifest)
    root = Path(staging_root)
    if root.is_symlink() or not root.is_dir():
        raise ValueError("staging root missing or symbolic link")
    if (root / NOTICE).is_symlink():
        raise ValueError("staging notice must not be a symlink")
    notice = (root / NOTICE).read_bytes()
    if len(notice) > 4096 or b"NOT INSTALLABLE" not in notice or \
            b"SELFREG_E_CLASS" not in notice:
        raise ValueError("T3a no-registration staging notice missing")
    results = []
    for arch, machine, magic in ARCHITECTURES:
        relative = Path("bin") / arch / "MeltiaTip.dll"
        path = root / relative
        if any((root / Path(*relative.parts[:i])).is_symlink()
               for i in range(1, len(relative.parts) + 1)):
            raise ValueError(f"{arch} artifact path contains symlink")
        if not path.is_file():
            raise ValueError(f"missing {arch} staged MeltiaTip.dll")
        size = path.stat().st_size
        if size <= 0 or size > MAX_DLL_BYTES:
            raise ValueError(f"{arch} artifact size out of bounds")
        payload = path.read_bytes()
        if len(payload) != size:
            raise ValueError(f"{arch} artifact changed while reading")
        parsed = inspect_pe(payload, machine, magic)
        results.append({"architecture": arch, "relative_path": relative.as_posix(),
                        "size_bytes": size, "sha256": hashlib.sha256(payload).hexdigest(),
                        **parsed})
    return {
        "phase": "T4-B4",
        "status": "structural-audit-only-not-authorized",
        "coverage": "staged-x64-x86-pe-headers-and-hashes-only",
        "registration_authorized": False,
        "native_dll_executed": False,
        "registry_writes": False,
        "installer_or_rollback_executed": False,
        "registration_stubs_proven": False,
        "build_provenance_proven": False,
        "artifacts": results,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--staging-root", type=Path, required=True,
                        help="existing T3a stage root containing bin/x64 and bin/x86; read only")
    args = parser.parse_args(argv)
    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        report = verify_staging_directory(args.staging_root, manifest)
        code = 0
    except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as exc:
        report = {"phase": "T4-B4", "status": "inconclusive",
                  "reason": (type(exc).__name__ if isinstance(exc, OSError)
                             else type(exc).__name__ + ": " + str(exc)),
                  "registration_authorized": False, "native_dll_executed": False,
                  "registry_writes": False, "installer_or_rollback_executed": False,
                  "registration_stubs_proven": False, "build_provenance_proven": False,
                  "artifacts": []}
        code = 2
    print(json.dumps(report, ensure_ascii=False, sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
