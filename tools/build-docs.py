#!/usr/bin/env python3
"""Build the website's documentation pages from the Markdown in docs/: python3 tools/build-docs.py.

Writes docs/docs.html (the index) and one page per entry in site_links.PAGES, all using
assets/base.css and assets/doc.css. Links between published docs stay on the site; links to
source files go to GitHub. Markdown subset: headings, paragraphs, nested lists and task lists,
fenced code (Mermaid runs in the browser), tables, block quotes, rules, inline code, bold,
italic and links. No third-party dependency.
"""
from html import escape
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from site_links import PAGES, attr, resolve  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
SITE = 'https://toucan.rasyid.dev'

# docs.html sections: (heading, blurb, [repo paths]).
GROUPS = [
    ('Using Toucan', 'Settings and features you meet in the app.', [
        'docs/ai-integration.md', 'docs/formats.md', 'docs/provider-settings.md', 'docs/pretranslation-preview.md']),
    ('Extending Toucan', 'How it is built, and how to add formats, providers and rules.', [
        'docs/plugins.md', 'docs/ARCHITECTURE.md', 'Toucan.Core/ARCHITECTURE.md']),
    ('Project status', 'What works, what is broken and what comes next.', [
        'docs/todos/future-roadmap.md', 'docs/known-bugs.md', 'docs/completed-features.md']),
    ('Design', 'Brand rules and how the UI is reviewed.', ['docs/branding.md', 'docs/visual-review.md']),
]

THEME_SVG = (
    '<svg class="theme-icon-dark" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>'
    '<svg class="theme-icon-light" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>')
GITHUB_SVG = '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 .5C5.65.5.5 5.65.5 12a11.5 11.5 0 0 0 7.86 10.92c.58.1.79-.25.79-.56v-2.02c-3.2.7-3.87-1.36-3.87-1.36-.52-1.33-1.28-1.68-1.28-1.68-1.04-.71.08-.7.08-.7 1.15.08 1.75 1.18 1.75 1.18 1.02 1.75 2.68 1.24 3.33.95.1-.74.4-1.24.72-1.53-2.56-.29-5.25-1.28-5.25-5.7 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.05 0 0 .97-.31 3.17 1.18a11 11 0 0 1 5.78 0c2.2-1.49 3.17-1.18 3.17-1.18.62 1.59.23 2.76.11 3.05.74.81 1.18 1.84 1.18 3.1 0 4.43-2.7 5.4-5.27 5.69.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56A11.5 11.5 0 0 0 23.5 12C23.5 5.65 18.35.5 12 .5z"/></svg>'


# ---------- Markdown ----------

def slug(text):
    text = re.sub(r'<[^>]+>|`', '', text).lower()
    return re.sub(r'\s', '-', re.sub(r'[^\w\s-]', '', text)).strip('-')


