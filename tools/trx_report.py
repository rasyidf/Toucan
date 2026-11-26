#!/usr/bin/env python3
"""Summarise the TRX files `dotnet test --logger trx` writes: python3 tools/trx_report.py <results folder> [options].

Writes any of: --junit FILE (JUnit XML, one <testsuite> per TRX), --markdown FILE (a table for the CI job summary; "-" for
stdout), --json FILE (the counts, read by tools/record-evidence). Exits 1 when a test failed or no TRX file was found, so a CI
step fails on a red run even if the test step's exit code was ignored. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
FAILED_OUTCOMES = {"Failed", "Error", "Timeout", "Aborted"}


def read(trx: Path) -> dict:
    root = ET.parse(trx).getroot()
    counters = root.find("t:ResultSummary/t:Counters", NS)
    attr = (lambda name: int(counters.get(name, "0"))) if counters is not None else (lambda name: 0)
    definitions = {d.get("id"): d for d in root.iterfind("t:TestDefinitions/t:UnitTest", NS)}
    results = []
    for result in root.iterfind("t:Results/t:UnitTestResult", NS):
        test = definitions.get(result.get("testId"))
        method = test.find("t:TestMethod", NS) if test is not None else None
        message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
        results.append({
            "name": result.get("testName", ""),
            "class": method.get("className", "") if method is not None else "",
            "outcome": result.get("outcome", ""),
            "seconds": duration(result.get("duration", "00:00:00")),
            "message": message.strip(),
        })
    total = attr("total")
    executed = attr("executed")
    failed = attr("failed") + attr("error") + attr("timeout") + attr("aborted")
    # Skipped tests are counted as not executed.
    return {
        "file": trx.name,
        "assembly": assembly_name(root, trx),
        "total": total,
        "passed": attr("passed"),
        "failed": failed,
        "skipped": max(total - executed, 0),
        "results": results,
    }


def assembly_name(root: ET.Element, trx: Path) -> str:
    storage = root.find("t:TestDefinitions/t:UnitTest", NS)
    if storage is not None and storage.get("storage"):
        return Path(storage.get("storage")).stem
    return trx.stem


def duration(text: str) -> float:
    try:
        hours, minutes, seconds = text.split(":")
        return int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    except ValueError:
        return 0.0


def junit(runs: list[dict]) -> ET.ElementTree:
    suites = ET.Element("testsuites")
    for run in runs:
        suite = ET.SubElement(suites, "testsuite", name=run["assembly"], tests=str(run["total"]),
                              failures=str(run["failed"]), skipped=str(run["skipped"]),
                              time=f"{sum(r['seconds'] for r in run['results']):.3f}")
        for result in run["results"]:
            case = ET.SubElement(suite, "testcase", name=result["name"], classname=result["class"], time=f"{result['seconds']:.3f}")
            if result["outcome"] in FAILED_OUTCOMES:
                ET.SubElement(case, "failure", message=result["message"].splitlines()[0] if result["message"] else result["outcome"]).text = result["message"]
            elif result["outcome"] == "NotExecuted":
                ET.SubElement(case, "skipped")
    return ET.ElementTree(suites)


def totals(runs: list[dict]) -> dict:
    return {key: sum(run[key] for run in runs) for key in ("total", "passed", "failed", "skipped")}


def markdown(runs: list[dict], title: str) -> str:
    total = totals(runs)
    status = "passed" if total["failed"] == 0 and runs else "failed"
    lines = [f"### {title}: {status}", "", "| Assembly | Total | Passed | Failed | Skipped |", "|---|---:|---:|---:|---:|"]
    for run in sorted(runs, key=lambda r: r["assembly"]):
        lines.append(f"| {run['assembly']} | {run['total']} | {run['passed']} | {run['failed']} | {run['skipped']} |")
    lines.append(f"| **All** | {total['total']} | {total['passed']} | {total['failed']} | {total['skipped']} |")
    failures = [(run["assembly"], r) for run in runs for r in run["results"] if r["outcome"] in FAILED_OUTCOMES]
    if failures:
        lines += ["", "Failed:", ""]
        lines += [f"- `{r['name']}` ({assembly}): {r['message'].splitlines()[0] if r['message'] else r['outcome']}" for assembly, r in failures[:50]]
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("results", type=Path, help="folder searched recursively for *.trx")
    parser.add_argument("--junit", type=Path)
    parser.add_argument("--markdown")
    parser.add_argument("--json", type=Path)
    parser.add_argument("--title", default="Tests")
    args = parser.parse_args()

    files = sorted(args.results.rglob("*.trx"))
    runs = [read(f) for f in files]
    if args.junit:
        args.junit.parent.mkdir(parents=True, exist_ok=True)
        junit(runs).write(args.junit, encoding="utf-8", xml_declaration=True)
    if args.markdown:
        text = markdown(runs, args.title) if runs else f"### {args.title}: no test results found\n"
        if args.markdown == "-":
            sys.stdout.write(text)
        else:
            with open(args.markdown, "a", encoding="utf-8") as out:
                out.write(text)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps({**totals(runs), "assemblies": [
            {k: run[k] for k in ("assembly", "total", "passed", "failed", "skipped")} for run in runs]}, indent=2) + "\n", encoding="utf-8")
    if not runs:
        print(f"No TRX files under {args.results}", file=sys.stderr)
        return 1
    return 1 if totals(runs)["failed"] else 0


if __name__ == "__main__":
    sys.exit(main())
