void Weights(float3 x, out int3 base, out float3 fx, out float3 w[3], out float3 dw[3])
{
    float3 g = x * _InvDx; base = int3(g - .5); fx = g - base;
    w[0] = .5 * (1.5 - fx) * (1.5 - fx); w[1] = .75 - (fx - 1) * (fx - 1); w[2] = .5 * (fx - .5) * (fx - .5);
    dw[0] = fx - 1.5; dw[1] = -2 * (fx - 1); dw[2] = fx - .5;
}
uint Node(int3 c) { c = clamp(c, 0, (int)_GridN - 1); return (uint)c.x + _GridN * ((uint)c.y + _GridN * (uint)c.z); }
void Mark(int3 node)
{
    uint bn = _GridN / 4; int3 b = clamp(node >> 2, 0, (int)bn - 1); uint id = (uint)b.x + bn * ((uint)b.y + bn * (uint)b.z);
    if (_BlockFlag[id] != 0) return;
    uint old; InterlockedOr(_BlockFlag[id], 1, old);
    if (old == 0) { uint slot; InterlockedAdd(_Counter[0], 1, slot); _BlockList[slot] = id; }
}

// Morton ordered groups usually touch a compact grid box. Accumulate that box
// directly in threadgroup memory: no hash lookup, CAS retries or lost particles.
#define NODE_CACHE 512
groupshared int nodeX[NODE_CACHE], nodeY[NODE_CACHE], nodeZ[NODE_CACHE], nodeM[NODE_CACHE];
groupshared int minX, minY, minZ, maxX, maxY, maxZ;
groupshared uint3 cacheOrigin, cacheSize;
groupshared uint cacheVolume;
void CacheAdd(int3 coord, float3 momentum, float mass)
{
    coord = clamp(coord, 0, (int)_GridN - 1);
    if (cacheVolume <= NODE_CACHE)
    {
        uint3 c = (uint3)coord - cacheOrigin;
        uint slot = c.x + cacheSize.x * (c.y + cacheSize.y * c.z);
        int3 fixedP = (int3)round(clamp(momentum * _FixedMomentum, -2.0e9, 2.0e9));
        int fixedM = (int)round(clamp(mass * _FixedMass, -2.0e9, 2.0e9));
        if (fixedP.x != 0) InterlockedAdd(nodeX[slot], fixedP.x);
        if (fixedP.y != 0) InterlockedAdd(nodeY[slot], fixedP.y);
        if (fixedP.z != 0) InterlockedAdd(nodeZ[slot], fixedP.z);
        if (fixedM != 0) InterlockedAdd(nodeM[slot], fixedM);
    }
    else
    {
        uint raw = Node(coord) * 4;
        FixedAdd(raw, momentum.x, _FixedMomentum); FixedAdd(raw + 1, momentum.y, _FixedMomentum);
        FixedAdd(raw + 2, momentum.z, _FixedMomentum); FixedAdd(raw + 3, mass, _FixedMass);
    }
}

