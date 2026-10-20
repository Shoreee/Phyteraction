"""Inventory signature declarations against maintained implementations.
A name match is evidence of a rewrite, never evidence of original method recovery.
"""
import json
import re
from pathlib import Path
ROOT = Path(__file__).resolve().parent.parent
sources = ROOT / 'ReverseEngineering/output/signatures/Assembly-CSharp'
runtime = ROOT / 'Assets/_Project/Runtime'
implementations = {}
for path in sorted(runtime.rglob('*.cs')):
    for name in re.findall(r'\b(?:class|struct|enum|interface)\s+(\w+)', path.read_text()):
        implementations.setdefault(name, []).append(str(path.relative_to(ROOT)))
aliases = {'TrackedHandOpenXR': 'TrackedHandXR', 'GameSettingsHPL': 'GameSettings'}
rows = []
for path in sorted(sources.rglob('*.cs')):
    name = path.stem
    matches = implementations.get(aliases.get(name, name), [])
    rows.append({'signature': str(path.relative_to(ROOT)), 'name': name,
                 'implementation_candidates': matches,
                 'status': 'rewritten-name-match-needs-behavior-validation' if matches else 'not-migrated',
                 'original_method_bodies_available': False})
output = ROOT / 'ReverseEngineering/output/migration-audit.json'
output.write_text(json.dumps({'scope': 'Vision Pro hand physics laboratory, not the full original game',
    'signature_files': len(rows), 'matched_files': sum(bool(r['implementation_candidates']) for r in rows),
    'note': 'Name matches do not imply feature parity. Compiler generated types, enums, third party systems, and gameplay types are all included.',
    'entries': rows}, ensure_ascii=False, indent=2))
print(output)
print('Signatures:', len(rows), 'name matches:', sum(bool(r['implementation_candidates']) for r in rows))
