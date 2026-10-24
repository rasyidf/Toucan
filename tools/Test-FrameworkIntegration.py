"""Original CLI capability, scanner-cycle and runtime acceptance fixtures. No vendor assets."""
import json
import os
import platform
import subprocess
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "build/resource-compatibility/m5-m6"
CLI = ROOT / "build/Toucan.CLI/Debug/net10.0/toucan.dll"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="fixtures-", dir=OUT) as temporary:
        root = Path(temporary)
        env = os.environ | {"TOUCAN_PLUGINS_DIR": str(root / "no-plugins"), "TOUCAN_PLUGIN_POLICY": str(root / "policy.json")}
        def run(*args, code=0):
            result = subprocess.run(["dotnet", str(CLI), *map(str, args)], cwd=ROOT, env=env, capture_output=True, text=True, encoding="utf-8", timeout=60)
            assert result.returncode == code, (args, result.returncode, result.stdout[-2000:], result.stderr[-2000:])
            return result.stdout
        catalog = root / "catalog"
        (catalog / "source").mkdir(parents=True)
        (catalog / "target").mkdir()
        source = catalog / "source/generated.json"
        source.write_text(json.dumps({"title": {"defaultMessage": "Hello {name}", "description": {"text": "Greeting", "custom": 42}}}), encoding="utf-8")
        (catalog / "target/de.json").write_text(json.dumps({"title": "Hallo {name}"}), encoding="utf-8")
        migration = catalog / "project.babel"
        migration.write_text('''<babeledit_project><framework>react-intl</framework><primary_language>en</primary_language>
<languages><language><code>en</code></language><language><code>de</code></language></languages>
<translation_packages><translation_package><name>main</name><translation_urls>
<translation_url><path>source/generated.json</path><language>en</language></translation_url>
<translation_url><path>target/de.json</path><language>de</language></translation_url>
</translation_urls></translation_package></translation_packages><configuration><unknown_option>keep</unknown_option></configuration></babeledit_project>''', encoding="utf-8")
        original, original_migration = source.read_bytes(), migration.read_bytes()
        assert run("list-keys", catalog).strip() == "title"
        run("set", catalog, "title", "en", "Forbidden", code=1)
        run("set", catalog, "new", "de", "Forbidden", code=1)
        assert source.read_bytes() == original
        run("set", catalog, "title", "de", "Guten Tag {name}")
        native = json.loads((catalog / "toucan.tproj").read_text(encoding="utf-8-sig"))
        assert native["framework"] == "formatjs"
        assert any(d["resource"].get("ownership") == 1 for d in native["resourceDocuments"])
        migration.rename(migration.with_suffix(".original"))
        assert json.loads(run("get", catalog, "title")) == {"en": "Hello {name}", "de": "Guten Tag {name}"}
        run("set", catalog, "title", "en", "Still forbidden", code=1)
        assert source.read_bytes() == original and migration.with_suffix(".original").read_bytes() == original_migration
        print("M5 CLI: generated source/additions refused, editable target saved, neutral ownership survives native reopen")

        project = root / "scan"
        (project / "src").mkdir(parents=True)
        (project / "node_modules").mkdir()
        count_files, per_file = 200, 50
        keys = {f"key{n}": f"Message {n}" for n in range(count_files * per_file)}
        (project / "en.json").write_text(json.dumps(keys), encoding="utf-8")
        (project / "toucan.tproj").write_text(json.dumps({"name": "Original source benchmark", "framework": "i18next", "primaryLanguage": "en", "languages": ["en"], "saveFormat": "json", "sourceRoots": ["."], "scanExtensions": [".ts"]}), encoding="utf-8")
        for file in range(count_files):
            text = "// t('comment-is-not-a-key')\n" + "\n".join(f"t(\n 'key{file * per_file + n}', {{count: 2}}\n);" for n in range(per_file))
            (project / "src" / f"file{file}.ts").write_text(text, encoding="utf-8")
        (project / "node_modules/ignored.ts").write_text("t('ignored')", encoding="utf-8")
        start = time.perf_counter()
        scan = json.loads(run("scan", project))
        wall_ms = (time.perf_counter() - start) * 1000
        assert scan["CanClassifyUnused"] and scan["Complete"] and not scan["Diagnostics"]
        assert scan["FilesScanned"] == count_files and len(scan["References"]) == count_files * per_file
        assert scan["References"][0]["Line"] == 3 and scan["References"][0]["Column"] == 2
        dynamic = project / "src/dynamic.ts"
        dynamic.write_text("t(`dynamic.${value}`)", encoding="utf-8")
        incomplete = json.loads(run("scan", project, code=2))
        assert not incomplete["CanClassifyUnused"] and any(d["Code"] == "source.unresolved" for d in incomplete["Diagnostics"])
        dynamic.unlink()
        junction = project / "src/cycle"
        if os.name == "nt":
            subprocess.run(["cmd", "/c", "mklink", "/J", str(junction), str(project)], check=True, capture_output=True)
        else:
            junction.symlink_to(project, target_is_directory=True)
        try:
            cycle = json.loads(run("scan", project, code=2))
            assert not cycle["Complete"] and any(d["Code"] == "source.link-skipped" for d in cycle["Diagnostics"])
        finally:
            # Remove the link itself, never recurse through its destination.
            if os.name == "nt": junction.rmdir()
            else: junction.unlink()
        payload = {"platform": platform.platform(), "configuration": "Debug", "files": count_files, "references": count_files * per_file,
                   "sourceBytes": sum(p.stat().st_size for p in (project / "src").glob("*.ts")), "scannerDuration": scan["Duration"], "processWallMs": round(wall_ms, 2),
                   "generatedSourceProtected": True, "targetSaveNativeReopen": True, "originalMigrationUnchanged": True,
                   "commentsExcluded": True, "projectExcludes": True, "locations": True, "dynamicIncomplete": True, "cycleSkipped": True,
                   "budget": "Measurement only; no cross-platform performance budget declared"}
        (OUT / "cli-framework-integration.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        print(f"M6 CLI: {count_files} files / {count_files * per_file} usages; scanner {scan['Duration']}; process {wall_ms:.0f} ms; dynamic/cycle diagnostics passed")


if __name__ == "__main__":
    main()
