"""Recover IL2CPP type information from global-metadata.dat (v31) plus the
unencrypted __DATA_CONST/__DATA segments of UnityFramework.

Only the Il2CppMetadataRegistration is used (types table). Method bodies live in
the FairPlay-encrypted __TEXT segment and are NOT recovered.
"""
import struct, json, sys
from pathlib import Path

APP = Path('/Users/rollowu/Downloads/HPL-Analysis/extracted/Payload/HandPhysicsLab.app')
META = APP / 'Data/Managed/Metadata/global-metadata.dat'
BIN = APP / 'Frameworks/UnityFramework.framework/UnityFramework'

HEADER_TABLES = ['stringLiteral', 'stringLiteralData', 'string', 'events', 'properties', 'methods',
                 'parameterDefaultValues', 'fieldDefaultValues', 'fieldAndParameterDefaultValueData',
                 'fieldMarshaledSizes', 'parameters', 'fields', 'genericParameters',
                 'genericParameterConstraints', 'genericContainers', 'nestedTypes', 'interfaces',
                 'vtableMethods', 'interfaceOffsets', 'typeDefinitions', 'images', 'assemblies',
                 'fieldRefs', 'referencedAssemblies', 'attributeData', 'attributeDataRange',
                 'unresolvedIndirectCallParameterTypes', 'unresolvedIndirectCallParameterRanges',
                 'windowsRuntimeTypeNames', 'windowsRuntimeStrings', 'exportedTypeDefinitions']

ELEMENT = {1: 'void', 2: 'bool', 3: 'char', 4: 'sbyte', 5: 'byte', 6: 'short', 7: 'ushort', 8: 'int',
           9: 'uint', 10: 'long', 11: 'ulong', 12: 'float', 13: 'double', 14: 'string', 0x18: 'IntPtr',
           0x19: 'UIntPtr', 0x1c: 'object'}
BUILTIN_BY_NAME = {'System.' + n: s for n, s in [('Void', 'void'), ('Boolean', 'bool'), ('Char', 'char'),
                   ('SByte', 'sbyte'), ('Byte', 'byte'), ('Int16', 'short'), ('UInt16', 'ushort'),
                   ('Int32', 'int'), ('UInt32', 'uint'), ('Int64', 'long'), ('UInt64', 'ulong'),
                   ('Single', 'float'), ('Double', 'double'), ('String', 'string'), ('Object', 'object')]}


class Metadata:
    def __init__(self, path):
        b = self.b = path.read_bytes()
        assert struct.unpack_from('<II', b) == (0xFAB11BAF, 31)
        self.tab = {n: struct.unpack_from('<II', b, 8 + i * 8) for i, n in enumerate(HEADER_TABLES)}
        self.types = self.rows('typeDefinitions', '<16i8H2I')
        self.methods = self.rows('methods', '<7i4H')
        self.fields = self.rows('fields', '<3i')
        self.params = self.rows('parameters', '<3i')
        self.images = self.rows('images', '<10i')
        self.gparams = self.rows('genericParameters', '<iihhHH')
        self.gcontainers = self.rows('genericContainers', '<iiii')
        self.nested = self.rows('nestedTypes', '<i')
        self.attr_ranges = self.rows('attributeDataRange', '<II')
        self.field_defaults = self.rows('fieldDefaultValues', '<iii')

    def rows(self, name, fmt):
        off, size = self.tab[name]
        sz = struct.calcsize(fmt)
        assert size % sz == 0, (name, size, sz)
        return list(struct.iter_unpack(fmt, self.b[off:off + size]))

    def s(self, i):
        so, _ = self.tab['string']
        return self.b[so + i:self.b.index(b'\0', so + i)].decode('utf8')


def read_uleb(b, p):
    r = s = 0
    while True:
        x = b[p]; p += 1
        r |= (x & 0x7f) << s; s += 7
        if not x & 0x80: return r, p


def read_compressed_u32(b, p):
    x = b[p]
    if x & 0x80 == 0: return x, p + 1
    if x & 0xC0 == 0x80: return ((x & 0x3F) << 8) | b[p + 1], p + 2
    if x & 0xE0 == 0xC0: return ((x & 0x1F) << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3], p + 4
    if x == 0xF0: return struct.unpack_from('<I', b, p + 1)[0], p + 5
    if x == 0xFE: return 0xFFFFFFFE, p + 1
    if x == 0xFF: return 0xFFFFFFFF, p + 1
    raise ValueError(x)


