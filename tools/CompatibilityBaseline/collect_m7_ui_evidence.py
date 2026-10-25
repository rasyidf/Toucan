"""M7/UI evidence. Historical milestone files and the frozen plugin are never regenerated."""
import hashlib
import json
import platform
import struct
import subprocess
import xml.etree.ElementTree as ET
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
BUILD = ROOT / 'build/resource-compatibility/m7'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def rel(path): return path.relative_to(ROOT).as_posix()

def suite(path):
    tree = ET.parse(path)
    definitions = {n.attrib['id']: n.find('t:TestMethod', NS).attrib for n in tree.findall('t:TestDefinitions/t:UnitTest', NS)}
    classes = {}; failures = []
    for node in tree.findall('t:Results/t:UnitTestResult', NS):
        cls = definitions[node.attrib['testId']]['className'].split(',')[0]
        classes.setdefault(cls, Counter())[node.attrib['outcome']] += 1
        if node.attrib['outcome'] != 'Passed': failures.append(node.attrib['testName'])
    assemblies = sorted({d['codeBase'].replace('\\','/').split('/')[-1] for d in definitions.values()})
    summary = tree.find('t:ResultSummary', NS)
    return {'trx': rel(path), 'sha256': sha(path), 'assemblies': assemblies, 'outcome': summary.attrib['outcome'],
            'counters': {k:int(v) for k,v in summary.find('t:Counters', NS).attrib.items()},
            'classes': {k:dict(v) for k,v in sorted(classes.items())}, 'failures': failures}

def main():
    latest = {}
    for path in sorted((BUILD/'test-results').glob('m7-final-verification*.trx')):
        result = suite(path)
        if result['assemblies']: latest[tuple(result['assemblies'])] = result
    assert set(latest) == {('Toucan.Avalonia.Tests.dll',),('Toucan.Core.Tests.dll',),('Toucan.Plugins.Testing.Tests.dll',)}
    assert sum(s['counters']['passed'] for s in latest.values()) >= 1384
    assert all(s['counters']['total'] == s['counters']['passed'] and not s['failures'] for s in latest.values())
    classes = {k:v for s in latest.values() for k,v in s['classes'].items()}
    for name in ['Toucan.Avalonia.Tests.UiClosureFlowTests','Toucan.Avalonia.Tests.UiClosureRecoveryTests',
                 'Toucan.Core.Tests.LocalSpellingTests','Toucan.Core.Tests.ResourceConfigurationTests']:
        assert classes[name]['Passed'] > 0
    attempts = [suite(p) for p in sorted((BUILD/'test-results').glob('*.trx'))]
    baseline = json.loads((ROOT/'docs/specs/resource-compatibility/evidence/m0-baseline.json').read_text(encoding='utf-8'))
    frozen = ROOT/'tests/fixtures/resource-compatibility/m0-sample-plugin.zip'
    with ZipFile(frozen) as archive:
        for artifact in baseline['frozenPluginArtifacts']:
            assert hashlib.sha256(archive.read(artifact['path'].replace('\\','/').split('/')[-1])).hexdigest() == artifact['sha256']
    assert not subprocess.check_output(['git','diff','HEAD','--','samples/babeledit-parity'],cwd=ROOT)
    outputs = {}
    for label, name in [('corpus','build/babeledit-parity/toucan-read-results.json'),
                        ('exact','build/resource-compatibility/m2/cli-exact-resources.json'),
                        ('fidelity','build/resource-compatibility/m3/cli-fidelity.json'),
                        ('framework','build/resource-compatibility/m5-m6/cli-framework-integration.json')]:
        path=ROOT/name; outputs[label]={'path':name,'sha256':sha(path),'results':json.loads(path.read_text(encoding='utf-8'))}
    corpus=outputs['corpus']['results'];assert len(corpus)==19
    assert all(c['roundtrip_set']['exit']==0 and not c['keys_lost_on_save'] and not c['keys_added_on_save'] and c['unicode_unchanged_on_save'] for c in corpus)
    framework=outputs['framework']['results']
    assert framework['files']==200 and framework['references']==10000
    assert all(framework[k] for k in ['generatedSourceProtected','targetSaveNativeReopen','originalMigrationUnchanged','commentsExcluded','projectExcludes','locations','dynamicIncomplete','cycleSkipped'])
    packaging=[]
    for name in ['publish-folder-verification.json','publish-single-verification.json','publish-portable-verification.json','msix-probe-verification.json']:
        path=BUILD/name;record=json.loads(path.read_text(encoding='utf-8'));assert record['verified']
        packaging.append({'path':rel(path),'sha256':sha(path),'results':record})
    images=[]
    for path in sorted((BUILD/'ui').rglob('*.png')):
        width,height=struct.unpack('>II',path.read_bytes()[16:24]);images.append({'path':rel(path),'sha256':sha(path),'width':width,'height':height})
    assert len(images)>=5
    source_names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT).decode('utf-8').split('\0')
    prefixes=('Toucan.','tests/','tools/','packaging/','.github/workflows/','third-party/')
    suffixes=('.cs','.axaml','.csproj','.props','.py','.ps1','.sh','.yml')
    sources={name:sha(ROOT/name) for name in sorted(set(source_names)) if name and (name.startswith('third-party/') or name.startswith(prefixes) and name.endswith(suffixes)) and (ROOT/name).is_file()}
    docs=subprocess.check_output(['python','.agents/skills/doc-status/scripts/docs_status.py','check'],cwd=ROOT,text=True).strip()
    report={'createdUtc':datetime.now(timezone.utc).isoformat(),'clientDate':'2026-10-23','platform':platform.platform(),'machine':platform.machine(),
            'sdk':subprocess.check_output(['dotnet','--version'],cwd=ROOT,text=True).strip(),
            'baseCommit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
            'acceptedSlices':['U1','U2','U3','U4','U5','M7a','M7b'],'M7c':'partial; native and platform gate open',
            'passingTests':sum(s['counters']['passed'] for s in latest.values()),'finalSuites':list(latest.values()),
            'additionalRuns':[{k:v for k,v in s.items() if k!='classes'} for s in attempts if s['trx'] not in {v['trx'] for v in latest.values()}],
            'cli':outputs,'frozenPlugin':{'path':rel(frozen),'sha256':sha(frozen),'membersMatchM0':True},
            'originalCorpusUnchanged':True,'packaging':packaging,'screenshots':images,'sourceFingerprints':sources,'docsCheck':docs,
            'limitations':['Windows x64 execution only; configured Linux/macOS CI has not run in this session.',
                'Native pickers, installed/signed packages and interactive packaged recovery/correction workflows remain unverified.',
                'Unsigned MSIX probe checks component materials, not production assets, installation or signing.',
                'ReadyToRun/trimming/AOT, other runtime targets and architectures remain unverified.',
                'Bundled assembly identity is established from pinned inputs and the folder build; the guard does not extract the bundle.',
                'No dictionaries or automatic dictionary download catalog are shipped. Existing AVLN3001 warning remains.']}
    target=ROOT/'docs/specs/resource-compatibility/evidence/m7-ui-verification.json'
    target.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    print(f'{report["passingTests"]} passing tests; 7/8 slices accepted; M7c remains open; {rel(target)}')

if __name__=='__main__':main()