class Doc:
    def __init__(self, source):
        self.source = source
        self.ids = {}
        self.toc = []  # (level, id, text)

    def unique(self, base):
        n = self.ids.get(base, 0)
        self.ids[base] = n + 1
        return base if n == 0 else f'{base}-{n}'

    def inline(self, text):
        out, pos = [], 0
        pattern = re.compile(r'`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\)|<(https?://[^>\s]+)>|\*\*(.+?)\*\*|(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])|<br\s*/?>')
        for m in pattern.finditer(text):
            out.append(escape(text[pos:m.start()], quote=False))
            code, label, target, auto, bold, ital = m.groups()
            if code is not None:
                out.append(f'<code>{escape(code, quote=False)}</code>')
            elif label is not None:
                out.append(f'<a href="{attr(resolve(target, self.source))}">{self.inline(label)}</a>')
            elif auto is not None:
                out.append(f'<a href="{attr(auto)}">{escape(auto)}</a>')
            elif bold is not None:
                out.append(f'<strong>{self.inline(bold)}</strong>')
            elif ital is not None:
                out.append(f'<em>{self.inline(ital)}</em>')
            else:
                out.append('<br>')
            pos = m.end()
        out.append(escape(text[pos:], quote=False))
        return ''.join(out)

    def blocks(self, lines, top=False):
        out, i = [], 0
        while i < len(lines):
            line = lines[i]
            if not line.strip():
                i += 1
                continue
            if m := re.match(r'^(\s*)(`{3,})\s*([\w+-]*)\s*$', line):
                indent, fence, lang = len(m.group(1)), m.group(2), m.group(3)
                body, i = [], i + 1
                while i < len(lines) and not re.match(r'^\s*' + fence + r'\s*$', lines[i]):
                    body.append(lines[i][indent:] if lines[i][:indent].strip() == '' else lines[i])
                    i += 1
                i += 1
                code = escape('\n'.join(body), quote=False)
                if lang == 'mermaid':
                    out.append(f'<pre class="mermaid">{code}</pre>')
                else:
                    cls = f' class="language-{lang}"' if lang else ''
                    out.append(f'<pre><code{cls}>{code}</code></pre>')
            elif m := re.match(r'^(#{1,6})\s+(.*?)\s*#*\s*$', line):
                level, text = len(m.group(1)), m.group(2)
                hid = self.unique(slug(text))
                if top and level in (2, 3):
                    self.toc.append((level, hid, re.sub(r'[`*]', '', text)))
                out.append(f'<h{level} id="{hid}"><a class="anchor" href="#{hid}" aria-label="Link to this section">#</a>{self.inline(text)}</h{level}>')
                i += 1
            elif re.match(r'^<a id="[^"]+"></a>\s*$', line):
                out.append(line.strip())
                i += 1
            elif re.match(r'^(-{3,}|\*{3,})\s*$', line):
                out.append('<hr>')
                i += 1
            elif line.startswith('|') and i + 1 < len(lines) and re.match(r'^\|[\s:|-]+\|\s*$', lines[i + 1]):
                head = self.cells(line)
                align = ['center' if c.strip().startswith(':') and c.strip().endswith(':') else 'right' if c.strip().endswith(':') else '' for c in self.cells(lines[i + 1])]
                rows, i = [], i + 2
                while i < len(lines) and lines[i].startswith('|'):
                    rows.append(self.cells(lines[i]))
                    i += 1
                th = ''.join(f'<th{self.al(align, n)}>{self.inline(c)}</th>' for n, c in enumerate(head))
                tr = ''.join('<tr>' + ''.join(f'<td{self.al(align, n)}>{self.inline(c)}</td>' for n, c in enumerate(r)) + '</tr>' for r in rows)
                out.append(f'<div class="table-wrap"><table><thead><tr>{th}</tr></thead><tbody>{tr}</tbody></table></div>')
            elif line.startswith('>'):
                quote = []
                while i < len(lines) and lines[i].startswith('>'):
                    quote.append(re.sub(r'^>\s?', '', lines[i]))
                    i += 1
                out.append('<blockquote>' + self.blocks(quote) + '</blockquote>')
            elif re.match(r'^\s*([-*]|\d+\.)\s+', line):
                html, i = self.list(lines, i)
                out.append(html)
            else:
                para = []
                while i < len(lines) and lines[i].strip() and not self.starts_block(lines[i]):
                    para.append(lines[i].strip())
                    i += 1
                out.append('<p>' + self.inline(' '.join(para)) + '</p>')
        return '\n'.join(out)

    @staticmethod
    def starts_block(line):
        return bool(re.match(r'^(\s*(`{3,}|[-*]\s|\d+\.\s)|#{1,6}\s|>|\||<a id=)', line))

    @staticmethod
    def cells(line):
        inner = re.sub(r'^\||\|\s*$', '', line).replace('\\|', '\x00')
        return [c.strip().replace('\x00', '|') for c in inner.split('|')]

    @staticmethod
    def al(align, n):
        return f' style="text-align:{align[n]}"' if n < len(align) and align[n] else ''

    def list(self, lines, i):
        first = re.match(r'^(\s*)([-*]|\d+\.)\s+', lines[i])
        base, ordered = len(first.group(1)), first.group(2)[0].isdigit()
        items = []
        while i < len(lines):
            m = re.match(r'^(\s*)([-*]|\d+\.)\s+(.*)$', lines[i])
            if not m or len(m.group(1)) != base or m.group(2)[0].isdigit() != ordered:
                break
            content_indent = len(m.group(1)) + len(m.group(2)) + 1
            body, i = [m.group(3)], i + 1
            while i < len(lines):
                nxt = lines[i]
                if not nxt.strip():
                    j = i
                    while j < len(lines) and not lines[j].strip():
                        j += 1
                    if j < len(lines) and len(lines[j]) - len(lines[j].lstrip()) >= content_indent:
                        body.extend([''] * (j - i))
                        i = j
                        continue
                    break
                indent = len(nxt) - len(nxt.lstrip())
                if indent >= content_indent:
                    body.append(nxt[content_indent:])
                elif not self.starts_block(nxt):
                    body.append(nxt.strip())  # lazy continuation of the paragraph
                else:
                    break
                i += 1
            items.append(self.item(body))
        tag = 'ol' if ordered else 'ul'
        cls = ' class="tasks"' if any('class="task"' in it for it in items) else ''
        return f'<{tag}{cls}>\n' + '\n'.join(items) + f'\n</{tag}>', i

    def item(self, body):
        first = body[0]
        task = re.match(r'^\[( |x|X)\]\s+(.*)$', first)
        cls = ''
        if task:
            box = '☑' if task.group(1) != ' ' else '☐'
            body[0] = task.group(2)
            cls = ' class="task"'
            prefix = f'<span class="box" aria-hidden="true">{box}</span><span class="sr-only">{"Done:" if box == "☑" else "To do:"} </span>'
        else:
            prefix = ''
        inner = self.blocks(body)
        if inner.startswith('<p>') and inner.count('<p>') == 1 and inner.endswith('</p>'):
            inner = inner[3:-4]
        elif inner.startswith('<p>'):
            end = inner.index('</p>')
            inner = inner[3:end] + inner[end + 4:]
        return f'<li{cls}>{prefix}{inner}</li>'


