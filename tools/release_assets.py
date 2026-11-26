#!/usr/bin/env python3
"""Prepares a release's assets: python3 tools/release_assets.py <dist folder> <version> [--notes FILE].

Checks that <dist folder> holds exactly the package set of a release (no package missing, none empty, nothing extra), writes
SHA256SUMS.txt and build-provenance.json next to them, and with --notes writes the release notes from the version's
CHANGELOG.md section (falling back to [Unreleased] for a release candidate). Used by .github/workflows/release.yml.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def expected(version: str) -> set[str]:
    rpm = version.replace("-", "~")
    return {
        f"Toucan-{version}-portable-x64.zip",
        f"ToucanSetup-{version}.exe",
        f"Toucan-{version}-osx-arm64.dmg",
        f"Toucan-{version}-osx-x64.dmg",
        f"Toucan-{version}-linux-x64.tar.gz",
        f"Toucan-{version}-linux-arm64.tar.gz",
        f"toucan_{version}_amd64.deb",
        f"toucan_{version}_arm64.deb",
        f"toucan-{rpm}-1.x86_64.rpm",
        f"toucan-{rpm}-1.aarch64.rpm",
    }


def notes(version: str) -> str:
    text = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    for heading in (re.escape(f"[{version}]"), re.escape("[Unreleased]")):
        match = re.search(rf"^## {heading}[^\n]*\n(.*?)(?=^## \[|\Z)", text, re.S | re.M)
        if match and match.group(1).strip():
            return match.group(1).strip() + "\n"
    return f"Toucan {version}.\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("dist", type=Path)
    parser.add_argument("version")
    parser.add_argument("--notes", type=Path)
    args = parser.parse_args()

    want = expected(args.version)
    have = {p.name for p in args.dist.iterdir() if p.is_file() and p.name not in {"SHA256SUMS.txt", "build-provenance.json"}}
    problems = [f"missing {name}" for name in sorted(want - have)] + [f"unexpected {name}" for name in sorted(have - want)]
    problems += [f"empty {name}" for name in sorted(want & have) if (args.dist / name).stat().st_size == 0]
    if problems:
        print("Package set is not complete:\n  " + "\n  ".join(problems), file=sys.stderr)
        return 1

    hashes = {name: hashlib.sha256((args.dist / name).read_bytes()).hexdigest() for name in sorted(want)}
    (args.dist / "SHA256SUMS.txt").write_text("".join(f"{digest}  {name}\n" for name, digest in hashes.items()), encoding="ascii")
    (args.dist / "build-provenance.json").write_text(json.dumps({
        "version": args.version,
        "sourceCommit": os.environ.get("GITHUB_SHA"),
        "workflowRun": os.environ.get("GITHUB_RUN_ID"),
        "sha256": hashes,
    }, indent=2) + "\n", encoding="utf-8")
    if args.notes:
        args.notes.write_text(notes(args.version) + "\nChecksums are in `SHA256SUMS.txt`; verify with `shasum -a 256 -c SHA256SUMS.txt`.\n", encoding="utf-8")
    print(f"{len(want)} packages checked; SHA256SUMS.txt written.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
