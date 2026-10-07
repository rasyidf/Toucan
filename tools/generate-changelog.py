#!/usr/bin/env python3
"""Refresh docs/changelog.html from CHANGELOG.md: python3 tools/generate-changelog.py.

Uses the changelog's Markdown subset: paragraphs, ### headings, bullets,
inline code, bold text, and links. No runtime fetch or third-party dependency.
"""
from datetime import date
from html import escape
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from site_links import REPO, attr, resolve  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]


def inline(text):
    pattern = r'`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)|\*\*(.+?)\*\*'
    parts, offset = [], 0
    for match in re.finditer(pattern, text):
        parts.append(escape(text[offset:match.start()]))
        code, label, target, bold = match.groups()
        if code is not None:
            parts.append(f'<code>{escape(code)}</code>')
        elif label is not None:
            parts.append(f'<a href="{attr(resolve(target))}">{inline(label)}</a>')
        else:
            parts.append(f'<strong>{inline(bold)}</strong>')
        offset = match.end()
    parts.append(escape(text[offset:]))
    return ''.join(parts)


def blocks(text):
    output, paragraph, bullets = [], [], []

    def flush():
        if paragraph:
            output.append('<p>' + inline(' '.join(paragraph)) + '</p>')
            paragraph.clear()
        if bullets:
            output.append('<ul>\n' + '\n'.join(f'<li>{inline(item)}</li>' for item in bullets) + '\n</ul>')
            bullets.clear()

    for line in text.splitlines():
        if not line.strip():
            flush()
        elif line.startswith('### '):
            flush()
            output.append(f'<h3>{inline(line[4:])}</h3>')
        elif line.startswith('- '):
            if paragraph:
                flush()
            bullets.append(line[2:])
        elif line.startswith(('```', '#', '> ', '| ', '    ')):
            raise ValueError(f'Unsupported changelog block; extend renderer first: {line}')
        else:
            if bullets:
                bullets[-1] += ' ' + line.strip()
            else:
                paragraph.append(line.strip())
    flush()
    return '\n'.join(output)


def main():
    source = (ROOT / 'CHANGELOG.md').read_text()
    sections = re.split(r'^## \[([^\]]+)\](?: - (\d{4}-\d{2}-\d{2}))?\s*$', source, flags=re.M)
    articles, links = [], []
    latest = next(sections[i] for i in range(1, len(sections), 3) if sections[i] != 'Unreleased')
    for i in range(1, len(sections), 3):
        version, released, body = sections[i:i + 3]
        pending = version == 'Unreleased'
        if pending and not body.strip():
            continue
        anchor = 'unreleased' if pending else f'v{version}'
        label = 'Unreleased' if pending else f'v{version}'
        links.append(f'<a href="#{anchor}">{label}</a>')
        intro, _, notes = body.partition('### ')
        if notes:
            notes = '### ' + notes
        if pending and not intro.strip():
            intro = 'Changes waiting for the next release.'
        counts = []
        for category, content in re.findall(r'^### ([^\n]+)\n(.*?)(?=^### |\Z)', notes, re.M | re.S):
            count = len(re.findall(r'^- ', content, re.M))
            kind = category.lower().split()[0]
            counts.append(f'<span class="{escape(kind)}">{count} {escape(category.lower())}</span>')
        badge = '<span class="release-badge pending">Not released</span>' if pending else '<span class="release-badge">Latest preview</span>' if version == latest else ''
        timestamp = '' if pending else f'<time datetime="{released}">{date.fromisoformat(released).strftime("%b %d, %Y")}</time>'
        release_link = '' if pending else f'<a href="{REPO}/releases/tag/v{version}">Release on GitHub ↗</a>'
        articles.append(f'''<article class="release" id="{anchor}" aria-labelledby="heading-{anchor}">
<div class="release-heading"><h2 id="heading-{anchor}"><a href="#{anchor}">{label}</a></h2>{badge}{timestamp}</div>
<div class="release-lead">{blocks(intro)}</div>
<div class="release-counts">{''.join(counts)}</div>
<details><summary>Read full release notes<span class="sr-only"> for {label}</span></summary><div class="release-notes">{blocks(notes)}</div></details>
<div class="release-links"><a href="#{anchor}" aria-label="Permalink to {label}">Permalink</a>{release_link}</div>
</article>''')
    page = ROOT / 'docs/changelog.html'
    html = page.read_text()
    for marker, content in [('RELEASE INDEX', '\n'.join(links)), ('RELEASES', '\n'.join(articles))]:
        start, end = f'<!-- {marker} START -->', f'<!-- {marker} END -->'
        before, remainder = html.split(start, 1)
        _, after = remainder.split(end, 1)
        html = before + start + '\n' + content + '\n' + end + after
    page.write_text(html)
    print(f'Generated {len(articles)} changelog entries.')


if __name__ == '__main__':
    main()