def front_matter(text):
    meta = {}
    if text.startswith('---\n'):
        end = text.index('\n---', 4)
        for line in text[4:end].splitlines():
            if m := re.match(r'^(\w+):\s*(.*)$', line):
                meta[m.group(1)] = m.group(2).strip().strip('"')
        text = text[end + 4:].lstrip('\n')
    return meta, text


# ---------- Page template ----------

def head(title, description, page, extra_head=''):
    url = f'{SITE}/{page}'
    return f'''<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{escape(title)}</title>
<meta name="description" content="{attr(description)}">
<link rel="canonical" href="{url}">
<link rel="icon" href="assets/favicon.ico" sizes="any">
<link rel="icon" type="image/png" sizes="192x192" href="assets/icons/icon-192.png">
<link rel="apple-touch-icon" href="assets/icons/apple-touch-icon.png">
<link rel="manifest" href="site.webmanifest">
<meta name="theme-color" content="#171B22" media="(prefers-color-scheme: dark)">
<meta name="theme-color" content="#F6F7F9" media="(prefers-color-scheme: light)">
<meta property="og:type" content="website">
<meta property="og:title" content="{attr(title)}">
<meta property="og:description" content="{attr(description)}">
<meta property="og:url" content="{url}">
<meta property="og:image" content="{SITE}/assets/icons/icon-512.png">
<script>
(function () {{
  try {{
    var saved = localStorage.getItem('toucan-theme');
    if (saved === 'light' || saved === 'dark') document.documentElement.setAttribute('data-theme', saved);
  }} catch (e) {{}}
}})();
</script>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:opsz,wght@12..96,500;12..96,700;12..96,800&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet">
<link rel="stylesheet" href="assets/base.css">
<link rel="stylesheet" href="assets/doc.css">
<script src="assets/site.js" defer></script>{extra_head}
</head>
<body>
<a class="sr-only skip" href="#main">Skip to content</a>
<header class="site-header" id="site-header">
  <div class="spectrum" aria-hidden="true"><span></span><span></span><span></span><span></span><span></span></div>
  <div class="wrap nav">
    <a class="brand" href="index.html" aria-label="Toucan home">
      <img src="assets/logo.png" alt="" width="30" height="30">
      Toucan
    </a>
    <nav class="nav-links" aria-label="Main navigation">
      <a href="index.html">Overview</a>
      <a href="docs.html"{' aria-current="page"' if page == 'docs.html' else ''}>Docs</a>
      <a href="plugins.html"{' aria-current="page"' if page == 'plugins.html' else ''}>Plugins</a>
      <a href="roadmap.html"{' aria-current="page"' if page == 'roadmap.html' else ''}>Roadmap</a>
      <a href="changelog.html">Changelog</a>
    </nav>
    <div class="nav-actions">
      <button class="icon-btn" id="theme-toggle" type="button" aria-label="Toggle color theme">{THEME_SVG}</button>
      <a class="icon-btn" href="https://github.com/rasyidf/Toucan" aria-label="Toucan on GitHub">{GITHUB_SVG}</a>
      <a class="btn btn-primary btn-sm" href="https://github.com/rasyidf/Toucan/releases">Download</a>
    </div>
  </div>
</header>
'''


FOOT = '''
<footer>
  <div class="wrap foot">
    <a class="brand" href="index.html"><img src="assets/logo.png" alt="" width="24" height="24">Toucan</a>
    <nav aria-label="Footer">
      <a href="https://github.com/rasyidf/Toucan">Source</a>
      <a href="https://github.com/rasyidf/Toucan/releases">Releases</a>
      <a href="https://github.com/rasyidf/Toucan/issues">Issues</a>
      <a href="docs.html">Docs</a>
      <a href="plugins.html">Plugins</a>
      <a href="changelog.html">Changelog</a>
    </nav>
    <span class="copy">© 2023–2026 <a href="https://rasyid.dev">rasyid.dev</a> · MIT License</span>
  </div>
</footer>
'''

