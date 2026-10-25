"""Render the local audit outputs without any external dependencies."""
from html import escape
import json
import os
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'build/babeledit-parity'
NATIVE = {'01-generic-json', '12-rails-yaml', '14-flutter-arb',
          '16-java-utf8', '18-yaml-arrays', '19-dotnet-resx'}
REPORT = ROOT / 'docs/babeledit-parity-review.md'


def inline_markdown(value):
    value = escape(value)
    def link(match):
        label, target = match.groups()
        if not target.startswith(('https://', 'http://', '#')):
            target = os.path.relpath(REPORT.parent / target, OUT).replace('\\', '/')
        return f'<a href="{escape(target, quote=True)}">{label}</a>'
    value = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', link, value)
    value = re.sub(r'`([^`]+)`', r'<code>\1</code>', value)
    return re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', value)


def render_markdown(source):
    """Render the report's headings, lists, tables and paragraphs with escaped HTML."""
    output = []
    for block in re.split(r'\n\s*\n', source.strip()):
        lines = block.splitlines()
        heading = re.match(r'^(#{1,6}) (.+)$', lines[0])
        if heading:
            level = len(heading[1])
            output.append(f'<h{level}>{inline_markdown(heading[2])}</h{level}>')
        elif lines[0].startswith('|'):
            rows = [line.strip().strip('|').split('|') for line in lines]
            output.append('<div class="scroll"><table><thead><tr>' + ''.join(f'<th>{inline_markdown(c.strip())}</th>' for c in rows[0]) + '</tr></thead><tbody>')
            for row in rows[2:]:
                output.append('<tr>' + ''.join(f'<td>{inline_markdown(c.strip())}</td>' for c in row) + '</tr>')
            output.append('</tbody></table></div>')
        elif re.match(r'^(?:- |\d+\. )', lines[0]):
            tag = 'ul' if lines[0].startswith('- ') else 'ol'
            items = []
            for line in lines:
                item = re.match(r'^(?:- |\d+\. )(.*)', line)
                if item:
                    items.append(item[1])
                elif items:
                    items[-1] += ' ' + line.strip()
            output.append(f'<{tag}>' + ''.join(f'<li>{inline_markdown(item)}</li>' for item in items) + f'</{tag}>')
        else:
            output.append('<p>' + inline_markdown(' '.join(line.strip() for line in lines)) + '</p>')
    return '\n'.join(output)


def main():
    cases = json.loads((ROOT / 'samples/babeledit-parity/manifest.json').read_text())
    results = {r['id']: r for r in json.loads((OUT / 'toucan-read-results.json').read_text(encoding='utf-8'))}
    report = REPORT.read_text(encoding='utf-8')
    latest = report.split('## Fix verification\n', 1)[1].split('## Baseline and evidence', 1)[0]
    lost_count = sum(bool(r.get('keys_lost_on_save')) for r in results.values())
    def passed_tests(name):
        file = OUT / 'tests' / name
        if not file.exists():
            return '?'
        node = ET.parse(file).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
        return node.get('passed', '?') if node is not None else '?'
    rows = []
    for case in cases:
        r = results[case['id']]
        read = r['key_count_matches'] and r['unicode_exact_two_languages'] and r['greeting_exact_two_languages']
        lost = ', '.join(r.get('keys_lost_on_save', []))
        save_ok = r.get('roundtrip_set', {}).get('exit') == 0 and not lost and not r.get('keys_added_on_save') and r.get('unicode_unchanged_on_save', False)
        saved = 'Lost: ' + lost if lost else 'Skipped: read differs' if 'roundtrip_skipped' in r else 'Save/reload checks pass' if save_ok else 'Save/reload differs'
        rows.append(f'<tr><td><a href="../../samples/babeledit-parity/{escape(case["id"])}/project.babel">{escape(case["id"])}</a></td>'
                    f'<td>{escape(case["framework"])}</td><td>{"Opened / saved" if case["id"] in NATIVE else "Generated; not UI verified"}</td>'
                    f'<td class="{"pass" if read else "fail"}">{"Narrow checks pass" if read else "Read differs"}</td><td class="{"pass" if save_ok else "fail"}">{escape(saved)}</td></tr>')
    figures = []
    preferred = ['01-framework-selector', '03-language-picker', '05-project-configuration',
                 '07-flutter-arb-saved', '14-rails-unicode', '16-yaml-arrays-settled',
                 '18-resx-saved', '20-java-utf8-saved', '21-rails-saved', '22-yaml-saved', '24-export-metadata']
    for prefix in preferred:
        path = next((OUT / 'evidence' / (prefix + '-0.' + ext) for ext in ('png', 'jpg', 'jpeg', 'webp')
                     if (OUT / 'evidence' / (prefix + '-0.' + ext)).exists()), None)
        if path is None:
            continue
        figures.append(f'<figure><a href="evidence/{path.name}"><img loading="lazy" src="evidence/{path.name}" alt="{escape(prefix)}"></a><figcaption>{escape(prefix)}</figcaption></figure>')
    html = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>BabelEdit / Toucan parity review</title><style>
