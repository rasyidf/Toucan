"""Which repo docs are published as pages on the website, shared by build-docs.py and generate-changelog.py."""
from html import escape
from pathlib import PurePosixPath
import posixpath
from urllib.parse import urlsplit

REPO = 'https://github.com/rasyidf/Toucan'

# Repo path (relative to the repo root) -> page file in docs/.
PAGES = {
    'docs/plugins.md': 'plugins.html',
    'docs/ai-integration.md': 'ai-integration.html',
    'docs/provider-settings.md': 'provider-settings.html',
    'docs/pretranslation-preview.md': 'pretranslation-preview.html',
    'docs/visual-review.md': 'visual-review.html',
    'docs/ARCHITECTURE.md': 'architecture.html',
    'Toucan.Core/ARCHITECTURE.md': 'core-architecture.html',
    'docs/todos/future-roadmap.md': 'roadmap.html',
    'docs/known-bugs.md': 'known-bugs.html',
    'docs/completed-features.md': 'completed-features.html',
    'docs/branding.md': 'branding.html',
}


def resolve(target, source='CHANGELOG.md'):
    """Turn a Markdown link target written in `source` into a site-relative page link or a GitHub URL."""
    scheme = urlsplit(target).scheme
    if scheme:
        if scheme not in ('https', 'http'):
            raise ValueError(f'Unsupported link scheme: {target}')
        return target
    if target.startswith('#'):
        return target
    path, _, fragment = target.partition('#')
    repo_path = posixpath.normpath(posixpath.join(posixpath.dirname(source), path))
    if repo_path in PAGES:
        return PAGES[repo_path] + (f'#{fragment}' if fragment else '')
    if repo_path.startswith('..'):
        raise ValueError(f'Link leaves the repo: {target} in {source}')
    if repo_path in ('docs/index.html', 'docs/changelog.html'):
        return repo_path[len('docs/'):] + (f'#{fragment}' if fragment else '')
    kind = 'tree' if PurePosixPath(repo_path).suffix == '' else 'blob'
    return f'{REPO}/{kind}/main/{repo_path}' + (f'#{fragment}' if fragment else '')


def attr(url):
    return escape(url, quote=True)
