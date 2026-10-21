[numthreads(64,1,1)] void P2G(uint p : SV_DispatchThreadID, uint lane : SV_GroupIndex)
{
    if (lane == 0) { minX = minY = minZ = (int)_GridN - 1; maxX = maxY = maxZ = 0; }
    for (uint slot = lane; slot < NODE_CACHE; slot += 64)
    { nodeX[slot] = 0; nodeY[slot] = 0; nodeZ[slot] = 0; nodeM[slot] = 0; }
    GroupMemoryBarrierWithGroupSync();
    if (p < _ParticleCount)
    {
        int3 lo = clamp(int3(_Particles[p].x.xyz * _InvDx - .5), 0, (int)_GridN - 1);
        int3 hi = clamp(int3(_Particles[p].x.xyz * _InvDx - .5) + 2, 0, (int)_GridN - 1);
        InterlockedMin(minX, lo.x); InterlockedMin(minY, lo.y); InterlockedMin(minZ, lo.z);
        InterlockedMax(maxX, hi.x); InterlockedMax(maxY, hi.y); InterlockedMax(maxZ, hi.z);
    }
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0)
    {
        cacheOrigin = uint3(minX, minY, minZ); cacheSize = uint3(maxX-minX+1, maxY-minY+1, maxZ-minZ+1);
        cacheVolume = cacheSize.x * cacheSize.y * cacheSize.z;
    }
    GroupMemoryBarrierWithGroupSync();
    if (p < _ParticleCount)
    {
    Particle q = _Particles[p];
    float3 x = q.x.xyz, v = q.v.xyz; float mass = q.v.w * _MassScale, volume = q.x.w, yieldStress = q.c0.w;
    // PhysFlow scripted interactions (pre-P2G particle velocity modifiers), masks set at rest.
    if (_Script != 0)
    {
        uint mask = asuint(q.c1.w);
        for (uint k = 0; k < _ModifierCount; k++)
        {
            if ((mask & (1u << k)) == 0) continue;
            Modifier m = _Modifiers[k]; uint type = (uint)m.pos.w;
            if (type == 0) { if (_Time >= m.size.w && _Time - _Dt < m.size.w) v += m.vec.xyz * q.c2.w; }
            else if (_Time >= m.size.w && _Time < m.vec.w)
            {
                if (type == 1) v = m.vec.xyz;
                else
                {
                    float3 o = x - m.pos.xyz;
                    v = m.normal.w * (dot(o, m.h1.xyz) * m.h2.xyz - dot(o, m.h2.xyz) * m.h1.xyz) + m.h1.w * m.normal.xyz;
                }
            }
        }
        if (_Time < q.f0.w) v = 0;
    }
    if (_GravityMode == 1) v += _Dt * _Gravity;
    float3x3 f = Rows(q.f0, q.f1, q.f2);

    float3x3 stress;
    if (_ReferenceConstitutive != 0)
    {
        if (_Material == 1 || _Material == 4) f = VonMises(f, yieldStress);
        else if (_Material == 2) f = Sand(f);
        else if (_Material == 3) f = Viscoplastic(f, yieldStress, _Dt, false);
        else if (_Material == 6) f = Viscoplastic(f, yieldStress, _Dt, true);
        stress = Stress(f);
    }
    else stress = ProjectAndStress(f, yieldStress);
    float3x3 c = Rows(q.c0, q.c1, q.c2);
    int3 base; float3 fx; float3 w[3], dw[3]; Weights(x, base, fx, w, dw);
    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++)
    {
        float weight = w[i].x * w[j].y * w[k].z;
        float3 grad = float3(dw[i].x * w[j].y * w[k].z, w[i].x * dw[j].y * w[k].z, w[i].x * w[j].y * dw[k].z) * _InvDx;
        float3 dpos = (float3(i, j, k) - fx) * _Dx;
        float3 momentum = weight * mass * (v + mul(c, dpos)) - _Dt * volume * mul(stress, grad);
        uint n = Node(base + int3(i, j, k)) * 4;
        CacheAdd(base + int3(i, j, k), momentum, weight * mass);
    }

    if (cacheVolume > NODE_CACHE)
        for (int bx=0; bx<2; bx++) for (int by=0; by<2; by++) for (int bz=0; bz<2; bz++) Mark(base + int3(bx,by,bz)*2);
    q.v.xyz = v; q.c0.w = yieldStress;
    q.f0.xyz = f[0]; q.f1.xyz = f[1]; q.f2.xyz = f[2];
    _Particles[p] = q;
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint flushSlot = lane; flushSlot < min(cacheVolume, (uint)NODE_CACHE); flushSlot += 64)
    {
        if (cacheVolume > NODE_CACHE) continue;
        if (nodeM[flushSlot] == 0 && nodeX[flushSlot] == 0 && nodeY[flushSlot] == 0 && nodeZ[flushSlot] == 0) continue;
        uint3 c = cacheOrigin + uint3(flushSlot % cacheSize.x, flushSlot / cacheSize.x % cacheSize.y, flushSlot / (cacheSize.x * cacheSize.y));
        uint raw = Node(c) * 4;
        InterlockedAdd(_Grid[raw], asuint(nodeX[flushSlot])); InterlockedAdd(_Grid[raw + 1], asuint(nodeY[flushSlot]));
        InterlockedAdd(_Grid[raw + 2], asuint(nodeZ[flushSlot])); InterlockedAdd(_Grid[raw + 3], asuint(nodeM[flushSlot]));
        Mark(c);
    }
}

[numthreads(1,1,1)] void Args(uint id : SV_DispatchThreadID)
{
    _Args[0] = _Counter[0]; _Args[1] = 1; _Args[2] = 1; _Counter[0] = 0;
}

