"""Emit pseudo-C# signature files (fields, attributes, method signatures, enum
values) for the game assemblies. Output is reference material, not compilable."""
import json, struct, re
from pathlib import Path
from il2cpp_types import TypeResolver, describe_type, read_compressed_i32, read_compressed_u32

OUT = Path(__file__).parent / 'output'
ASSEMBLIES = ('Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll')

r = TypeResolver()
m = r.m
defaults = {fd[0]: (fd[1], fd[2]) for fd in m.field_defaults}
data_base = m.tab['fieldAndParameterDefaultValueData'][0]


def const_value(field_index):
    if field_index not in defaults: return None
    tidx, didx = defaults[field_index]
    if didx < 0: return None
    et = r.il2cpp_type(tidx)['type']
    p = data_base + didx
    b = m.b
    if et in (4,): return struct.unpack_from('<b', b, p)[0]
    if et in (5, 2): return b[p]
    if et == 6: return struct.unpack_from('<h', b, p)[0]
    if et == 7: return struct.unpack_from('<H', b, p)[0]
    if et == 8: return read_compressed_i32(b, p)[0]
    if et == 9: return read_compressed_u32(b, p)[0]
    if et == 12: return struct.unpack_from('<f', b, p)[0]
    if et == 13: return struct.unpack_from('<d', b, p)[0]
    if et == 14:
        n, q = read_compressed_i32(b, p)
        return json.dumps(b[q:q + n].decode('utf8', 'replace')) if n >= 0 else 'null'
    return None


def attr_str(attrs):
    keep = [a for a in attrs if a not in ('CompilerGenerated', 'DebuggerBrowsable', 'DebuggerHidden',
                                          'IteratorStateMachine', 'AsyncStateMachine', 'Nullable',
                                          'NullableContext', 'IsReadOnly')]
    return ''.join(f'[{a}] ' for a in keep)


def render(ti):
    d = describe_type(r, ti)
    t = m.types[ti]
    lines = []
    kind = 'enum' if d['is_enum'] else 'struct' if d['is_valuetype'] else \
        'interface' if d['flags'] & 0x20 else 'class'
    head = attr_str(d['custom_attributes'])
    base = '' if d['parent'] in (None, 'object', 'ValueType', 'Enum') else f' : {d["parent"]}'
    lines.append(f'// TypeDefIndex {ti}')
    lines.append(f'{head}{"abstract " if d["flags"] & 0x80 and kind == "class" else ""}{kind} {d["full_name"]}{base}')
    lines.append('{')
    for k, f in enumerate(d['fields']):
        fi = t[8] + k
        if kind == 'enum':
            if f['name'] != 'value__': lines.append(f'    {f["name"]} = {const_value(fi)},')
            continue
        mods = ('public ' if f['public'] else 'private ') + ('static ' if f['static'] else '') + \
               ('const ' if f['const'] else '') + ('readonly ' if f['readonly'] else '')
        cv = const_value(fi) if f['const'] else None
        lines.append(f'    {attr_str(f["custom_attributes"])}{mods}{f["type"]} {f["name"]}'
                     f'{" = " + str(cv) if cv is not None else ""};')
    if kind != 'enum':
        if d['fields'] and d['methods']: lines.append('')
        for mt in d['methods']:
            mods = ('public ' if mt['flags'] & 0x7 == 6 else 'private ' if mt['flags'] & 0x7 == 1 else
                    'protected ' if mt['flags'] & 0x7 == 4 else 'internal ') + \
                   ('static ' if mt['flags'] & 0x10 else '') + ('abstract ' if mt['flags'] & 0x400 else
                   'virtual ' if mt['flags'] & 0x40 else '')
            ps = ', '.join(f'{ty} {n}' for n, ty in mt['params'])
            lines.append(f'    {attr_str(mt["custom_attributes"])}{mods}{mt["return"]} {mt["name"]}({ps});')
    lines.append('}')
    return d, '\n'.join(lines)


def main():
    sig_dir = OUT / 'signatures'
    sig_dir.mkdir(parents=True, exist_ok=True)
    index = []
    for im_idx, im in enumerate(m.images):
        asm = m.s(im[0])
        if asm not in ASSEMBLIES: continue
        for ti in range(im[2], im[2] + im[3]):
            name = r.full_names[ti]
            if '<' in name: continue
            d, text = render(ti)
            if ti in r.declaring: continue  # nested types are appended to their parent file below
            nested = [n for n, p in r.declaring.items() if p == ti and '<' not in r.full_names[n]]
            for n in nested: text += '\n\n' + render(n)[1]
            rel = name.replace('.', '/') + '.cs'
            path = sig_dir / asm.replace('.dll', '') / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text + '\n', encoding='utf8')
            index.append({'assembly': asm, 'name': name, 'parent': d['parent'], 'file': str(path.relative_to(OUT))})
    (OUT / 'signature-index.json').write_text(json.dumps(index, indent=1, ensure_ascii=False))
    print('wrote', len(index), 'types')


if __name__ == '__main__':
    main()
