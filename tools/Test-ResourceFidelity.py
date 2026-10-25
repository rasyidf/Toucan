"""Check typed native resource settings through the shipped CLI, using original inputs."""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "build/resource-compatibility/m3"
CLI = ROOT / "build/Toucan.CLI/Debug/net10.0/toucan.dll"
CASES = [
    ("json", "json", '{"title":"Original","empty":"","literal.key":"Keep","opaque":null,"numeric":{"0":"zero","2":"two"},"array":[null,"value",{}]}', "title"),
    ("yaml", "yml", 'en:\n  title: Original\nempty: ""\nopaque: null\n', "en.title"),
    ("arb", "arb", '{"@@locale":"en","@@custom":{"keep":true},"title":"Original","empty":"","@title":{"description":"Keep description","placeholders":{"name":{"type":"String","custom":42}},"unknown":true}}', "title"),
    ("java-properties", "properties", '# Keep header\ntitle=Original\nempty=\n', "title"),
]


def run(command, folder, *arguments):
    result = subprocess.run(["dotnet", str(CLI), command, str(folder), *arguments], capture_output=True,
                            encoding="utf-8", env=dict(os.environ, TOUCAN_PLUGINS_DIR=str(OUT / "empty-plugins")))
    assert result.returncode == 0, (command, result.stderr)
    return result.stdout.strip()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    results = []
    with tempfile.TemporaryDirectory(prefix="fidelity-cli-", dir=OUT) as temporary:
        for format_id, extension, source, key in CASES:
            folder = Path(temporary) / format_id
            folder.mkdir()
            resource = folder / ("custom." + extension)
            resource.write_text(source, encoding="utf-8")
            native = {"saveFormat": format_id, "languages": ["en"], "primaryLanguage": "en",
                      "resourceSchemaVersion": 1,
                      "resourceOptions": {"layout": 0, "indent": 2, "order": 1, "newline": 1,
                                          "inputEncoding": "UTF-8", "outputEncoding": "UTF-8 BOM", "saveEmptyTranslations": False},
                      "resourceDocuments": [{"resource": {"id": "catalog", "relativePath": resource.name,
                                                            "formatId": format_id, "package": "main", "language": "en"}}]}
            (folder / "toucan.tproj").write_text(json.dumps(native), encoding="utf-8")
            run("set", folder, key, "en", "日本語 — café 😀")
            run("set", folder, "new.literal", "en", "New literal")
            assert json.loads(run("get", folder, key)) == {"en": "日本語 — café 😀"}
            assert json.loads(run("get", folder, "new.literal")) == {"en": "New literal"}
            assert "empty" not in run("list-keys", folder).splitlines()
            data = resource.read_bytes()
            assert data.startswith(b"\xef\xbb\xbf")
            text = data.decode("utf-8-sig")
            assert "\r\n" in text and "\n" not in text.replace("\r\n", "")
            if format_id == "json":
                parsed = json.loads(text)
                assert parsed["opaque"] is None and isinstance(parsed["numeric"], dict)
                assert parsed["array"] == [None, "value", {}]
                assert parsed["literal.key"] == "Keep" and parsed["new.literal"] == "New literal"
                assert list(parsed) == sorted(parsed)
            elif format_id == "yaml":
                assert '"en":' in text and '"title":' in text and '"opaque": null' in text
            elif format_id == "arb":
                parsed = json.loads(text)
                assert parsed["@title"]["description"] == "Keep description"
                assert parsed["@title"]["placeholders"]["name"]["custom"] == 42
                assert parsed["@title"]["unknown"] and parsed["@@custom"]["keep"]
            else:
                assert "# Keep header" in text
            results.append({"format": format_id, "typedSettings": True, "nativeReopen": True,
                            "literalNewKey": True, "emptyPolicy": True, "supportedOpaqueData": True})
            print(format_id + ": CLI options, encoding/newlines, new key, empty policy and retained data passed")
    output = OUT / "cli-fidelity.json"
    output.write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(f"Results: {output}")


if __name__ == "__main__":
    main()
