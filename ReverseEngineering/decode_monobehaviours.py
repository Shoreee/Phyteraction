"""Decode serialized MonoBehaviour / ScriptableObject payloads without type
trees, using field layouts recovered by il2cpp_types.TypeResolver.

A payload is marked `ok` only if decoding consumes exactly all bytes, which is a
strong check that the reconstructed layout matches the original serializer.
"""
import json, struct, sys, collections
from pathlib import Path
import UnityPy
from il2cpp_types import TypeResolver, APP

DATA = APP / 'Data'
OUT = Path(__file__).parent / 'output'
r = TypeResolver()
m, b = r.m, r.bin

TYPE_SERIALIZABLE = 0x2000
PRIM = {2: ('?', 1), 3: ('H', 2), 4: ('b', 1), 5: ('B', 1), 6: ('h', 2), 7: ('H', 2), 8: ('i', 4), 9: ('I', 4),
        10: ('q', 8), 11: ('Q', 8), 12: ('f', 4), 13: ('d', 8)}
UNITY_STRUCTS = {
    'UnityEngine.Vector2': 'ff', 'UnityEngine.Vector3': 'fff', 'UnityEngine.Vector4': 'ffff',
    'UnityEngine.Quaternion': 'ffff', 'UnityEngine.Color': 'ffff', 'UnityEngine.Rect': 'ffff',
    'UnityEngine.Bounds': 'ffffff', 'UnityEngine.Matrix4x4': 'f' * 16, 'UnityEngine.LayerMask': 'I',
    'UnityEngine.Vector2Int': 'ii', 'UnityEngine.Vector3Int': 'iii', 'UnityEngine.Color32': 'I',
    'UnityEngine.RangeInt': 'ii',
}
NAMES_BY_FULL = {}
for i, n in r.full_names.items():
    NAMES_BY_FULL.setdefault(n, i)
UNITY_OBJECT = NAMES_BY_FULL['UnityEngine.Object']


class Type:
    __slots__ = ('kind', 'et', 'ti', 'args', 'elem', 'name')

    def __repr__(self): return self.name


def parse_type(off, gmap=None):
    data = b.q(off)
    bits = struct.unpack_from('<I', b.b, off + 8)[0]
    et = (bits >> 16) & 0xFF
    t = Type(); t.et = et; t.ti = None; t.args = []; t.elem = None
    if et in PRIM: t.kind = 'prim'
    elif et == 14: t.kind = 'string'
    elif et in (0x11, 0x12): t.kind = 'def'; t.ti = data
    elif et == 0x1d: t.kind = 'array'; t.elem = parse_type(b.ptr(off), gmap)
    elif et == 0x15:
        gc = b.ptr(off)
        base = parse_type(b.ptr(gc), gmap)
        inst = b.ptr(gc + 8)
        argc, argv = b.q(inst), b.ptr(inst + 8)
        t.kind = 'def'; t.ti = base.ti
        t.args = [parse_type(b.ptr(argv + k * 8), gmap) for k in range(argc)]
    elif et == 0x13:
        gp = m.gparams[data]
        if gmap and gp[4] < len(gmap): return gmap[gp[4]]
        t.kind = 'other'
    else: t.kind = 'other'
    t.name = r.name_of_type_ptr(off)
    return t


def type_from_index(idx, gmap=None):
    return parse_type(b.ptr(r.types_ptr + idx * 8), gmap)


def is_unity_object(ti):
    seen = 0
    while ti is not None and ti >= 0 and seen < 40:
        if ti == UNITY_OBJECT: return True
        p = m.types[ti][4]
        if p < 0: return False
        ti = type_from_index(p).ti
        seen += 1
    return False


def parent_chain_fields(ti, gargs):
    """Serialized fields of type `ti` including inherited ones, base first."""
    chain = []
    cur, cur_args = ti, gargs
    while cur is not None:
        name = r.full_names[cur]
        if name in ('UnityEngine.MonoBehaviour', 'UnityEngine.ScriptableObject', 'UnityEngine.Behaviour',
                    'UnityEngine.Component', 'UnityEngine.Object', 'System.Object', 'System.ValueType'):
            break
        chain.append((cur, cur_args))
        p = m.types[cur][4]
        if p < 0: break
        pt = type_from_index(p, cur_args)
        cur, cur_args = pt.ti, pt.args
    fields = []
    for cti, cargs in reversed(chain):
        t = m.types[cti]
        img = r.image_of_type(cti)
        for fi in range(t[8], t[8] + t[18]):
            f = m.fields[fi]
            ft = r.il2cpp_type(f[1])
            attrs = ft['attrs']
            if attrs & 0x10 or attrs & 0x40 or attrs & 0x20 or attrs & 0x80: continue
            cattrs = r.attributes_for_token(img, f[2])
            if (attrs & 7) != 6 and 'SerializeField' not in cattrs: continue
            if 'SerializeReference' in cattrs:
                raise NotImplementedError('SerializeReference')
            fields.append((m.s(f[0]), type_from_index(f[1], cargs)))
    return fields


