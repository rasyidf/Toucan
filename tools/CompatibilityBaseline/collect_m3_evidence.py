"""Capture the final M3 run without changing the frozen M0 baseline."""
import json
import subprocess
from collections import Counter
from datetime import datetime, timezone
from zipfile import ZipFile
import xml.etree.ElementTree as ET

from collect_evidence import ROOT, NS, rel, sha


def main():
    results = ROOT / "build/resource-compatibility/m3/test-results"
    latest = {}
    for path in sorted(results.glob("m3-verification*.trx")):
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
    exact_file = ROOT / "build/resource-compatibility/m2/cli-exact-resources.json"
    exact = json.loads(exact_file.read_text(encoding="utf-8"))
    assert len(exact) == 6 and all(c["valuesAndMetadataPreserved"] and c["sourceUnchanged"] and
                                 c["nativeReopenWithoutSource"] and c["unrelatedSiblingUnchanged"] for c in exact)
    subprocess.check_call(["python", ".agents/skills/doc-status/scripts/docs_status.py", "check"], cwd=ROOT)
    fidelity_file = ROOT / "build/resource-compatibility/m3/cli-fidelity.json"
    fidelity = json.loads(fidelity_file.read_text(encoding="utf-8"))
    assert len(fidelity) == 4 and all(c["typedSettings"] and c["nativeReopen"] and c["literalNewKey"] and c["emptyPolicy"] and c["supportedOpaqueData"] for c in fidelity)
    changed = subprocess.check_output(["git", "ls-files", "--modified", "--others", "--exclude-standard"], cwd=ROOT, text=True).splitlines()
    source_files = {name: sha(ROOT / name) for name in sorted(set(changed)) if (ROOT / name).exists() and (ROOT / name).suffix in {".cs", ".csproj", ".zip", ".py", ".slnx"}}
    payload = {
        "evidenceSchemaVersion": 1, "milestone": "M3", "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "workingTree": "M0–M2 committed in 7dc5d8c; M3 source changes remain uncommitted", "platform": "Windows x64", "sdk": "10.0.401",
        "pluginApi": "1.2", "resourceSchema": 1, "suites": list(latest.values()),
        "totalPassed": sum(s["counters"]["passed"] for s in latest.values()),
        "sourceSha256": source_files,
        "cli": {"source": rel(cli_file), "sha256": sha(cli_file), "cases": [{"id": c["id"], "passed": True} for c in cli]},
        "corpusUnchangedFromHead": True,
        "frozenPlugin": {"archive": rel(archive_file), "sha256": sha(archive_file), "baselineEvidence": rel(baseline_file),
                         "membersMatchM0Hashes": True, "provenance": "Toucan MIT sample binaries, compiled at M0; no vendor or third-party runtime resources"},
        "limits": ["Windows x64 and Avalonia headless only; other platforms not executed",
                   "Exact migration supports JSON, YAML, ARB, Java properties, RESX/RESW and Laravel PHP",
                   "PO/XLIFF exact migration is explicitly unsupported; legacy folder APIs remain available",
                   "Restricted YAML block dialect; unsupported input is review-only",
                   "Legacy folder-only APIs retain their documented formatting limits",
                   "M4–M7 remain unaccepted",
                   "Known pre-existing Avalonia AVLN3001 constructor warning remains"],
        "fidelityCli": {"source": rel(fidelity_file), "sha256": sha(fidelity_file), "cases": fidelity},
        "exactCli": {"source": rel(exact_file), "sha256": sha(exact_file), "cases": exact},
        "docs": {"globalHeaders": "passed", "generatedFormatMatrix": "verified by FormatSupportTests.MatrixDocumentIsUpToDate",
                 "repairs": ["Historical review YAML header", "Generated format matrix updated field and its generator"]}

    }
    target = ROOT / "docs/specs/resource-compatibility/evidence/m3-verification.json"
    target.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{rel(target)}: {payload['totalPassed']} tests; 19 corpus, 6 exact-resource and 4 fidelity CLI cases; frozen M0 hashes verified")


if __name__ == "__main__":
    main()
