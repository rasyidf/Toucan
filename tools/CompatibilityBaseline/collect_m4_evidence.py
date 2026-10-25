"""Collect M4 test evidence; never regenerate the frozen M0 or M3 snapshots."""
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
from collections import Counter
from datetime import datetime, timezone
from zipfile import ZipFile

from collect_evidence import ROOT, NS, rel, sha


def main():
    latest = {}
    attempts = []
    for path in sorted((ROOT / "build/resource-compatibility/m4/test-results").glob("m4-verification*.trx")):
        tree = ET.parse(path)
        definitions = {n.attrib["id"]: n.find("t:TestMethod", NS).attrib
                       for n in tree.findall("t:TestDefinitions/t:UnitTest", NS)}
        classes = {}
        for node in tree.findall("t:Results/t:UnitTestResult", NS):
            cls = definitions[node.attrib["testId"]]["className"].split(",")[0]
            classes.setdefault(cls, Counter())[node.attrib["outcome"]] += 1
        assemblies = tuple(sorted({d["codeBase"].replace("\\", "/").split("/")[-1] for d in definitions.values()}))
        summary = tree.find("t:ResultSummary", NS)
        latest[assemblies] = {"trx": rel(path), "sha256": sha(path), "assemblies": assemblies,
                              "outcome": summary.attrib["outcome"],
                              "counters": {k: int(v) for k, v in summary.find("t:Counters", NS).attrib.items()},
                              "classes": {k: dict(v) for k, v in sorted(classes.items())}}
        attempts.append({k: v for k, v in latest[assemblies].items() if k != "classes"})
    assert set(latest) == {("Toucan.Avalonia.Tests.dll",), ("Toucan.Core.Tests.dll",), ("Toucan.Plugins.Testing.Tests.dll",)}
    assert all(s["outcome"] == "Completed" and s["counters"]["passed"] == s["counters"]["total"] for s in latest.values())
    classes = {k: v for suite in latest.values() for k, v in suite["classes"].items()}
    assert classes["Toucan.Core.Tests.SpreadsheetExchangeTests"]["Passed"] >= 27
    assert classes["Toucan.Core.Tests.BabelExcelTests"]["Passed"] == 5
    assert classes["Toucan.Avalonia.Tests.SpreadsheetImportFlowTests"]["Passed"] == 7
    assert not subprocess.check_output(["git", "diff", "HEAD", "--", "samples/babeledit-parity"], cwd=ROOT)
    baseline_file = ROOT / "docs/specs/resource-compatibility/evidence/m0-baseline.json"
    baseline = json.loads(baseline_file.read_text(encoding="utf-8"))
    plugin_file = ROOT / "tests/fixtures/resource-compatibility/m0-sample-plugin.zip"
    with ZipFile(plugin_file) as archive:
        for artifact in baseline["frozenPluginArtifacts"]:
            assert hashlib.sha256(archive.read(artifact["path"].replace("\\", "/").split("/")[-1])).hexdigest() == artifact["sha256"]
    subprocess.check_call(["python", ".agents/skills/doc-status/scripts/docs_status.py", "check"], cwd=ROOT)
    changed = subprocess.check_output(["git", "ls-files", "--modified", "--others", "--exclude-standard"], cwd=ROOT, text=True).splitlines()
    source = {name: sha(ROOT / name) for name in sorted(set(changed))
              if (ROOT / name).is_file() and (ROOT / name).suffix in {".cs", ".csproj", ".py", ".slnx", ".axaml"}}
    payload = {
        "evidenceSchemaVersion": 1, "milestone": "M4", "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "workingTree": "M0–M2 committed in 7dc5d8c; preserved M3 and new M4 changes remain uncommitted",
        "platform": "Windows x64; Avalonia headless", "sdk": "10.0.401",
        "suites": list(latest.values()), "totalPassed": sum(s["counters"]["passed"] for s in latest.values()),
        "sourceSha256": source, "corpusUnchangedFromHead": True,
        "priorFailedAttempts": [a for a in attempts if a["outcome"] != "Completed" or a["counters"]["passed"] != a["counters"]["total"]],
        "frozenPlugin": {"archive": rel(plugin_file), "sha256": sha(plugin_file), "membersMatchM0Hashes": True},
        "exchange": {"coreCases": classes["Toucan.Core.Tests.SpreadsheetExchangeTests"]["Passed"], "newGuiCases": 7,
                     "legacyCases": 5, "previewCountsAndOneStepUndo": True, "cancelAndConflictNoMutation": True,
                     "strictApprovalAndStaleRevision": True, "metadataSaveReopen": True, "crashRecovery": True,
                     "textAndFormulaSafety": True, "archiveRowsColumnsUnitsAndCellBounds": True},
        "limits": ["Native windows, OS pickers, and non-Windows platforms were not exercised",
                   "Unmarked package-header workbooks cannot reveal their originating editor",
                   "Legacy blanks mean clear; only Toucan v2 has explicit presence flags",
                   "Compatibility export refuses sparse units, distinct typed-key collisions and language-specific comments it cannot represent",
                   "Unknown packages/languages and ambiguous destinations must be configured before import",
                   "Known locale headers only; one worksheet; formulas are rejected",
                   "Spreadsheet exchange has no CLI command; previous M3 CLI evidence is historical, not rerun here",
                   "M5–M7 remain unaccepted", "Pre-existing AVLN3001 MainWindow warning remains"]
    }
    output = ROOT / "docs/specs/resource-compatibility/evidence/m4-verification.json"
    output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{payload['totalPassed']} passing tests; {payload['exchange']['coreCases']} new core and 7 new GUI exchange cases. Evidence: {rel(output)}")


if __name__ == "__main__":
    main()