class Reader:
    def __init__(self, data):
        self.d, self.p = data, 0

    def align(self):
        self.p = (self.p + 3) & ~3

    def unpack(self, fmt):
        v = struct.unpack_from('<' + fmt, self.d, self.p)
        self.p += struct.calcsize('<' + fmt)
        return v

    def i32(self): return self.unpack('i')[0]

    def string(self):
        n = self.i32()
        if n < 0 or self.p + n > len(self.d): raise ValueError('bad string length')
        s = self.d[self.p:self.p + n].decode('utf8', 'replace'); self.p += n; self.align(); return s

    def pptr(self):
        fid, pid = self.unpack('iq')
        return None if pid == 0 else {'fileID': fid, 'pathID': pid}


def is_serializable_type(t, depth):
    if t.kind in ('prim', 'string'): return True
    if t.kind == 'array': return t.elem.kind != 'array' and is_serializable_type(t.elem, depth)
    if t.kind != 'def': return False
    name = r.full_names[t.ti]
    if name in UNITY_STRUCTS or name in ('UnityEngine.AnimationCurve', 'UnityEngine.Gradient'): return True
    if name.startswith('System.Collections.Generic.List'):
        return t.args[0].kind != 'array' and not (t.args[0].kind == 'def' and
               r.full_names[t.args[0].ti].startswith('System.Collections.Generic.List')) and \
               is_serializable_type(t.args[0], depth)
    if is_unity_object(t.ti): return True
    parent = m.types[t.ti][4]
    pname = type_from_index(parent).name if parent >= 0 else None
    if pname == 'Enum': return True
    if is_unity_event(t.ti): return True
    if name.startswith('System.') or name.startswith('UnityEngine.') and not (m.types[t.ti][7] & TYPE_SERIALIZABLE):
        return False
    return bool(m.types[t.ti][7] & TYPE_SERIALIZABLE) and depth < 8


def is_unity_event(ti):
    cur = ti
    for _ in range(10):
        if r.full_names[cur] == 'UnityEngine.Events.UnityEventBase': return True
        p = m.types[cur][4]
        if p < 0: return False
        cur = type_from_index(p).ti
        if cur is None: return False
    return False


def read_unity_event(rd):
    calls = []
    for _ in range(rd.i32()):
        c = {'target': rd.pptr(), 'targetType': rd.string(), 'method': rd.string(), 'mode': rd.i32()}
        c['args'] = {'object': rd.pptr(), 'objectType': rd.string(), 'int': rd.i32(),
                     'float': rd.unpack('f')[0], 'string': rd.string(), 'bool': rd.unpack('?')[0]}
        rd.align()
        c['callState'] = rd.i32()
        calls.append(c)
    return {'calls': calls}


