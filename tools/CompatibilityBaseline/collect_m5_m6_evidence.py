"""Final M5/M6 evidence; previous milestone snapshots and frozen binaries remain unchanged."""
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from collections import Counter
from datetime import datetime, timezone
from zipfile import ZipFile
from collect_evidence import ROOT, NS, rel, sha


def main():
    latest, attempts = {}, []
    for path in sorted((ROOT / "build/resource-compatibility/m5-m6/test-results").glob("m5-m6-verification*.trx")):
        tree = ET.parse(path)
        definitions = {n.attrib["id"]: n.find("t:TestMethod", NS).attrib for n in tree.findall("t:TestDefinitions/t:UnitTest", NS)}
        classes, failures = {}, []
        for node in tree.findall("t:Results/t:UnitTestResult", NS):
            cls = definitions[node.attrib["testId"]]["className"].split(",")[0]
            classes.setdefault(cls, Counter())[node.attrib["outcome"]] += 1
            if node.attrib["outcome"] != "Passed": failures.append(node.attrib["testName"])
        assemblies = tuple(sorted({d["codeBase"].replace("\\", "/").split("/")[-1] for d in definitions.values()}))
        summary = tree.find("t:ResultSummary", NS)
        suite = {"trx": rel(path), "sha256": sha(path), "assemblies": assemblies, "outcome": summary.attrib["outcome"],
                 "counters": {k: int(v) for k, v in summary.find("t:Counters", NS).attrib.items()},
                 "classes": {k: dict(v) for k, v in sorted(classes.items())}}
        latest[assemblies] = suite
        if failures: attempts.append({k: v for k, v in suite.items() if k != "classes"} | {"failures": failures})
    assert set(latest) == {("Toucan.Avalonia.Tests.dll",), ("Toucan.Core.Tests.dll",), ("Toucan.Plugins.Testing.Tests.dll",)}
    assert all(s["outcome"] == "Completed" and s["counters"]["passed"] == s["counters"]["total"] for s in latest.values())
    classes = {k: v for suite in latest.values() for k, v in suite["classes"].items()}
    assert classes["Toucan.Core.Tests.FrameworkCapabilitiesTests"]["Passed"] == 8
    assert classes["Toucan.Core.Tests.FrameworkSourceScannerTests"]["Passed"] == 51
    assert classes["Toucan.Avalonia.Tests.FrameworkIntegrationFlowTests"]["Passed"] == 4
    assert not subprocess.check_output(["git", "diff", "HEAD", "--", "samples/babeledit-parity"], cwd=ROOT)
    baseline_file = ROOT / "docs/specs/resource-compatibility/evidence/m0-baseline.json"
    baseline = json.loads(baseline_file.read_text(encoding="utf-8"))
    plugin_file = ROOT / "tests/fixtures/resource-compatibility/m0-sample-plugin.zip"
    with ZipFile(plugin_file) as archive:
        for artifact in baseline["frozenPluginArtifacts"]:
            assert hashlib.sha256(archive.read(artifact["path"].replace("\\", "/").split("/")[-1])).hexdigest() == artifact["sha256"]
    outputs = {}
    for label, name in [("framework", "build/resource-compatibility/m5-m6/cli-framework-integration.json"),
                        ("corpus", "build/babeledit-parity/toucan-read-results.json"),
                        ("exact", "build/resource-compatibility/m2/cli-exact-resources.json"),
                        ("fidelity", "build/resource-compatibility/m3/cli-fidelity.json")]:
        path = ROOT / name
        outputs[label] = {"source": name, "sha256": sha(path), "results": json.loads(path.read_text(encoding="utf-8"))}
    framework = outputs["framework"]["results"]
    assert framework["files"] == 200 and framework["references"] == 10000
    assert all(framework[k] for k in ["generatedSourceProtected", "targetSaveNativeReopen", "originalMigrationUnchanged", "commentsExcluded", "projectExcludes", "locations", "dynamicIncomplete", "cycleSkipped"])
    corpus = outputs["corpus"]["results"]
    assert len(corpus) == 19 and all(all(c.get(k) for k in ["unicode_exact_two_languages", "key_count_matches", "greeting_exact_two_languages", "unicode_unchanged_on_save"])
        and c["roundtrip_set"]["exit"] == 0 and not c["keys_lost_on_save"] and not c["keys_added_on_save"] for c in corpus)
    exact = outputs["exact"]["results"]
    assert len(exact) == 6 and all(c["valuesAndMetadataPreserved"] and c["sourceUnchanged"] and c["nativeReopenWithoutSource"] and c["unrelatedSiblingUnchanged"] for c in exact)
    fidelity = outputs["fidelity"]["results"]
    assert len(fidelity) == 4 and all(c["typedSettings"] and c["nativeReopen"] and c["literalNewKey"] and c["emptyPolicy"] and c["supportedOpaqueData"] for c in fidelity)
    subprocess.check_call(["python", ".agents/skills/doc-status/scripts/docs_status.py", "check"], cwd=ROOT)
    changed = subprocess.check_output(["git", "ls-files", "--modified", "--others", "--exclude-standard"], cwd=ROOT, text=True).splitlines()
    source = {name: sha(ROOT / name) for name in sorted(set(changed)) if (ROOT / name).is_file() and (ROOT / name).suffix in {".cs", ".csproj", ".py", ".slnx", ".axaml"}}
    payload = {"evidenceSchemaVersion": 1, "milestones": ["M5", "M6"], "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "workingTree": "M0–M2 committed in 7dc5d8c; preserved M3/M4 and new M5/M6 work remain uncommitted",
        "platform": "Windows x64 / Avalonia headless", "sdk": "10.0.401", "pluginApi": "1.2 (unreleased additive contract set)",
        "suites": list(latest.values()), "totalPassed": sum(s["counters"]["passed"] for s in latest.values()),
        "priorFailedAttempts": attempts, "sourceSha256": source, "corpusUnchangedFromHead": True,
        "frozenPlugin": {"archive": rel(plugin_file), "sha256": sha(plugin_file), "membersMatchM0Hashes": True},
        "cli": outputs,
        "coverage": {"capabilityCoreCases": 8, "scannerCoreCases": 51, "headlessFrameworkCases": 4,
            "legacyEditableFormats": 14, "additionalGeneratedFormat": "formatjs", "newDependencies": [],
            "protectedSourceEditableTargets": True, "profileImportPreflightAndNativeSettingsPriority": True,
            "nativeReopenAndSaveAs": True, "namespaceIsolationAndLocations": True, "incompleteUnusedGuard": True,
            "boundedParserAndIo": True, "cancelAndCycleDiagnostics": True, "configFormDraftPreservation": True},
        "limits": ["Windows x64 and Avalonia headless only; native OS pickers and other platforms not executed",
            "FormatJS string-defaultMessage extraction catalogs and uncompiled string targets only; compiled ASTs unsupported",
            "Static lexical parser, not full language compilation or cross-file data-flow analysis",
            "Complex scope aliases, dynamic keys/bindings/templates, generated IDs and recognized unsupported constructs are findings",
            "Generic fallback shows usages but cannot certify unused keys; no automatic source-unused deletion was added",
            "Measured Debug runtime is one corpus/run, not a cross-platform performance budget",
            "Initial writable-format assumptions and registration snapshots corrected to account for generated capabilities",
            "Existing settings-form file-lock race fixed by awaiting completion in tests; form saves preserve other drafts",
            "Pre-existing AVLN3001 MainWindow warning remains", "M7 spelling/provenance and final release verification remain open"]}
    output = ROOT / "docs/specs/resource-compatibility/evidence/m5-m6-verification.json"
    output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"M5/M6: {payload['totalPassed']} tests passed; 29 legacy CLI cases plus ownership, cycle and measured source fixtures. Evidence: {rel(output)}")


if __name__ == "__main__":
    main()