body{font:16px/1.6 system-ui,sans-serif;background:#12181c;color:#e2e8ed;max-width:1400px;margin:auto;padding:32px}
h1{font-size:36px;line-height:1.2}h2{margin-top:40px}a{color:#42d5e7}p,li{max-width:1000px}.notice{padding:20px;background:#1b3029;border-left:4px solid #9fddbb;border-radius:6px}li{margin:8px 0}code{background:#24323a;padding:2px 5px;border-radius:4px;overflow-wrap:anywhere}nav{display:flex;gap:20px;flex-wrap:wrap;margin:20px 0}.report-summary{background:#1b242b;padding:8px 24px 20px;border-radius:12px}
.stats{display:flex;gap:16px;flex-wrap:wrap}.stats div{padding:16px 24px;background:#1d272e;border-radius:12px}.stats strong{font-size:28px;display:block}
table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:12px;text-align:left;border-bottom:1px solid #38434b}th{background:#23313a;position:sticky;top:0}tr:hover{background:#1b272d}.pass{color:#9fddbb}.fail{color:#f3ad9f}.scroll{overflow:auto}
.gallery{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,440px),1fr));gap:18px}figure{margin:0;background:#202a31;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto}figcaption{padding:10px 16px;color:#b9c9d2}
</style><h1>BabelEdit ↔ Toucan</h1><p>Windows comparison · 21 October 2026 · Installed BabelEdit 5.5.1</p>
<p class="notice"><strong>The six audited failures have fixes and regression coverage.</strong> Empty keys, Java encodings, Rails locale roots, YAML sequences, BabelEdit concepts and Excel metadata now have targeted checks. The current fixture results appear below. Broader 100% workflow parity is still not established.</p>
<div class="stats"><div><strong>19</strong>Synthetic projects</div><div><strong>@CORE@ + @GUI@</strong>Core and GUI tests passed</div><div><strong>@LOST@</strong>CLI copies lose keys</div><div><strong>6</strong>Native BabelEdit project checks</div></div>
<nav><a href="#latest-report">Latest fix report</a><a href="report.html">Full comparison and acceptance criteria</a><a href="#project-coverage">Project results</a><a href="#screenshots">Reference screenshots</a></nav>
<p><a href="toucan-read-results.json">Raw Toucan results</a> · <a href="../../samples/babeledit-parity/README.md">Fixture instructions</a> · <a href="babeledit-export.xlsx">BabelEdit XLSX sample</a> · <a href="../../docs/babeledit-parity-review.md">Report source</a></p>
<p>One project was authored in the BabelEdit UI; eighteen were generated from that seed. Six were opened and saved in BabelEdit. The current CLI uses .babel configuration when present; saves go into isolated copies and create a native .tproj while preserving the original .babel file. Checks cover key counts, Unicode, a placeholder greeting and save/reload. These passes do not certify every framework or workflow.</p>
<section class="report-summary" id="latest-report"><h2>Latest fix report</h2>@LATEST@</section>
<h2 id="project-coverage">Project coverage</h2><div class="scroll"><table><thead><tr><th>Project fixture</th><th>Framework</th><th>BabelEdit evidence</th><th>Toucan read checks</th><th>Toucan CLI save/reload</th></tr></thead><tbody>'''
    html += ''.join(rows) + '</tbody></table></div><h2 id="screenshots">BabelEdit reference screenshots</h2><p>Captured during the original BabelEdit 5.5.1 audit. Current Toucan verification is reported above. Click an image to view it at full size.</p><div class="gallery">' + ''.join(figures) + '</div></html>'
    html = html.replace('@CORE@', passed_tests('core-parity-final.trx')).replace('@GUI@', passed_tests('gui-parity-full.trx')).replace('@LOST@', str(lost_count))
    html = html.replace('@LATEST@', render_markdown(latest))
    (OUT / 'index.html').write_text(html, encoding='utf-8')
    style = re.search(r'<style>(.*?)</style>', html, flags=re.S)[0]
    report_html = '<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Toucan — updated BabelEdit compatibility report</title>' + style
    report_html += '<nav><a href="index.html">← Results gallery</a><a href="../../docs/babeledit-parity-review.md">Markdown source</a></nav>'
    report_html += render_markdown(report) + '</html>'
    (OUT / 'report.html').write_text(report_html, encoding='utf-8')
    print(OUT / 'index.html')


if __name__ == '__main__':
    main()
