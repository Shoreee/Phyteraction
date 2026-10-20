"""Extract scene subtrees (GameObjects, Transforms, built-in components and
decoded script fields) into JSON that the Unity-side importer can rebuild."""
import json, sys, collections
from pathlib import Path
import UnityPy
from il2cpp_types import APP

DATA = APP / 'Data'
OUT = Path(__file__).parent / 'output'

BUILTIN_KEEP = {'Transform', 'Rigidbody', 'ConfigurableJoint', 'FixedJoint', 'HingeJoint', 'CharacterJoint',
                'SpringJoint', 'BoxCollider', 'SphereCollider', 'CapsuleCollider', 'MeshCollider',
                'SkinnedMeshRenderer', 'MeshRenderer', 'MeshFilter', 'LineRenderer', 'AudioSource',
                'ParticleSystem', 'Light', 'Camera', 'Animator', 'TrailRenderer'}


def load_scene(fname):
    env = UnityPy.load(str(DATA / fname))
    objs = {o.path_id: o for o in env.objects if o.assets_file.name == fname}
    mbs = {}
    p = OUT / f'mb-{fname}.jsonl'
    for line in p.open():
        x = json.loads(line)
        mbs[x['path_id']] = x
    return env, objs, mbs


def build(fname):
    env, objs, mbs = load_scene(fname)
    gos, trs, comps = {}, {}, {}
    for pid, o in objs.items():
        t = o.type.name
        if t == 'GameObject':
            d = o.read_typetree()
            gos[pid] = {'name': d['m_Name'], 'active': d['m_IsActive'], 'layer': d['m_Layer'], 'tag': d.get('m_Tag'),
                        'components': [c['component']['m_PathID'] for c in d['m_Component']]}
        elif t in ('Transform', 'RectTransform'):
            d = o.read_typetree()
            trs[pid] = d
    return env, objs, mbs, gos, trs


def ref_name(ref, gos, trs, objs, mbs):
    if not ref or not isinstance(ref, dict) or not ref.get('pathID') and not ref.get('m_PathID'): return None
    fid = ref.get('fileID', ref.get('m_FileID'))
    pid = ref.get('pathID', ref.get('m_PathID'))
    if fid != 0: return {'external': fid, 'pathID': pid}
    o = objs.get(pid)
    if o is None: return {'pathID': pid}
    t = o.type.name
    if t == 'GameObject': return {'go': pid}
    if t in ('Transform', 'RectTransform'): return {'go': trs[pid]['m_GameObject']['m_PathID'], 'type': 'Transform'}
    if t == 'MonoBehaviour' and pid in mbs: return {'go': mbs[pid].get('game_object'), 'type': mbs[pid].get('cls'), 'pathID': pid}
    try:
        d = o.read_typetree()
        if 'm_GameObject' in d: return {'go': d['m_GameObject']['m_PathID'], 'type': t, 'pathID': pid}
        return {'asset': t, 'name': d.get('m_Name'), 'pathID': pid}
    except Exception:
        return {'asset': t, 'pathID': pid}


def resolve_refs(v, ctx):
    if isinstance(v, dict):
        if set(v.keys()) == {'fileID', 'pathID'} or set(v.keys()) == {'m_FileID', 'm_PathID'}:
            return {'$ref': ref_name(v, *ctx)}
        return {k: resolve_refs(x, ctx) for k, x in v.items()}
    if isinstance(v, list): return [resolve_refs(x, ctx) for x in v]
    return v


def subtree(root_go, env, objs, mbs, gos, trs):
    tr_by_go = {d['m_GameObject']['m_PathID']: pid for pid, d in trs.items()}
    ctx = (gos, trs, objs, mbs)
    nodes = []

    def visit(go_pid, parent_go):
        g = gos[go_pid]
        tpid = tr_by_go[go_pid]
        t = trs[tpid]
        node = {'id': go_pid, 'parent': parent_go, 'name': g['name'], 'active': g['active'], 'layer': g['layer'],
                'tag': g['tag'], 'localPosition': t['m_LocalPosition'], 'localRotation': t['m_LocalRotation'],
                'localScale': t['m_LocalScale'], 'components': []}
        for cpid in g['components']:
            o = objs.get(cpid)
            if o is None: continue
            ty = o.type.name
            if ty in ('Transform', 'RectTransform'): continue
            if ty == 'MonoBehaviour':
                x = mbs.get(cpid, {})
                node['components'].append({'type': 'Script', 'class': x.get('cls'), 'pathID': cpid,
                                           'enabled': x.get('enabled'), 'status': x.get('status'),
                                           'fields': resolve_refs(x.get('fields'), ctx)})
            elif ty in BUILTIN_KEEP:
                try:
                    d = o.read_typetree()
                except Exception as e:
                    d = {'error': str(e)}
                d.pop('m_GameObject', None)
                node['components'].append({'type': ty, 'pathID': cpid, 'data': resolve_refs(d, ctx)})
            else:
                node['components'].append({'type': ty, 'pathID': cpid})
        nodes.append(node)
        for child in t['m_Children']:
            visit(trs[child['m_PathID']]['m_GameObject']['m_PathID'], go_pid)

    visit(root_go, None)
    return nodes


def path_of(go_pid, gos, trs):
    tr_by_go = {d['m_GameObject']['m_PathID']: pid for pid, d in trs.items()}
    names = []
    tp = tr_by_go.get(go_pid)
    while tp:
        names.append(gos[trs[tp]['m_GameObject']['m_PathID']]['name'])
        tp = trs[tp]['m_Father']['m_PathID']
    return '/'.join(reversed(names))


def tree_text(nodes):
    depth = {}
    lines = []
    for n in nodes:
        d = depth[n['id']] = 0 if n['parent'] is None else depth[n['parent']] + 1
        comps = ', '.join((c.get('class') or c['type']).split('.')[-1] for c in n['components'])
        lines.append(f'{"  " * d}{n["name"]}{"" if n["active"] else " (inactive)"} [L{n["layer"]}] {{{comps}}}')
    return '\n'.join(lines)


if __name__ == '__main__':
    fname = sys.argv[1]
    roots = sys.argv[2:]
    env, objs, mbs, gos, trs = build(fname)
    tr_by_go = {d['m_GameObject']['m_PathID']: pid for pid, d in trs.items()}
    if not roots:
        for gid, g in gos.items():
            tp = tr_by_go.get(gid)
            if tp and trs[tp]['m_Father']['m_PathID'] == 0:
                print(gid, g['name'], g['active'])
        sys.exit()
    for root in roots:
        rid = int(root) if root.isdigit() else next(k for k, g in gos.items() if g['name'] == root)
        nodes = subtree(rid, env, objs, mbs, gos, trs)
        name = gos[rid]['name']
        print(path_of(rid, gos, trs), len(nodes), 'nodes')
        (OUT / f'rig-{fname}-{name}.json').write_text(json.dumps(nodes, indent=1, ensure_ascii=False))
        (OUT / f'rig-{fname}-{name}.txt').write_text(tree_text(nodes))
