"""Checks for the release uploader's publication and package-completeness guards."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('draft', ROOT / 'tools/attach-draft-packages.py')
draft = importlib.util.module_from_spec(spec)
spec.loader.exec_module(draft)


class DraftPackagesTests(unittest.TestCase):
    def setUp(self):
        (ROOT / 'build').mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT / 'build')
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.release = {'tagName': 'v0.24.0', 'isDraft': True, 'isPrerelease': True}
        for name in ['Toucan-0.24.0-portable-x64.zip', 'Toucan-0.24.0-linux-x64.tar.gz',
                     'Toucan-0.24.0-osx-arm64.dmg', 'Toucan-0.24.0-osx-x64.dmg']:
            (self.folder / name).write_bytes(b'package fixture')

    def test_refuses_published_stable_or_wrong_version_release(self):
        for overrides in [{'isDraft': False}, {'isPrerelease': False}, {'tagName': 'v0.23.0'}]:
            with self.subTest(overrides=overrides), self.assertRaises(ValueError):
                draft.prepare(self.folder, '0.24.0', 'commit', {**self.release, **overrides})
        self.assertFalse((self.folder / 'SHA256SUMS.txt').exists())

    def test_refuses_incomplete_set(self):
        (self.folder / 'Toucan-0.24.0-osx-x64.dmg').unlink()
        with self.assertRaises(ValueError):
            draft.prepare(self.folder, '0.24.0', 'commit', self.release)

    def test_refuses_unexpected_or_empty_packages(self):
        extra = self.folder / 'Toucan-0.23.0-linux-x64.tar.gz'
        extra.write_bytes(b'old release')
        with self.assertRaises(ValueError):
            draft.prepare(self.folder, '0.24.0', 'commit', self.release)
        extra.unlink()
        (self.folder / 'Toucan-0.24.0-osx-x64.dmg').write_bytes(b'')
        with self.assertRaises(ValueError):
            draft.prepare(self.folder, '0.24.0', 'commit', self.release)

    def test_checksums_cover_every_package(self):
        files = draft.prepare(self.folder, '0.24.0', 'commit', self.release)
        self.assertEqual(len(files), 6)
        lines = (self.folder / 'SHA256SUMS.txt').read_text().splitlines()
        self.assertEqual(len(lines), 4)
        for line in lines:
            digest, name = line.split('  ')
            self.assertEqual(digest, hashlib.sha256((self.folder / name).read_bytes()).hexdigest())


if __name__ == '__main__':
    unittest.main()