def read_value(rd, t, depth=0):
    if t.kind == 'prim':
        fmt, size = PRIM[t.et]
        v = rd.unpack(fmt)[0]
        if size < 4: rd.align()
        return v
    if t.kind == 'string': return rd.string()
    if t.kind == 'array':
        return read_list(rd, t.elem, depth)
    name = r.full_names[t.ti]
    if name in UNITY_STRUCTS:
        v = rd.unpack(UNITY_STRUCTS[name])
        return list(v) if len(v) > 1 else v[0]
    if name == 'UnityEngine.AnimationCurve':
        keys = [rd.unpack('ffffifi') for _ in range(rd.i32())]
        pre, post, order = rd.unpack('iii')
        return {'keys': [dict(zip(('time', 'value', 'inSlope', 'outSlope', 'weightedMode', 'inWeight',
                                   'outWeight'), k)) for k in keys], 'pre': pre, 'post': post}
    if name == 'UnityEngine.Gradient':
        colors = [rd.unpack('ffff') for _ in range(8)]
        ctime = rd.unpack('8H'); atime = rd.unpack('8H')
        mode, cspace, nc, na = rd.unpack('BBBB')
        return {'mode': mode, 'colorKeys': [{'color': colors[k][:3], 'time': ctime[k] / 65535} for k in range(nc)],
                'alphaKeys': [{'alpha': colors[k][3], 'time': atime[k] / 65535} for k in range(na)]}
    if name.startswith('System.Collections.Generic.List'):
        return read_list(rd, t.args[0], depth)
    if is_unity_object(t.ti): return rd.pptr()
    parent = m.types[t.ti][4]
    if parent >= 0 and type_from_index(parent).name == 'Enum':
        under = next(type_from_index(m.fields[fi][1]) for fi in range(m.types[t.ti][8], m.types[t.ti][8] + m.types[t.ti][18])
                     if m.s(m.fields[fi][0]) == 'value__')
        v = read_value(rd, under)
        return {'enum': name.split('.')[-1], 'value': v, 'name': enum_name(t.ti, v)}
    if is_unity_event(t.ti): return read_unity_event(rd)
    return read_object(rd, t.ti, t.args, depth + 1)


def enum_name(ti, v):
    from dump_signatures import const_value
    t = m.types[ti]
    names = [m.s(m.fields[fi][0]) for fi in range(t[8], t[8] + t[18]) if const_value(fi) == v]
    return '|'.join(n for n in names if n != 'value__') or None


def read_list(rd, elem, depth):
    n = rd.i32()
    if n < 0 or n > 1_000_000: raise ValueError(f'bad array length {n}')
    out = [read_value(rd, elem, depth) for _ in range(n)]
    rd.align()
    return out


def read_object(rd, ti, gargs, depth):
    out = {}
    for fname, ft in parent_chain_fields(ti, gargs):
        if not is_serializable_type(ft, depth): continue
        out[fname] = read_value(rd, ft, depth)
    return out


def script_index():
    idx = {}
    for im in m.images:
        asm = m.s(im[0]).replace('.dll', '')
        for ti in range(im[2], im[2] + im[3]):
            t = m.types[ti]
            idx[(asm, m.s(t[1]), m.s(t[0]))] = ti
    return idx


def decode_file(path, scripts, want=None):
    env = UnityPy.load(str(path))
    results = []
    for obj in env.objects:
        if obj.assets_file.name != path.name or obj.type.name != 'MonoBehaviour': continue
        raw = obj.get_raw_data()
        rd = Reader(raw)
        entry = {'file': path.name, 'path_id': obj.path_id}
        try:
            go = rd.pptr(); enabled = rd.unpack('B')[0]; rd.align(); script = rd.pptr(); name = rd.string()
            mb = obj.read(check_read=False)
            sc = mb.m_Script.read()
            key = (sc.m_AssemblyName.replace('.dll', ''), sc.m_Namespace, sc.m_ClassName)
            entry.update(game_object=go['pathID'] if go else None, enabled=enabled, name=name,
                         cls=(sc.m_Namespace + '.' if sc.m_Namespace else '') + sc.m_ClassName, assembly=key[0])
            if want and sc.m_ClassName not in want: continue
            ti = scripts.get(key)
            if ti is None: entry['status'] = 'no_type'; results.append(entry); continue
            entry['fields'] = read_object(rd, ti, [], 0)
            entry['status'] = 'ok' if rd.p == len(raw) else f'size_mismatch {rd.p}/{len(raw)}'
        except Exception as e:
            entry['status'] = f'error {type(e).__name__}: {str(e)[:120]} @ {rd.p}/{len(raw)}'
        results.append(entry)
    return results


if __name__ == '__main__':
    scripts = script_index()
    files = sys.argv[1:] or ['level0', 'level1', 'globalgamemanagers.assets', 'resources.assets',
                             'sharedassets0.assets', 'sharedassets1.assets']
    OUT.mkdir(exist_ok=True)
    for fn in files:
        res = decode_file(DATA / fn, scripts)
        c = collections.Counter(x.get('status', '?').split(' ')[0] for x in res)
        print(fn, dict(c), flush=True)
        with (OUT / f'mb-{fn}.jsonl').open('w') as f:
            for x in res: f.write(json.dumps(x, ensure_ascii=False) + '\n')
