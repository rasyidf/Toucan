"""Exercise independently authored exact-resource migrations through the shipped CLI."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "build/resource-compatibility/m2"
CLI = ROOT / "build/Toucan.CLI/Debug/net10.0/toucan.dll"
FORMATS = [
    ("generic-json", "json", '{"title":"日本語 — café 😀"}'),
    ("generic-yaml", "yml", "title: '日本語 — café 😀'\n"),
    ("arb", "arb", '{"@@locale":"wrong","title":"日本語 — café 😀"}'),
    ("prop-files", "properties", "title=日本語 — café 😀\n"),
    ("resx", "resw", '<root><data name="title"><value>日本語 — café 😀</value></data></root>'),
    ("laravel", "php", "<?php return ['title' => '日本語 — café 😀'];"),
]


def run(command, folder, *arguments):
    result = subprocess.run(["dotnet", str(CLI), command, str(folder), *arguments], capture_output=True,
                            encoding="utf-8", env=dict(os.environ, TOUCAN_PLUGINS_DIR=str(OUT / "empty-plugins")))
    assert result.returncode == 0, (command, result.stderr)
    return result.stdout.strip()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    results = []
    with tempfile.TemporaryDirectory(prefix="exact-cli-", dir=OUT) as temporary:
        for framework, extension, content in FORMATS:
            folder = Path(temporary) / framework
            folder.mkdir()
            project = ET.Element("babeledit_project", version="1.3")
            ET.SubElement(project, "framework").text = framework
            packages = ET.SubElement(project, "translation_packages")
            hierarchy = ET.SubElement(ET.SubElement(project, "folder_node"), "children")
            for package in ("main", "checkout"):
                node = ET.SubElement(packages, "translation_package")
                ET.SubElement(node, "name").text = package
                urls = ET.SubElement(node, "translation_urls")
                metadata = ET.SubElement(hierarchy, "package_node")
                ET.SubElement(metadata, "name").text = package
                children = ET.SubElement(metadata, "children")
                if framework == "laravel":
                    metadata_file = ET.SubElement(children, "file_node")
                    ET.SubElement(metadata_file, "name").text = "arbitrary-catalog"
                    children = ET.SubElement(metadata_file, "children")
                concept = ET.SubElement(children, "concept_node")
                for field, value in (("name", "title"), ("comment", "Original comment"), ("description", "Original description")):
                    ET.SubElement(concept, field).text = value
                reviews = ET.SubElement(concept, "translations")
                for language in ("en-US", "id"):
                    relative = f"{package}/{language}/arbitrary-catalog.{extension}"
                    target = folder / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_text(content, encoding="utf-8")
                    url = ET.SubElement(urls, "translation_url")
                    ET.SubElement(url, "path").text = relative
                    ET.SubElement(url, "language").text = language
                    review = ET.SubElement(reviews, "translation")
                    ET.SubElement(review, "language").text = language
                    ET.SubElement(review, "approved").text = str(language == "en-US").lower()
            source = folder / "project.babel"
            ET.ElementTree(project).write(source, encoding="utf-8", xml_declaration=True)
            original = source.read_bytes()
            sibling = folder / ("unrelated." + extension)
            sibling.write_text("Unrelated and deliberately invalid", encoding="utf-8")
            key = "main." + ("arbitrary-catalog." if framework == "laravel" else "") + "title"
            keys = run("list-keys", folder).splitlines()
            assert len(keys) == 2 and key in keys, keys
            assert json.loads(run("get", folder, key)) == {"en-US": "日本語 — café 😀", "id": "日本語 — café 😀"}
            run("set", folder, key, "en-US", "Changed by CLI")
            native = json.loads((folder / "toucan.tproj").read_text(encoding="utf-8-sig"))
            assert len(native["resourceDocuments"]) == 4
            assert len(native["translationStates"]) == 4
            for state in native["translationStates"]:
                assert state["comment"] == "Original comment"
                assert state["approved"] == (state["language"] == "en-US")
                assert state["data"]["babel.description"] == "Original description"
            assert source.read_bytes() == original
            source.rename(source.with_suffix(".original"))
            assert json.loads(run("get", folder, key)) == {"en-US": "Changed by CLI", "id": "日本語 — café 😀"}
            assert run("list-keys", folder).splitlines() == keys
            assert sibling.read_text(encoding="utf-8") == "Unrelated and deliberately invalid"
            results.append({"framework": framework, "extension": extension, "resources": 4,
                            "explicitLocales": ["en-US", "id"], "packages": 2,
                            "valuesAndMetadataPreserved": True, "sourceUnchanged": True,
                            "nativeReopenWithoutSource": True, "unrelatedSiblingUnchanged": True})
            print(framework + ": exact files, locales, packages, metadata and native reopen passed")
    output = OUT / "cli-exact-resources.json"
    output.write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Results: {output}")


if __name__ == "__main__":
    main()