MERMAID = '''
<script type="module">
import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';
var dark = document.documentElement.getAttribute('data-theme') === 'dark' ||
  (!document.documentElement.getAttribute('data-theme') && matchMedia('(prefers-color-scheme: dark)').matches);
mermaid.initialize({ startOnLoad: true, theme: dark ? 'dark' : 'neutral', securityLevel: 'strict' });
</script>
'''


def guide_list(current):
    items = []
    for _, _, paths in GROUPS:
        for p in paths:
            title = meta_of(p)['title']
            cur = ' aria-current="page"' if PAGES[p] == current else ''
            items.append(f'<a href="{PAGES[p]}"{cur}>{escape(title)}</a>')
    return '\n'.join(items)


_meta = {}


def meta_of(path):
    if path not in _meta:
        meta, _ = front_matter((ROOT / path).read_text())
        meta.setdefault('title', Path(path).stem)
        _meta[path] = meta
    return _meta[path]


def build_page(path):
    page = PAGES[path]
    meta, text = front_matter((ROOT / path).read_text())
    doc = Doc(path)
    lines = text.splitlines()
    title = meta.get('title', Path(path).stem)
    # The first H1 repeats the title; the page header shows it once.
    if lines and lines[0].startswith('# '):
        lines = lines[1:]
    body = doc.blocks(lines, top=True)
    toc = ''.join(f'<a class="l{lvl}" href="#{hid}">{escape(t)}</a>' for lvl, hid, t in doc.toc if lvl == 2)
    toc_html = f'<div class="side-title">On this page</div><nav class="toc" aria-label="On this page">{toc}</nav>' if toc else ''
    updated = f'<span>Updated {escape(meta["updated"])}</span>' if meta.get('updated') else ''
    status = f'<span class="doc-status">{escape(meta["status"])}</span>' if meta.get('status') else ''
    src = f'https://github.com/rasyidf/Toucan/blob/main/{path}'
    html = head(f'Toucan · {title}', meta.get('summary', title), page, '') + f'''
<main id="main" class="wrap doc-main">
  <div class="doc-layout">
    <aside class="doc-side" aria-label="Documentation">
      <div class="side-title"><a href="docs.html">All docs</a></div>
      <nav class="guides" aria-label="Guides">
{guide_list(page)}
      </nav>
      {toc_html}
    </aside>
    <article class="doc">
      <header class="doc-head">
        <div class="kicker">Documentation</div>
        <h1>{escape(title)}</h1>
        <p class="lead">{escape(meta.get("summary", ""))}</p>
        <div class="doc-meta">{status}{updated}<a href="{attr(src)}">Edit on GitHub ↗</a></div>
      </header>
      <div class="prose">
{body}
      </div>
    </article>
  </div>
</main>
''' + FOOT + (MERMAID if 'class="mermaid"' in body else '') + '</body>\n</html>\n'
    (ROOT / 'docs' / page).write_text(html)


def build_index():
    sections = []
    for name, blurb, paths in GROUPS:
        cards = ''.join(
            f'<a class="doc-card" href="{PAGES[p]}"><strong>{escape(meta_of(p)["title"])}</strong><span>{escape(meta_of(p).get("summary", ""))}</span></a>'
            for p in paths)
        sections.append(f'<section class="doc-group"><h2>{escape(name)}</h2><p>{escape(blurb)}</p><div class="doc-cards">{cards}</div></section>')
    extras = ('<section class="doc-group"><h2>Also on the site</h2><p>Release history and the project on GitHub.</p><div class="doc-cards">'
              '<a class="doc-card" href="changelog.html"><strong>Changelog</strong><span>Every documented release, searchable.</span></a>'
              '<a class="doc-card" href="https://github.com/rasyidf/Toucan#build-from-source"><strong>Build from source ↗</strong><span>Clone, build and run Toucan yourself. On GitHub.</span></a>'
              '</div></section>')
    html = head('Toucan · Documentation', 'Guides for Toucan: AI Integration, provider settings, plugins, architecture, roadmap and known issues.', 'docs.html') + f'''
<main id="main" class="wrap doc-main">
  <header class="doc-intro">
    <div class="kicker">Documentation</div>
    <h1>Guides for<br>working with <span>Toucan.</span></h1>
    <p class="lead">Everything lives on this site. Source files, samples and the issue tracker are on GitHub.</p>
  </header>
  {''.join(sections)}
  {extras}
</main>
''' + FOOT + '</body>\n</html>\n'
    (ROOT / 'docs' / 'docs.html').write_text(html)


def main():
    for path in PAGES:
        build_page(path)
    build_index()
    print(f'Built {len(PAGES)} doc pages and docs.html.')


if __name__ == '__main__':
    main()