def read_compressed_i32(b, p):
    u, p = read_compressed_u32(b, p)
    if u == 0xFFFFFFFF: return -2147483648, p
    return (-(u >> 1) - 1) if (u & 1) else (u >> 1), p


class Binary:
    def __init__(self, path):
        self.b = path.read_bytes()

    def ptr(self, off):
        v = struct.unpack_from('<Q', self.b, off)[0]
        if v == 0: return 0
        if v >> 63: return None  # bind
        # DYLD_CHAINED_PTR_64_OFFSET rebase: target(36) high8(8)
        return (v & 0xFFFFFFFFF) | (((v >> 36) & 0xFF) << 56)

    def q(self, off): return struct.unpack_from('<Q', self.b, off)[0]

    def find_metadata_registration(self, type_def_count):
        b = self.b
        start, end = 104996672, 114011760
        needle = struct.pack('<Q', type_def_count)
        p = b.find(needle, start, end)
        while p != -1:
            if p % 8 == 0 and b[p + 16:p + 24] == needle:
                reg = p - 10 * 8
                types_count = self.q(reg + 6 * 8)
                if 1000 < types_count < 10_000_000 and self.ptr(reg + 7 * 8):
                    return reg
            p = b.find(needle, p + 1, end)
        raise RuntimeError('MetadataRegistration not found')


class TypeResolver:
    def __init__(self):
        self.m = Metadata(META)
        self.bin = Binary(BIN)
        reg = self.bin.find_metadata_registration(len(self.m.types))
        self.types_count = self.bin.q(reg + 6 * 8)
        self.types_ptr = self.bin.ptr(reg + 7 * 8)
        self.full_names = {}
        for i, t in enumerate(self.m.types):
            self.full_names[i] = self._def_name(i)
        # declaring type for nested types
        self.declaring = {}
        for i, t in enumerate(self.m.types):
            nested_start, nested_count = t[12], t[20]
            for n in self.m.nested[nested_start:nested_start + nested_count]:
                self.declaring[n[0]] = i
        self.full_names = {i: self._qualified(i) for i in range(len(self.m.types))}

    def _def_name(self, i):
        t = self.m.types[i]
        return self.m.s(t[0])

    def _qualified(self, i):
        t = self.m.types[i]
        name = self.m.s(t[0])
        if i in self.declaring:
            return self._qualified(self.declaring[i]) + '.' + name
        ns = self.m.s(t[1])
        return f'{ns}.{name}' if ns else name

    def il2cpp_type(self, index):
        off = self.bin.ptr(self.types_ptr + index * 8)
        data = self.bin.q(off)
        bits = struct.unpack_from('<I', self.bin.b, off + 8)[0]
        return {'off': off, 'data': data, 'attrs': bits & 0xFFFF, 'type': (bits >> 16) & 0xFF,
                'byref': (bits >> 29) & 1, 'valuetype': (bits >> 31) & 1}

    def name_of_type_ptr(self, off, short=True):
        data = self.bin.q(off)
        bits = struct.unpack_from('<I', self.bin.b, off + 8)[0]
        return self._name(data, (bits >> 16) & 0xFF, off, short)

    def _clean(self, full, short):
        if full in BUILTIN_BY_NAME: return BUILTIN_BY_NAME[full]
        name = full.split('`')[0]
        if short:
            for pre in ('UnityEngine.', 'System.Collections.Generic.', 'System.', 'Unity.Mathematics.'):
                if name.startswith(pre) and '.' not in name[len(pre):]:
                    return name[len(pre):]
        return name

    def _name(self, data, et, off, short):
        if et in ELEMENT: return ELEMENT[et]
        if et in (0x11, 0x12):  # VALUETYPE / CLASS -> typeDefinitionIndex
            return self._clean(self.full_names.get(data, f'?{data}'), short)
        if et == 0x1d:  # SZARRAY
            return self.name_of_type_ptr(self.bin.ptr(off), short) + '[]'
        if et == 0x14:  # ARRAY -> Il2CppArrayType*
            arr = self.bin.ptr(off)
            elem = self.bin.ptr(arr)
            rank = self.bin.b[arr + 8]
            return self.name_of_type_ptr(elem, short) + '[' + ',' * (rank - 1) + ']'
        if et == 0x0f:  # PTR
            return self.name_of_type_ptr(self.bin.ptr(off), short) + '*'
        if et in (0x13, 0x1e):  # VAR / MVAR -> genericParameterIndex
            gp = self.m.gparams[data]
            return self.m.s(gp[1])
        if et == 0x15:  # GENERICINST -> Il2CppGenericClass*
            gc = self.bin.ptr(off)
            base_type = self.bin.ptr(gc)
            base = self.name_of_type_ptr(base_type, short)
            inst = self.bin.ptr(gc + 8)
            argc = self.bin.q(inst)
            argv = self.bin.ptr(inst + 8)
            args = [self.name_of_type_ptr(self.bin.ptr(argv + k * 8), short) for k in range(argc)]
            if base == 'Nullable' and len(args) == 1: return args[0] + '?'
            return f'{base}<{", ".join(args)}>'
        return f'<et{et:#x}>'

    def type_name(self, index, short=True):
        off = self.bin.ptr(self.types_ptr + index * 8)
        return self.name_of_type_ptr(off, short)

    # ---- custom attributes (v29+ attributeData blob) ----
    def attributes_for_token(self, image_index, token):
        img = self.m.images[image_index]
        start, count = img[8], img[9]  # customAttributeStart, customAttributeCount
        lo, hi = start, start + count
        while lo < hi:
            mid = (lo + hi) // 2
            if self.m.attr_ranges[mid][0] < token: lo = mid + 1
            else: hi = mid
        if lo >= start + count or self.m.attr_ranges[lo][0] != token: return []
        begin = self.m.attr_ranges[lo][1]
        end = self.m.attr_ranges[lo + 1][1] if lo + 1 < len(self.m.attr_ranges) else self.m.tab['attributeData'][1]
        base = self.m.tab['attributeData'][0]
        blob = self.m.b[base + begin: base + end]
        n, p = read_compressed_u32(blob, 0)
        names = []
        for k in range(n):
            ctor = struct.unpack_from('<i', blob, p + 4 * k)[0]
            decl = self.m.methods[ctor][1]
            names.append(self.full_names[decl].split('.')[-1].replace('Attribute', ''))
        return names

    def image_of_type(self, ti):
        for idx, im in enumerate(self.m.images):
            if im[2] <= ti < im[2] + im[3]: return idx
        return None


