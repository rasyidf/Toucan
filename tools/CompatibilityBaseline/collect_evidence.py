"""Collect executed M0 evidence; refuses incomplete/failed three-project test runs.

Run from the repository root after the commands in acceptance.md.
Raw binaries, TRX files and mutable projects remain under ignored build/.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
from collections import Counter
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "build/resource-compatibility/m0"
DOC = ROOT / "docs/specs/resource-compatibility/evidence"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def rel(path):
    return path.relative_to(ROOT).as_posix()


def main():
    baseline_commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    previous = DOC / "m0-baseline.json"
    if previous.exists() and json.loads(previous.read_text(encoding="utf-8"))["baselineCommit"] != baseline_commit:
        raise RuntimeError("HEAD differs from the frozen M0 commit; use a new baseline directory instead of overwriting M0.")
    suites = []
    for path in sorted((OUT / "test-results").glob("m0-complete*.trx")):
        tree = ET.parse(path)
        definitions = {node.attrib["id"]: node.find("t:TestMethod", NS).attrib
                       for node in tree.findall("t:TestDefinitions/t:UnitTest", NS)}
        groups = {}
        for node in tree.findall("t:Results/t:UnitTestResult", NS):
            definition = definitions[node.attrib["testId"]]
            group = definition["className"].split(",")[0]
            groups.setdefault(group, Counter())[node.attrib["outcome"]] += 1
        counters = tree.find("t:ResultSummary/t:Counters", NS).attrib
        summary = tree.find("t:ResultSummary", NS).attrib["outcome"]
        assemblies = sorted({Path(d["codeBase"]).name for d in definitions.values()})
        suites.append({"trx": rel(path), "sha256": sha(path), "assemblies": assemblies,
                       "outcome": summary, "counters": {k: int(v) for k, v in counters.items()},
                       "classes": {k: dict(v) for k, v in sorted(groups.items())}})
    # Keep only the latest result per assembly if this command was repeated.
    latest = {tuple(s["assemblies"]): s for s in suites}
    suites = list(latest.values())
    expected = {("Toucan.Avalonia.Tests.dll",), ("Toucan.Core.Tests.dll",), ("Toucan.Plugins.Testing.Tests.dll",)}
    if set(latest) != expected or any(s["outcome"] != "Completed" or s["counters"]["failed"] or
                               s["counters"]["passed"] != s["counters"]["total"] for s in suites):
        raise RuntimeError("Need three fully passing m0-complete TRX suites; inspect raw results.")

    probe_path = max((OUT / "probes").glob("*/observations.json"), key=lambda p: p.parent.name)
    probes = json.loads(probe_path.read_text(encoding="utf-8"))
    if len(probes["results"]) != 10 or any(p["execution"] != "completed" for p in probes["results"]):
        raise RuntimeError("Incomplete probe execution; inspect observations.json.")

    corpus = ROOT / "samples/babeledit-parity"
    fixtures = []
    for folder in sorted(p for p in corpus.iterdir() if p.is_dir()):
        document = ET.parse(folder / "project.babel").getroot()
        fixtures.append({"id": folder.name, "schema": document.attrib.get("version"),
                         "authoringApplicationVersion": document.attrib.get("be_version"),
                         "framework": document.findtext("framework"),
                         "configuration": {e.tag: e.text for e in document.findall("configuration/*")},
                         "files": [{"path": rel(p), "sha256": sha(p)} for p in sorted(folder.rglob("*")) if p.is_file()]})

    frozen = OUT / "frozen-plugin"
    frozen.mkdir(parents=True, exist_ok=True)
    artifacts = []
    for name in ("Toucan.Sample.Plugin.dll", "Toucan.Sample.Plugin.Desktop.dll", "plugin.json"):
        source = ROOT / "build/Toucan.Sample.Plugin/Debug/net10.0" / name
        target = frozen / name
        if target.exists() and sha(target) != sha(source):
            raise RuntimeError("Frozen baseline artifact differs; preserve it and start a new baseline directory.")
        shutil.copy2(source, target)
        artifacts.append({"path": rel(target), "sha256": sha(target)})
    git = lambda *args: subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()
    corpus_diff = git("diff", "HEAD", "--", "samples/babeledit-parity")
    if corpus_diff:
        raise RuntimeError("Corpus differs from baseline HEAD.")
    cli_path = ROOT / "build/babeledit-parity/toucan-read-results.json"
    cli = json.loads(cli_path.read_text(encoding="utf-8"))
    cli_checks = ["unicode_exact_two_languages", "key_count_matches", "greeting_exact_two_languages", "unicode_unchanged_on_save"]
    cli_results = [{"id": c["id"], **{k: c.get(k, False) for k in cli_checks},
                    "keys_lost_on_save": c.get("keys_lost_on_save"), "keys_added_on_save": c.get("keys_added_on_save"),
                    "save_exit": c.get("roundtrip_set", {}).get("exit")} for c in cli]
    if len(cli_results) != 19 or any(not all(c[k] for k in cli_checks) or c["save_exit"] != 0 or
                                   c["keys_lost_on_save"] or c["keys_added_on_save"] for c in cli_results):
        raise RuntimeError("CLI corpus audit is incomplete or failed; inspect its raw JSON.")

    payload = {"evidenceSchemaVersion": 1, "baselineCommit": baseline_commit,
               "generatedUtc": datetime.now(timezone.utc).isoformat(),
               "platform": "Windows x64", "sdk": git_dotnet_version(),
               "suites": suites, "totalPassed": sum(s["counters"]["passed"] for s in suites),
               "probeSource": rel(probe_path), "probeSourceSha256": sha(probe_path), "probes": probes,
               "fixtureProvenance": "Original synthetic translations; user-generated BabelEdit XML seed and workbook; generated variants. See corpus README. No installed vendor demo/assets added.",
               "fixtures": fixtures, "corpusUnchangedFromHead": True,
               "exchangeWorkbook": {"path": rel(corpus / "babeledit-export.xlsx"), "sha256": sha(corpus / "babeledit-export.xlsx")},
               "cli": {"source": rel(cli_path), "sha256": sha(cli_path), "cases": cli_results},
               "frozenPluginArtifacts": artifacts,
               "pluginLimit": "Sample built at the baseline before contract changes; not certification of an independently published historical plugin binary."}
    DOC.mkdir(parents=True, exist_ok=True)
    target = DOC / "m0-baseline.json"
    target.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{rel(target)}: {payload['totalPassed']} tests, {len(fixtures)} fixture projects, 10 probes, 19 CLI cases")


def git_dotnet_version():
    return subprocess.check_output(["dotnet", "--version"], cwd=ROOT, text=True).strip()


if __name__ == "__main__":
    main()
