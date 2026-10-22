#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltia contributors
"""Bounded, offline evidence byte reader shared by B5/C2/C4.

Reject special files and aliases before opening, and verify the opened file.
POSIX O_NONBLOCK|O_NOFOLLOW prevents a FIFO or symlink swap from hanging.
This is not atomic or a substitute for independently authenticated evidence.
"""
import os
from pathlib import Path
import stat

MAX_EVIDENCE_BYTES = 131072


def read_regular_bytes(path, limit=MAX_EVIDENCE_BYTES):
    if type(limit) is not int or limit <= 0:
        raise ValueError("invalid evidence read limit")
    target = Path(path).absolute()
    if ".." in target.parts:
        raise ValueError("path traversal")
    for part in (target, *target.parents):
        if (part.is_symlink() or
                (hasattr(part, "is_junction") and part.is_junction())):
            raise ValueError("evidence path must be an ordinary file (alias)")

    prior = target.lstat()
    if not stat.S_ISREG(prior.st_mode) or not 0 < prior.st_size <= limit:
        raise ValueError("evidence file size or type outside limit")
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0)
    if os.name == "posix":
        if not hasattr(os, "O_NOFOLLOW") or not hasattr(os, "O_NONBLOCK"):
            raise ValueError("safe nonblocking open unavailable")
        flags |= os.O_NOFOLLOW | os.O_NONBLOCK
    with os.fdopen(os.open(target, flags), "rb") as stream:
        opened = os.fstat(stream.fileno())
        def identity(value):
            return (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)
        if (not stat.S_ISREG(opened.st_mode) or not 0 < opened.st_size <= limit
                or identity(prior) != identity(opened)):
            raise ValueError("evidence replaced before read")
        raw = stream.read(limit + 1)
        finished = os.fstat(stream.fileno())
        current = target.lstat()
        if (len(raw) != opened.st_size or identity(opened) != identity(finished)
                or identity(finished) != identity(current)
                or not stat.S_ISREG(current.st_mode)):
            raise ValueError("evidence changed while reading")
    return raw
