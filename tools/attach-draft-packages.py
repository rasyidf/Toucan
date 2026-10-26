#!/usr/bin/env python3
"""Attach a complete native package set to an existing unpublished preview draft."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


def prepare(folder, version, commit, release):
    tag = f'v{version}'
    if not release.get('isDraft') or not release.get('isPrerelease') or release.get('tagName') != tag:
        raise ValueError(f'{tag} must already exist as an unpublished preview draft')
    expected = {
        f'Toucan-{version}-portable-x64.zip',
        f'Toucan-{version}-linux-x64.tar.gz',
        f'Toucan-{version}-osx-arm64.dmg',
        f'Toucan-{version}-osx-x64.dmg',
    }
    actual = {p.name for p in folder.iterdir() if p.is_file()}
    if actual != expected:
        raise ValueError(f'Package set mismatch: missing {expected - actual}; unexpected {actual - expected}')
    if any((folder / name).stat().st_size == 0 for name in expected):
        raise ValueError('Empty package')
    hashes = {name: hashlib.sha256((folder / name).read_bytes()).hexdigest() for name in sorted(expected)}
    sums = folder / 'SHA256SUMS.txt'
    sums.write_text(''.join(f'{digest}  {name}\n' for name, digest in hashes.items()), encoding='ascii')
    provenance = folder / 'build-provenance.json'
    provenance.write_text(json.dumps({
        'version': version, 'sourceCommit': commit, 'sha256': hashes,
        'workflowRun': os.environ.get('GITHUB_RUN_ID'),
        'spellingMaterialsVerified': True,
        'nativeAcceptance': 'Packaging checks only; see the release acceptance record.',
    }, indent=2) + '\n', encoding='utf-8')
    return [folder / name for name in sorted(expected)] + [sums, provenance]


def main():
    root = Path(__file__).resolve().parents[1]
    version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
    tag = f'v{version}'
    release = json.loads(subprocess.check_output([
        'gh', 'release', 'view', tag, '--json', 'tagName,isDraft,isPrerelease',
    ], text=True, cwd=root))
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True, cwd=root).strip()
    files = prepare(root / 'dist', version, commit, release)
    subprocess.run(['gh', 'release', 'upload', tag, '--clobber', *map(str, files)], check=True, cwd=root)


if __name__ == '__main__':
    main()
