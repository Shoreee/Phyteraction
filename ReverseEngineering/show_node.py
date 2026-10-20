"""Pretty-print components of nodes in an extracted rig JSON, with references
resolved to node names. Usage: show_node.py rig.json NodeName [NodeName...]"""
import json, sys

SKIP_BUILTIN_KEYS = {'m_ObjectHideFlags', 'm_CorrespondingSourceObject', 'm_PrefabInstance', 'm_PrefabAsset'}

nodes = json.load(open(sys.argv[1]))
by_id = {n['id']: n for n in nodes}


def path(nid):
    parts = []
    while nid in by_id:
        parts.append(by_id[nid]['name']); nid = by_id[nid]['parent']
    return '/'.join(reversed(parts[:4]))


def fmt(v):
    if isinstance(v, dict):
        if '$ref' in v:
            r = v['$ref']
            if r is None: return 'null'
            if 'go' in r: return f'<{r.get("type", "GO")}@{path(r["go"]) if r["go"] in by_id else r["go"]}>'
            return f'<{r}>'
        return '{' + ', '.join(f'{k}: {fmt(x)}' for k, x in v.items() if k not in SKIP_BUILTIN_KEYS) + '}'
    if isinstance(v, list):
        return '[' + ', '.join(fmt(x) for x in v) + ']'
    if isinstance(v, float): return f'{v:.6g}'
    return str(v)


def full_path(nid):
    parts = []
    while nid in by_id:
        parts.append(by_id[nid]['name']); nid = by_id[nid]['parent']
    return '/'.join(reversed(parts))


for name in (a for a in sys.argv[2:] if not a.startswith('--')):
    for n in nodes:
        if not ('/' + full_path(n['id'])).endswith('/' + name): continue
        print(f'### {path(n["id"])}  layer={n["layer"]} active={n["active"]} pos={fmt(n["localPosition"])} rot={fmt(n["localRotation"])} scale={fmt(n["localScale"])}')
        for c in n['components']:
            if c['type'] == 'Script':
                print(f'  [{c["class"]}] enabled={c["enabled"]} {c["status"]}')
                for k, v in (c['fields'] or {}).items(): print(f'      {k} = {fmt(v)}')
            elif 'data' in c:
                print(f'  [{c["type"]}] ' + fmt(c['data']))
        print()
        if '--first' in sys.argv: break
