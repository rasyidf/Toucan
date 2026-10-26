"""Verify exact spelling artifacts and notices in source or published output. No dictionary is distributable by default."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
REL = Path('third-party/WeCantSpell.Hunspell/7.0.1')
def notice(record):
    return f'''{record['id']} {record['version']}\nUpstream: {record['upstream']}\nSource commit: {record['commit']}\nToucan distributes the unmodified engine under its {record['chosenGrant'].replace('-', ' ', 1)} alternative.\nOriginal authors, contributors and copyright: see license.txt and source.zip.\nThe exact library source is provided in source.zip beside this notice.\nThe full chosen license is in MPL-1.1.txt. Other grants remain in license.txt.\nNo dictionary data, upstream icons or test corpora are included.\nLocal user dictionary imports have independent grants and are not release assets.\n'''

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def verify(folder, bundled=False, materials_folder=None):
    folder = Path(folder).resolve()
    app_bundle = folder.suffix == '.app'
    code = folder / 'Contents/MacOS' if app_bundle else folder
    materials = folder / 'Contents/Resources' if app_bundle else folder
    base = materials / REL
    record = json.loads((base / 'provenance.json').read_text(encoding='utf-8-sig'))
    assert record['id'] == 'WeCantSpell.Hunspell' and record['version'] == '7.0.1'
    assert record['chosenGrant'] == 'MPL-1.1' and record['runtimeTarget'] == 'net10.0'
    assert record['modified'] is False and record['runtimeDependencies'] == []
    assert record['commit'] == 'ab5709d95b2d23541984d22baa0ab2d1e783582f'
    for name, expected in record['files'].items():
        path = (base / name).resolve()
        assert path.is_relative_to(base.resolve()) and path.is_file(), name
        assert sha(path) == expected, f'changed artifact: {path}'
    assert (base / 'NOTICE.txt').read_text(encoding='utf-8') == notice(record)
    with ZipFile(base / 'source.zip') as source:
        assert 'WeCantSpell.Hunspell/WordList.cs' in source.namelist()
        assert source.read('license.txt') == (base / 'license.txt').read_bytes()
        assert all(n.startswith('WeCantSpell.Hunspell/') or n in ['license.txt','readme.md','changelog.md','global.json'] for n in source.namelist())
        assert not any(n.lower().endswith(('.aff','.dic','.png','.jpg')) or '..' in Path(n).parts for n in source.namelist())
    dll = code / 'WeCantSpell.Hunspell.dll'
    if folder != ROOT and not bundled:
        assert dll.is_file() and sha(dll) == record['assemblySha256'], 'missing/changed net10.0 engine'
    if folder != ROOT:
        assert not any(p.suffix.lower() in ['.aff','.dic','.xcu'] for p in folder.rglob('*')), 'unapproved dictionary in release output'
    return {'folder': str(folder), 'engine': record['id'], 'version':record['version'], 'license':record['chosenGrant'],
            'sourceSha256':sha(base/'source.zip'), 'noticeSha256':sha(base/'NOTICE.txt'), 'assemblyMode':'bundled; assembly verified from build separately' if bundled else 'separate assembly', 'applicationHashes':{p.name:sha(p) for p in [code/'Toucan.dll',code/'Toucan.exe'] if p.is_file()}, 'verified':True}

if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--folder',type=Path,default=ROOT);parser.add_argument('--bundled',action='store_true');parser.add_argument('--output',type=Path)
    parser.add_argument('--materials-folder',type=Path,help='Separate source/notice root, e.g. macOS Contents/Resources')
    parser.add_argument('--write-notice',action='store_true');args=parser.parse_args()
    if args.write_notice:
        assert args.folder.resolve() == ROOT, 'only generate the source notice'
        record=json.loads((ROOT/REL/'provenance.json').read_text(encoding='utf-8'))
        (ROOT/REL/'NOTICE.txt').write_text(notice(record),encoding='utf-8')
    result=verify(args.folder,args.bundled,args.materials_folder)
    if args.output: args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result))
