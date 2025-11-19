#!/usr/bin/env python3
"""
Tool: Convert a BabelEdit .babel XML file into a toucan.project JSON format.

Usage:
    python tools/babel2toucan.py d:\locales\qcash-ui.babel > toucan.project

This script mirrors BabelEdit metadata (no actual translation text) into the Toucan JSON schema.
"""
import xml.etree.ElementTree as ET
import json
import sys
from pathlib import Path


def node_text(elem, tag):
    child = elem.find(tag)
    return child.text if child is not None else None


def parse_translations(trans_elem):
    if trans_elem is None:
        return {}
    result = {}
    for tr in trans_elem.findall('translation'):
        lang = node_text(tr, 'language')
        approved_text = node_text(tr, 'approved')
        approved = approved_text.lower() == 'true' if approved_text else False
        result[lang] = {
            'approved': approved
        }
    return result


def parse_concept(concept):
    return {
        'name': node_text(concept, 'name'),
        'description': node_text(concept, 'description') or '',
        'comment': node_text(concept, 'comment') or '',
        'translations': parse_translations(concept.find('translations'))
    }


def parse_folder(node):
    # folder_node => may contain folder_node(s) and concept_node(s)
    name = node_text(node, 'name')
    result = { 'name': name, 'folders': [], 'concepts': [] }
    children = node.find('children')
    if children is None:
        return result
    for c in children:
        if c.tag == 'folder_node':
            result['folders'].append(parse_folder(c))
        elif c.tag == 'concept_node':
            result['concepts'].append(parse_concept(c))
        else:
            # ignore other node types
            continue
    return result


def parse_file(file_node):
    file_name = node_text(file_node, 'name')
    children = file_node.find('children')
    folders = []
    if children is not None:
        for ch in children:
            if ch.tag == 'folder_node':
                folders.append(parse_folder(ch))
            elif ch.tag == 'concept_node':
                # top-level concept in a file
                # Represent as a folderless concept
                folders.append({ 'name': '', 'folders':[], 'concepts':[parse_concept(ch)]})
    return {'name': file_name, 'folders': folders}


def parse_package(pkg):
    name = node_text(pkg, 'name')
    files = []
    children = pkg.find('children')
    if children is not None:
        for child in children:
            if child.tag == 'file_node':
                files.append(parse_file(child))
    return { 'name': name, 'files': files }


def main(argv):
    if len(argv) < 2:
        print('Usage: babel2toucan.py <path/to/project.babel>', file=sys.stderr)
        sys.exit(2)

    p = Path(argv[1])
    if not p.exists():
        print('File not found: ' + str(p), file=sys.stderr)
        sys.exit(2)

    tree = ET.parse(str(p))
    root = tree.getroot()

    project = {
        '$schema': './toucan.project.schema.json',
        'schemaVersion': '1.0.0',
        'projectName': node_text(root, 'filename') or p.stem,
        'beVersion': root.attrib.get('be_version'),
        'framework': node_text(root, 'framework'),
        'languages': [],
        'packages': []
    }

    # Prefer explicit <languages><language><code> entries when present
    langs = []
    langs_node = root.find('languages')
    if langs_node is not None:
        for l in langs_node.findall('language'):
            code = node_text(l, 'code')
            if code:
                langs.append(code)
    else:
        # Fall back to scanning <translation> nodes
        for tr in root.findall('.//translation'):
            lang = node_text(tr, 'language')
            if lang and lang not in langs:
                langs.append(lang)
    project['languages'] = sorted(langs)

    for pkg in root.findall('.//package_node'):
        project['packages'].append(parse_package(pkg))

    # translation_packages mapping (paths to language files)
    tp = []
    tp_node = root.find('translation_packages')
    if tp_node is not None:
        for tpackage in tp_node.findall('translation_package'):
            tname = node_text(tpackage, 'name') or ''
            turls = []
            urls = tpackage.find('translation_urls')
            if urls is not None:
                for url in urls.findall('translation_url'):
                    turls.append({
                        'path': node_text(url, 'path'),
                        'language': node_text(url, 'language')
                    })
            tp.append({'name': tname, 'translationUrls': turls})
    project['translationPackages'] = tp

    # embedded source, primary language, editor and configuration
    est = node_text(root, 'embedded_source_texts')
    project['embeddedSourceTexts'] = (est or 'false').lower() == 'true'
    project['primaryLanguage'] = node_text(root, 'primary_language')

    # editor_configuration => mapping of child text values
    editor_cfg = {}
    ed = root.find('editor_configuration')
    if ed is not None:
        for c in ed:
            # For repeated tags like copy_template, aggregate into a list
            if c.tag == 'copy_template':
                editor_cfg.setdefault('copy_templates', []).append(c.text or '')
            else:
                editor_cfg[c.tag] = c.text
    project['editorConfiguration'] = editor_cfg

    # configuration block, copy children into a dict
    cfg = {}
    conf = root.find('configuration')
    if conf is not None:
        for c in conf:
            cfg[c.tag] = c.text
    project['configuration'] = cfg

    json.dump(project, sys.stdout, indent=2, ensure_ascii=False)


if __name__ == '__main__':
    main(sys.argv)