FIELD_PUBLIC, FIELD_STATIC, FIELD_LITERAL, FIELD_INIT_ONLY, FIELD_NOT_SERIALIZED = 0x6, 0x10, 0x40, 0x20, 0x80


def describe_type(r, ti):
    t = r.m.types[ti]
    img = r.image_of_type(ti)
    parent = r.type_name(t[4]) if t[4] >= 0 else None
    flags = t[7]
    fields = []
    for fi in range(t[8], t[8] + t[18]):
        f = r.m.fields[fi]
        ty = r.il2cpp_type(f[1])
        attrs = ty['attrs']
        fields.append({'name': r.m.s(f[0]), 'type': r.type_name(f[1]), 'attrs': attrs,
                       'public': (attrs & 0x7) == FIELD_PUBLIC, 'static': bool(attrs & FIELD_STATIC),
                       'const': bool(attrs & FIELD_LITERAL), 'readonly': bool(attrs & FIELD_INIT_ONLY),
                       'not_serialized': bool(attrs & FIELD_NOT_SERIALIZED),
                       'custom_attributes': r.attributes_for_token(img, f[2])})
    methods = []
    for mi in range(t[9], t[9] + t[16]):
        m = r.m.methods[mi]
        pars = [(r.m.s(p[0]), r.type_name(p[2])) for p in r.m.params[m[4]:m[4] + m[10]]]
        methods.append({'name': r.m.s(m[0]), 'return': r.type_name(m[2]), 'params': pars, 'flags': m[7],
                        'custom_attributes': r.attributes_for_token(img, m[6])})
    return {'index': ti, 'full_name': r.full_names[ti], 'parent': parent, 'flags': flags,
            'is_enum': parent == 'Enum', 'is_valuetype': parent in ('ValueType', 'Enum'),
            'custom_attributes': r.attributes_for_token(img, t[25]),
            'fields': fields, 'methods': methods}


if __name__ == '__main__':
    r = TypeResolver()
    print('types table entries', r.types_count)
    for q in sys.argv[1:]:
        for i, n in r.full_names.items():
            if n.split('.')[-1] == q:
                print(json.dumps(describe_type(r, i), indent=1))
