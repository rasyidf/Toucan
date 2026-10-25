"""Capture the final M1 run without changing the frozen M0 baseline."""
import json
import subprocess
from collections import Counter
from datetime import datetime, timezone
from zipfile import ZipFile
import xml.etree.ElementTree as ET

from collect_evidence import ROOT, NS, rel, sha


def main():
    results = ROOT / "build/resource-compatibility/m1/test-results"
    latest = {}
    for path in sorted(results.glob("m1-verification*.trx")):
        tree = ET.parse(path)
        definitions = {n.attrib["id"]: n.find("t:TestMethod", NS).attrib
                       for n in tree.findall("t:TestDefinitions/t:UnitTest", NS)}
        classes = {}
        for node in tree.findall("t:Results/t:UnitTestResult", NS):
            cls = definitions[node.attrib["testId"]]["className"].split(",")[0]
            classes.setdefault(cls, Counter())[node.attrib["outcome"]] += 1
        assemblies = tuple(sorted({d["codeBase"].replace("\\", "/").split("/")[-1] for d in definitions.values()}))
        latest[assemblies] = {"trx": rel(path), "sha256": sha(path), "assemblies": assemblies,
                              "outcome": tree.find("t:ResultSummary", NS).attrib["outcome"],
                              "counters": {k: int(v) for k, v in tree.find("t:ResultSummary/t:Counters", NS).attrib.items()},
                              "classes": {k: dict(v) for k, v in sorted(classes.items())}}
    expected = {("Toucan.Avalonia.Tests.dll",), ("Toucan.Core.Tests.dll",), ("Toucan.Plugins.Testing.Tests.dll",)}
    assert set(latest) == expected, "Restore and execute all three test projects."
    assert all(s["outcome"] == "Completed" and s["counters"]["passed"] == s["counters"]["total"] for s in latest.values())

    cli_file = ROOT / "build/babeledit-parity/toucan-read-results.json"
    cli = json.loads(cli_file.read_text(encoding="utf-8"))
    checks = ("unicode_exact_two_languages", "key_count_matches", "greeting_exact_two_languages", "unicode_unchanged_on_save")
    assert len(cli) == 19 and all(all(c.get(k) for k in checks) and c["roundtrip_set"]["exit"] == 0 and
                                 not c["keys_lost_on_save"] and not c["keys_added_on_save"] for c in cli)
    assert not subprocess.check_output(["git", "diff", "HEAD", "--", "samples/babeledit-parity"], cwd=ROOT)
    baseline_file = ROOT / "docs/specs/resource-compatibility/evidence/m0-baseline.json"
    baseline = json.loads(baseline_file.read_text(encoding="utf-8"))
    archive_file = ROOT / "tests/fixtures/resource-compatibility/m0-sample-plugin.zip"
    with ZipFile(archive_file) as archive:
        import hashlib
        for artifact in baseline["frozenPluginArtifacts"]:
            name = artifact["path"].replace("\\", "/").split("/")[-1]
            assert hashlib.sha256(archive.read(name)).hexdigest() == artifact["sha256"]
    changed = subprocess.check_output(["git", "ls-files", "--modified", "--others", "--exclude-standard"], cwd=ROOT, text=True).splitlines()
    source_files = {name: sha(ROOT / name) for name in sorted(set(changed)) if (ROOT / name).suffix in {".cs", ".csproj", ".zip"}}
    payload = {
        "evidenceSchemaVersion": 1, "milestone": "M1", "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "workingTree": "M0 artifacts retained; M1 source changes not committed by this task", "platform": "Windows x64", "sdk": "10.0.401",
        "pluginApi": "1.2", "resourceSchema": 1, "suites": list(latest.values()),
        "totalPassed": sum(s["counters"]["passed"] for s in latest.values()),
        "sourceSha256": source_files,
        "cli": {"source": rel(cli_file), "sha256": sha(cli_file), "cases": [{"id": c["id"], "passed": True} for c in cli]},
        "corpusUnchangedFromHead": True,
        "frozenPlugin": {"archive": rel(archive_file), "sha256": sha(archive_file), "baselineEvidence": rel(baseline_file),
                         "membersMatchM0Hashes": True, "provenance": "Toucan MIT sample binaries, compiled at M0; no vendor or third-party runtime resources"},
        "limits": ["Windows/headless only", "JSON is the first exact-file adapter; non-JSON and importer separation remain M2",
                   "Full format options/fidelity remain M3", "One pre-existing UI settings config-sharing failure passed on complete UI rerun",
                   "Two pre-existing global documentation-header defects remain"]
    }
    target = ROOT / "docs/specs/resource-compatibility/evidence/m1-verification.json"
    target.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{rel(target)}: {payload['totalPassed']} tests; 19 CLI cases; frozen M0 hashes verified")


if __name__ == "__main__":
    main()
