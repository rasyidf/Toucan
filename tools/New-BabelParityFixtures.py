"""Create synthetic interoperability projects from our UI-authored BabelEdit seed.

Never uses customer translations. Existing files are not overwritten. Generated
projects are candidates until opened in BabelEdit; see the evidence ledger.
"""
import copy
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1] / 'samples' / 'babeledit-parity'
SEED = ROOT / '01-generic-json' / 'project.babel'
SCENARIOS = [
    ('02-angular-ngx', 'ngx-translate', 'json', 'json'),
    ('03-angular-transloco', 'angular-transloco', 'namespaced-json', 'json'),
    ('04-vue', 'vue-json', 'namespaced-json', 'json'),
    ('05-i18next', 'i18next', 'namespaced-json', 'json'),
    ('06-react', 'react-json', 'json', 'json'),
    ('07-svelte', 'svelte-i18n', 'json', 'json'),
    ('08-ember-json', 'ember-json', 'namespaced-json', 'json'),
    ('09-laravel-json', 'laravel-json', 'plaintext-json', 'json'),
    ('10-generic-yaml', 'generic-yaml', 'yaml', 'yaml'),
    ('11-ember-yaml', 'ember-yaml', 'yaml', 'yaml'),
    ('12-rails-yaml', 'rails-yaml', 'yaml', 'rails'),
    ('13-laravel-php', 'laravel', 'php', 'php'),
    ('14-flutter-arb', 'arb', 'plaintext-json', 'arb'),
    ('15-java-latin1', 'prop-files', 'properties-tree', 'latin1'),
    ('16-java-utf8', 'prop-files', 'properties-tree', 'utf8'),
    ('17-json-arrays', 'generic-json', 'namespaced-json', 'arrays'),
    ('18-yaml-arrays', 'generic-yaml', 'yaml', 'yaml-arrays'),
    ('19-dotnet-resx', 'resx', 'resx', 'resx'),
]


def values(lang):
    return {'title': 'Parity Lab' if lang == 'en-US' else 'Paritätslabor',
            'welcome': 'Hello {name}' if lang == 'en-US' else 'Hallo {name}',
            'empty': '', 'unicode': '日本語 — café 😀',
            'multiline': 'First line\nSecond line', 'quoted': 'Say "hello"',
            'path': 'C:\\translations\\new',
            'items_one': '{{count}} item', 'items_other': '{{count}} items'}


def write(path, content, encoding='utf-8'):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        path.write_text(content, encoding=encoding)


def main():
    seed = ET.parse(SEED)
    ledger = [{'id': '01-generic-json', 'framework': 'generic-json',
               'format': 'json', 'kind': 'json', 'origin': 'UI-created and saved',
               'paths': ['en-US.json', 'de-DE.json']}]
    for ident, framework, fmt, kind in SCENARIOS:
        folder = ROOT / ident
        tree = copy.deepcopy(seed)
        root = tree.getroot()
        root.find('framework').text = framework
        root.find('folder_node/children/package_node/children').clear()
        root.find('configuration/format').text = fmt
        urls = root.find('translation_packages/translation_package/translation_urls')
        urls.clear()
        paths = []
        if kind in ('latin1', 'utf8'):
            ET.SubElement(root.find('configuration'), 'encoding').text = 'ISO-8859-1' if kind == 'latin1' else 'UTF-8'
        for lang in ('en-US', 'de-DE'):
            data = values(lang)
            ext = {'rails': 'yml', 'latin1': 'properties', 'utf8': 'properties', 'arrays': 'json', 'yaml-arrays': 'yaml'}.get(kind, kind)
            rel = f'{lang}.{ext}'
            if kind == 'resx':
                rel = f'Resources.{lang}.resx'
                resource = ET.Element('root')
                for key, value in data.items():
                    entry = ET.SubElement(resource, 'data', {'name': key, 'xml:space': 'preserve'})
                    ET.SubElement(entry, 'value').text = value
                content = ET.tostring(resource, encoding='unicode')
            elif kind == 'php':
                rel = f'{lang}/messages.php'
                content = '<?php\nreturn [\n' + ''.join("    '" + k + "' => '" + v.replace('\\', '\\\\').replace("'", "\\'") + "',\n" for k, v in data.items()) + '];\n'
            elif kind in ('yaml', 'rails', 'yaml-arrays'):
                content = ''.join(k + ': ' + json.dumps(v, ensure_ascii=False) + '\n' for k, v in data.items())
                if kind == 'rails':
                    content = lang + ':\n' + ''.join('  ' + line + '\n' for line in content.splitlines())
                if kind == 'yaml-arrays':
                    content += 'choices:\n  - First\n  - Second\n'
            elif kind in ('latin1', 'utf8'):
                content = '# Synthetic parity fixture\n' + ''.join(k + '=' + json.dumps(v, ensure_ascii=kind == 'latin1')[1:-1] + '\n' for k, v in data.items())
            else:
                if kind == 'arb':
                    data.update({'@@locale': lang, '@welcome': {'description': 'Greeting', 'placeholders': {'name': {'type': 'String'}}}, 'plural': '{count, plural, one{One item} other{{count} items}}'})
                if kind == 'arrays':
                    data.update({'choices': ['First', 'Second'], 'enabled': True, 'limit': 42, 'absent': None})
                content = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
            write(folder / rel, content)
            paths.append(rel)
            url = ET.SubElement(urls, 'translation_url')
            ET.SubElement(url, 'path').text = rel
            ET.SubElement(url, 'language').text = lang
        if not (folder / 'project.babel').exists():
            ET.indent(tree, space='  ')
            tree.write(folder / 'project.babel', encoding='utf-8', xml_declaration=True)
        ledger.append({'id': ident, 'framework': framework, 'format': fmt, 'kind': kind,
                       'origin': 'Generated from UI seed; verification tracked separately', 'paths': paths})
    (ROOT / 'manifest.json').write_text(json.dumps(ledger, indent=2) + '\n', encoding='utf-8')
    print(f'{len(ledger)} synthetic project scenarios available at {ROOT}')


if __name__ == '__main__':
    main()
