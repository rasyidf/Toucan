"""Read original fixtures, then test native CLI saves only in isolated copies."""
import json
import os
from pathlib import Path
import subprocess
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / 'samples/babeledit-parity'
OUT = ROOT / 'build/babeledit-parity'
CLI = ROOT / 'build/Toucan.CLI/Debug/net10.0/toucan.dll'


def run(command, folder, *args):
    env = dict(os.environ, TOUCAN_PLUGINS_DIR=str(OUT / 'empty-plugins'))
    result = subprocess.run(['dotnet', str(CLI), command, str(folder), *args],
                            capture_output=True, encoding='utf-8', env=env)
    return {'exit': result.returncode, 'stdout': result.stdout.strip(), 'stderr': result.stderr.strip()}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    results = []
    roundtrip_root = Path(tempfile.mkdtemp(prefix='roundtrip-', dir=OUT))
    for case in json.loads((FIXTURES / 'manifest.json').read_text()):
        folder = FIXTURES / case['id']
        keys = run('list-keys', folder)
        unicode_key = next((k for k in keys['stdout'].splitlines() if k.endswith('unicode')), 'unicode')
        welcome_key = next((k for k in keys['stdout'].splitlines() if k.endswith('welcome')), 'welcome')
        unicode = run('get', folder, unicode_key)
        welcome = run('get', folder, welcome_key)
        try:
            texts = json.loads(unicode['stdout'])
            unicode_ok = len(texts) == 2 and all(v == '日本語 — café 😀' for v in texts.values())
        except (ValueError, AttributeError):
            unicode_ok = False
        result = {'id': case['id'], 'keys': keys, 'unicode': unicode, 'welcome': welcome,
                  'unicode_exact_two_languages': unicode_ok,
                  'scope': 'Native project loading (including .babel when present), followed by an isolated CLI save/reload'}
        result['expected_key_count'] = 13 if case['kind'] == 'arrays' else 11 if case['kind'] == 'yaml-arrays' else 10 if case['kind'] == 'arb' else 9
        result['key_count_matches'] = keys['exit'] == 0 and len(keys['stdout'].splitlines()) == result['expected_key_count']
        try:
            result['greeting_exact_two_languages'] = json.loads(welcome['stdout']) == {'en-US': 'Hello {name}', 'de-DE': 'Hallo {name}'}
        except ValueError:
            result['greeting_exact_two_languages'] = False
        copy = roundtrip_root / case['id']
        shutil.copytree(folder, copy)
        # Change a synthetic value only in the isolated copy; observe save/reload.
        language = 'en-US'
        if unicode_ok and result['key_count_matches'] and result['greeting_exact_two_languages']:
            result['roundtrip_set'] = run('set', copy, unicode_key, language, '日本語 — café 😀')
            result['roundtrip_keys'] = run('list-keys', copy)
            result['keys_lost_on_save'] = sorted(set(keys['stdout'].splitlines()) - set(result['roundtrip_keys']['stdout'].splitlines()))
            result['keys_added_on_save'] = sorted(set(result['roundtrip_keys']['stdout'].splitlines()) - set(keys['stdout'].splitlines()))
            result['roundtrip_unicode'] = run('get', copy, unicode_key)
            result['unicode_unchanged_on_save'] = result['roundtrip_unicode']['stdout'] == unicode['stdout']
        else:
            result['roundtrip_skipped'] = 'Read semantics already differ; do not save corrupted values.'
        results.append(result)
        print(case['id'], 'keys=' + str(len(keys['stdout'].splitlines())), 'unicode=' + str(unicode_ok), flush=True)
    (OUT / 'toucan-read-results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (OUT / 'roundtrip-location.txt').write_text(str(roundtrip_root), encoding='utf-8')
    print('Results:', OUT / 'toucan-read-results.json')
    failures = [r['id'] for r in results if not (r['key_count_matches'] and r['unicode_exact_two_languages'] and
                r['greeting_exact_two_languages'] and r.get('roundtrip_set', {}).get('exit') == 0 and
                not r.get('keys_lost_on_save') and not r.get('keys_added_on_save') and r.get('unicode_unchanged_on_save'))]
    print('Failed scenarios:', failures)
    return bool(failures)


if __name__ == '__main__':
    raise SystemExit(main())
