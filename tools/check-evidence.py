#!/usr/bin/env python3
"""Checks that docs/evidence/ has a passing record for every release requirement: python3 tools/check-evidence.py [--commit REF].

A requirement is <check>@<OS>, optionally :packaged (the check used an installed build) or :native (not WSL). A record counts for
the release commit when it names that commit, or an ancestor whose difference to it lies only in docs/evidence/ (committing a
record makes a new commit). Records made with uncommitted changes never count. When a requirement has several records, the latest
decides, so a later failure is not hidden by an earlier pass. Exits 1 when a requirement is missing or failed.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from functools import cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "docs" / "evidence"

# The release checklist (docs/release-checklist.md) lists the same requirements with what each check covers.
TESTS = ["tests@macOS", "tests@Windows", "tests@Linux"]
RELEASE = TESTS + [
    "packaged-flow@macOS:packaged", "packaged-flow@Windows:packaged", "packaged-flow@Linux:packaged",
    "install-upgrade@macOS:packaged", "install-upgrade@Windows:packaged", "install-upgrade@Linux:packaged",
    "gatekeeper@macOS:packaged",
    "keyring@macOS:native", "keyring@Windows:native", "keyring@Linux:native",
    "screen-reader@macOS", "screen-reader@Windows", "screen-reader@Linux",
]


@dataclass(frozen=True)
class Requirement:
    check: str
    family: str
    packaged: bool
    native: bool

    @staticmethod
    def parse(text: str) -> "Requirement":
        head, *flags = text.split(":")
        check, family = head.split("@")
        return Requirement(check, family, "packaged" in flags, "native" in flags or "packaged" in flags)

    def __str__(self) -> str:
        return f"{self.check}@{self.family}" + (":packaged" if self.packaged else ":native" if self.native else "")


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()


@cache
def counts_for(commit: str, target: str) -> bool:
    """Whether a record of `commit` stands for `target`: the same code, differing at most in docs/evidence/."""
    if commit == target:
        return True
    try:
        if subprocess.run(["git", "merge-base", "--is-ancestor", commit, target], cwd=ROOT).returncode != 0:
            return False
        changed = [p for p in git("diff", "--name-only", commit, target).splitlines() if p]
    except subprocess.CalledProcessError:
        return False
    return all(p.startswith("docs/evidence/") for p in changed)


def records() -> list[dict]:
    found = []
    for path in sorted(EVIDENCE.rglob("*.json")):
        if path.name == "schema.json":
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        if isinstance(data, dict) and data.get("schema") == 1:
            data["_path"] = path.relative_to(ROOT).as_posix()
            found.append(data)
    return found


def satisfies(record: dict, requirement: Requirement, target: str) -> bool:
    return (record.get("check") == requirement.check and record.get("os", {}).get("family") == requirement.family
            and not record.get("dirty", False) and counts_for(record.get("commit", ""), target)
            and (not requirement.packaged or record.get("packaged") is True)
            and (not requirement.native or record.get("environment") != "wsl"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--commit", default="HEAD", help="the release commit (default HEAD)")
    parser.add_argument("--only-tests", action="store_true", help="require only the automated test records")
    parser.add_argument("--require", action="append", default=[], help="an extra requirement such as gatekeeper@macOS:packaged")
    args = parser.parse_args()

    target = git("rev-parse", args.commit)
    requirements = [Requirement.parse(r) for r in (TESTS if args.only_tests else RELEASE) + args.require]
    all_records = records()
    missing = 0
    print(f"Evidence for {target[:12]} ({len(all_records)} records in docs/evidence):")
    for requirement in requirements:
        matches = sorted((r for r in all_records if satisfies(r, requirement, target)), key=lambda r: r.get("recordedAt", ""))
        if not matches:
            print(f"  MISSING {requirement}")
            missing += 1
            continue
        latest = matches[-1]
        ok = latest.get("result") == "passed"
        counts = latest.get("counts") or {}
        detail = f"{counts.get('passed')}/{counts.get('total')} passed" if counts else latest.get("notes", "")[:60]
        print(f"  {'ok' if ok else 'FAILED':7} {requirement}  {latest['_path']}  {latest.get('recordedBy', '')}  {detail}")
        missing += 0 if ok else 1
    print("All release evidence is present." if missing == 0 else f"{missing} requirement(s) missing or failed.")
    return 0 if missing == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
