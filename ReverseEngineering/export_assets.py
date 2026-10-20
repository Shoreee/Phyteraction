"""Export meshes (with skinning), materials, textures and physic materials that
are referenced by extracted rig subtrees into an intermediate format consumed
by the Unity-side importer (Assets/_Project/Editor/Import/HplAssetImporter.cs).

Usage: export_assets.py rig.json [SubtreeRootPath ...]
"""
import json, sys, re
from pathlib import Path
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from il2cpp_types import APP

DATA = APP / 'Data'
OUT = Path(__file__).parent / 'output' / 'export'
SCENE_EXTERNALS = {1: 'globalgamemanagers.assets', 2: 'sharedassets0.assets', 3: 'resources.assets',
                   4: 'sharedassets1.assets'}

_envs = {}


def env_objects(fname):
    if fname not in _envs:
        env = UnityPy.load(str(DATA / fname))
        objs = {o.path_id: o for o in env.objects if o.assets_file.name == fname}
        ext = {i + 1: Path(e.path).name for i, e in enumerate(next(iter(objs.values())).assets_file.externals)}
        _envs[fname] = (objs, ext)
    return _envs[fname]


def safe(name):
    return re.sub(r'[^A-Za-z0-9_\-]+', '_', name).strip('_') or 'unnamed'


def asset_key(fname, pid):
    return f'{fname.split(".")[0]}_{pid}'


def resolve_pptr(fname, pptr):
    fid = pptr['m_FileID'] if 'm_FileID' in pptr else pptr.fileID if hasattr(pptr, 'fileID') else pptr['fileID']
    pid = pptr['m_PathID'] if 'm_PathID' in pptr else pptr.pathID if hasattr(pptr, 'pathID') else pptr['pathID']
    if pid == 0: return None
    if fid == 0: return fname, pid
    _, ext = env_objects(fname)
    target = ext.get(fid)
    if target is None or 'unity default resources' in target or 'unity_builtin_extra' in target:
        return ('builtin', pid)
    return target, pid


def export_texture(fname, pid, done):
    key = asset_key(fname, pid)
    if key in done: return key
    objs, _ = env_objects(fname)
    tex = objs[pid].read()
    img = tex.image
    path = OUT / 'Textures' / f'{safe(tex.m_Name)}__{key}.png'
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)
    done[key] = {'type': 'Texture2D', 'name': tex.m_Name, 'file': str(path.relative_to(OUT)),
                 'format': str(tex.m_TextureFormat), 'width': tex.m_Width, 'height': tex.m_Height}
    return key


def export_material(fname, pid, done):
    key = asset_key(fname, pid)
    if key in done: return key
    objs, _ = env_objects(fname)
    tree = objs[pid].read_typetree()
    shader_name = None
    sref = resolve_pptr(fname, tree['m_Shader'])
    if sref and sref[0] != 'builtin':
        sh = env_objects(sref[0])[0][sref[1]].read()
        shader_name = sh.m_ParsedForm.m_Name if getattr(sh, 'm_ParsedForm', None) else getattr(sh, 'm_Name', None)
    props = tree['m_SavedProperties']
    textures = {}
    for name, env in props['m_TexEnvs']:
        tref = resolve_pptr(fname, env['m_Texture'])
        if tref and tref[0] != 'builtin':
            try:
                textures[name] = {'texture': export_texture(tref[0], tref[1], done), 'scale': env['m_Scale'],
                                  'offset': env['m_Offset']}
            except Exception as e:
                textures[name] = {'error': str(e)[:200]}
    done[key] = {'type': 'Material', 'name': tree['m_Name'], 'shader': shader_name,
                 'keywords': tree.get('m_ValidKeywords', []), 'renderQueue': tree.get('m_CustomRenderQueue'),
                 'textures': textures, 'floats': dict(props['m_Floats']), 'colors': dict(props['m_Colors']),
                 'ints': dict(props.get('m_Ints', []))}
    return key


def export_mesh(fname, pid, done):
    key = asset_key(fname, pid)
    if key in done: return key
    objs, _ = env_objects(fname)
    mesh = objs[pid].read()
    h = MeshHandler(mesh)
    h.process()
    tris = h.get_triangles()
    data = {'name': mesh.m_Name, 'vertices': h.m_Vertices, 'normals': h.m_Normals, 'tangents': h.m_Tangents,
            'uv0': h.m_UV0, 'uv1': h.m_UV1, 'colors': h.m_Colors,
            'boneWeights': h.m_BoneWeights, 'boneIndices': h.m_BoneIndices,
            'bindposes': [[getattr(m, f'e{r}{c}') for r in range(4) for c in range(4)] for m in mesh.m_BindPose],
            'boneNameHashes': list(getattr(mesh, 'm_BoneNameHashes', []) or []),
            'submeshes': [[list(t) for t in sm] for sm in tris],
            'bounds': {'center': [mesh.m_LocalAABB.m_Center.x, mesh.m_LocalAABB.m_Center.y, mesh.m_LocalAABB.m_Center.z],
                       'extent': [mesh.m_LocalAABB.m_Extent.x, mesh.m_LocalAABB.m_Extent.y, mesh.m_LocalAABB.m_Extent.z]}}
    path = OUT / 'Meshes' / f'{safe(mesh.m_Name)}__{key}.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, separators=(',', ':')))
    done[key] = {'type': 'Mesh', 'name': mesh.m_Name, 'file': str(path.relative_to(OUT)),
                 'vertexCount': len(h.m_Vertices or []), 'bones': len(data['bindposes'])}
    return key


def export_physic_material(fname, pid, done):
    key = asset_key(fname, pid)
    if key in done: return key
    objs, _ = env_objects(fname)
    t = objs[pid].read_typetree()
    done[key] = {'type': 'PhysicsMaterial', 'name': t['m_Name'], 'data': t}
    return key


EXPORTERS = {'Mesh': export_mesh, 'Material': export_material, 'Texture2D': export_texture,
             'PhysicMaterial': export_physic_material, 'PhysicsMaterial': export_physic_material}


def collect_refs(v, out):
    if isinstance(v, dict):
        if '$ref' in v:
            r = v['$ref']
            if r and 'external' in r: out.add((SCENE_EXTERNALS.get(r['external']), r['pathID']))
            return
        for x in v.values(): collect_refs(x, out)
    elif isinstance(v, list):
        for x in v: collect_refs(x, out)


if __name__ == '__main__':
    nodes = json.load(open(sys.argv[1]))
    roots = sys.argv[2:]
    by_id = {n['id']: n for n in nodes}

    def full(nid):
        p = []
        while nid in by_id: p.append(by_id[nid]['name']); nid = by_id[nid]['parent']
        return '/'.join(reversed(p))

    refs = set()
    for n in nodes:
        if roots and not any(('/' + full(n['id']) + '/').find('/' + r + '/') >= 0 for r in roots): continue
        collect_refs(n['components'], refs)
    OUT.mkdir(parents=True, exist_ok=True)
    manifest_path = OUT / 'manifest.json'
    done = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}
    for fname, pid in sorted(r for r in refs if r[0]):
        objs, _ = env_objects(fname)
        o = objs.get(pid)
        if o is None: continue
        fn = EXPORTERS.get(o.type.name)
        if fn is None: continue
        try:
            fn(fname, pid, done)
        except Exception as e:
            done[asset_key(fname, pid)] = {'type': o.type.name, 'error': f'{type(e).__name__}: {str(e)[:200]}'}
    manifest_path.write_text(json.dumps(done, indent=1, ensure_ascii=False))
    import collections
    print(collections.Counter((v['type'], 'error' in v) for v in done.values()))
