"""Regression checks shared by the Python and C# legacy metadata converters."""
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'samples/babeledit-parity/01-generic-json/project.babel'


class ConverterTests(unittest.TestCase):
    def test_both_converters_preserve_direct_concepts_and_typed_settings(self):
        commands = [
            [sys.executable, str(ROOT / 'tools/babel2toucan.py'), str(SOURCE)],
            ['dotnet', str(ROOT / 'build/Babel2Toucan/Debug/net10.0/Babel2Toucan.dll'), str(SOURCE)],
        ]
        outputs = []
        for command in commands:
            with self.subTest(converter=command[0]):
                result = subprocess.run(command, check=True, capture_output=True, encoding='utf-8')
                project = json.loads(result.stdout)
                def concepts(folder):
                    return len(folder.get('concepts', [])) + sum(concepts(c) for c in folder.get('folders', []))
                count = sum(concepts(folder) for package in project['packages'] for file in package['files'] for folder in file['folders'])
                self.assertEqual(9, count)
                self.assertIs(True, project['configuration']['support_arrays'])
                self.assertIs(True, project['editorConfiguration']['save_empty_translations'])
                self.assertEqual("'%1'", project['editorConfiguration']['copy_templates'][0])
                outputs.append(project)
        self.assertEqual(outputs[0], outputs[1])


if __name__ == '__main__':
    unittest.main()
