#!/usr/bin/env python3
"""Verify every public adapter against immutable Unity source snapshots. Standard library only."""
from __future__ import annotations
import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
PATCHES = ROOT / 'Packages/com.voidlogistics.urp-stable-projection/Editor/Patches'

def normalize(text):
    return text.replace('\r\n', '\n').replace('\r', '\n').lstrip('\ufeff')

def digest(text):
    return hashlib.sha256(normalize(text).encode('utf-8')).hexdigest()

def apply(source, edits):
    result = normalize(source)
    for edit in edits:
        if result.count(edit['before']) != 1:
            raise ValueError('Expected exactly one matching edit block')
        result = result.replace(edit['before'], edit['after'], 1)
    return result

def verify_file(args):
    profile, reference, rule, cache = args
    relative = rule['path']
    if '..' in Path(relative).parts or Path(relative).is_absolute():
        raise ValueError('Unsafe patch path')
    destination = cache / reference['commit'] / relative
    if not destination.is_file():
        url = 'https://raw.githubusercontent.com/Unity-Technologies/Graphics/' + reference['commit'] + '/Packages/' + relative
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
    original = destination.read_text(encoding='utf-8-sig')
    if digest(original) != rule['beforeSha256']:
        raise AssertionError(profile['id'] + ': upstream fingerprint changed: ' + relative)
    patched = apply(original, rule['edits'])
    assert digest(patched) == rule['afterSha256'], relative
    assert apply(original.replace('\n', '\r\n'), rule['edits']) == patched
    # A second application must refuse rather than corrupt the already-patched source.
    try:
        apply(patched, rule['edits'])
    except ValueError:
        pass
    else:
        raise AssertionError('Double application was accepted: ' + relative)
    return {'profile': profile['id'], 'path': relative, 'edits': len(rule['edits']), 'passed': True}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, default=ROOT / '.cache/upstream')
    parser.add_argument('--report', type=Path, default=ROOT / '.cache/source-validation.json')
    args = parser.parse_args()
    refs = json.loads((ROOT / 'docs/upstream-references.json').read_text(encoding='utf-8'))
    work = []
    for path in sorted(PATCHES.glob('urp-*.json')):
        profile = json.loads(path.read_text(encoding='utf-8'))
        work.extend((profile, refs[profile['id']], rule, args.cache) for rule in profile['files'])
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        checks = list(pool.map(verify_file, work))
    report = {'passed': True, 'files': len(checks), 'edits': sum(c['edits'] for c in checks), 'checks': checks}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f"PASS: {len(checks)} files, {report['edits']} edit blocks; source hashes, patched hashes, CRLF, and double-apply refusal.")

if __name__ == '__main__':
    main()
