[numthreads(64,1,1)] void ContactBound(uint3 group : SV_GroupID, uint local : SV_GroupIndex)
{
    uint bn = _GridN / 4, block = _BlockList[group.x];
    uint3 b = uint3(block % bn, block / bn % bn, block / (bn * bn));
    if (local == 0) candidateCount = 0;
    GroupMemoryBarrierWithGroupSync();
    // Shapes whose bounds (at this substep) reach this 4^3 block.
    float3 lo = b * 4 * _Dx - _Skin, hi = (b * 4 + 3) * _Dx + _Skin;
    bool reachesShapes = all(hi >= _ShapeLo) && all(lo <= _ShapeHi);
    for (uint s = local; reachesShapes && s < _ShapeCount; s += 64)
    {
        Shape sh = _Shapes[s]; float3 slo, shi;
        float3 a1 = lerp(sh.previousA.xyz, sh.a.xyz, _ShapeTime);
        if (sh.b.w != 0)
        {
            float3 e = abs(sh.ax.xyz) * sh.he.x + abs(sh.ay.xyz) * sh.he.y + abs(sh.az.xyz) * sh.he.z;
            slo = a1 - e; shi = a1 + e;
        }
        else
        {
            float3 b1 = lerp(sh.previousB.xyz, sh.b.xyz, _ShapeTime);
            slo = min(a1, b1) - sh.a.w; shi = max(a1, b1) + sh.a.w;
        }
        if (all(shi >= lo) && all(slo <= hi)) { uint slot; InterlockedAdd(candidateCount, 1, slot); candidates[slot] = s; }
    }
    GroupMemoryBarrierWithGroupSync();
    uint3 c = b * 4 + uint3(local & 3, (local >> 2) & 3, local >> 4);
    uint node = c.x + _GridN * (c.y + _GridN * c.z), raw = node * 4;
    int quanta = asint(_Grid[raw + 3]);
    // A node with a few quanta of mass has a meaningless momentum/mass ratio; treat it as empty.
    if (quanta <= 16) return;
    float mass = quanta / _FixedMass;
    float3 x = c * _Dx;
    for (uint k=0;k<candidateCount;k++)
    {
        Shape shape=_Shapes[candidates[k]];
        if(shape.he.w<1) continue;
        float3 n, velocity; float d=Distance(shape,x,n,velocity);
        if(d>=_Skin) continue;
        uint id=(uint)shape.he.w-1;
        CouplingBody body=_Bodies[id]; float3 r=x-body.centreInvMass.xyz;
        // Trace bounds the largest eigenvalue of inverse inertia. The mass-weighted
        // sum bounds the shared body's contact Delassus operator, including all nodes.
        float bound=body.centreInvMass.w+dot(r,r)*(body.inertia0.x+body.inertia1.y+body.inertia2.z);
        BoundAdd(id,mass*bound);
    }
}

[numthreads(64,1,1)] void BeginCoupling(uint id : SV_DispatchThreadID)
{
    if(id>=_BodyCount) return;
    for(uint k=0;k<16;k++) _BodySnapshot[id*16+k]=_Reaction[id*16+k];
    _ContactBound[id]=0;
}

[numthreads(64,1,1)] void Grid(uint3 group : SV_GroupID, uint local : SV_GroupIndex)
{
    uint bn = _GridN / 4, block = _BlockList[group.x];
    uint3 b = uint3(block % bn, block / bn % bn, block / (bn * bn));
    if (local == 0) candidateCount = 0;
    GroupMemoryBarrierWithGroupSync();
    // Shapes whose bounds (at this substep) reach this 4^3 block.
    float3 lo = b * 4 * _Dx - _Skin, hi = (b * 4 + 3) * _Dx + _Skin;
    bool reachesShapes = all(hi >= _ShapeLo) && all(lo <= _ShapeHi);
    for (uint s = local; reachesShapes && s < _ShapeCount; s += 64)
    {
        Shape sh = _Shapes[s]; float3 slo, shi;
        float3 a1 = lerp(sh.previousA.xyz, sh.a.xyz, _ShapeTime);
        if (sh.b.w != 0)
        {
            float3 e = abs(sh.ax.xyz) * sh.he.x + abs(sh.ay.xyz) * sh.he.y + abs(sh.az.xyz) * sh.he.z;
            slo = a1 - e; shi = a1 + e;
        }
        else
        {
            float3 b1 = lerp(sh.previousB.xyz, sh.b.xyz, _ShapeTime);
            slo = min(a1, b1) - sh.a.w; shi = max(a1, b1) + sh.a.w;
        }
        if (all(shi >= lo) && all(slo <= hi)) { uint slot; InterlockedAdd(candidateCount, 1, slot); candidates[slot] = s; }
    }
    GroupMemoryBarrierWithGroupSync();
    uint3 c = b * 4 + uint3(local & 3, (local >> 2) & 3, local >> 4);
    uint node = c.x + _GridN * (c.y + _GridN * c.z), raw = node * 4;
    int quanta = asint(_Grid[raw + 3]);
    // A node with a few quanta of mass has a meaningless momentum/mass ratio; treat it as empty.
    if (quanta <= 16) { _Grid[raw] = 0; _Grid[raw + 1] = 0; _Grid[raw + 2] = 0; return; }
    float mass = quanta / _FixedMass;
    float3 momentum = float3(asint(_Grid[raw]), asint(_Grid[raw + 1]), asint(_Grid[raw + 2])) / _FixedMomentum;
    float3 x = c * _Dx, v = momentum / mass;
    v *= _Damping;
    // Unilateral contact and Coulomb friction; opposing fingers do not impose a sticky velocity weld.
    for (uint k = 0; k < candidateCount; k++)
    {
        uint id = candidates[k]; float3 n, vs; float d = Distance(_Shapes[id], x, n, vs);
        if (d >= _Skin) continue;
        Shape shape = _Shapes[id];
        vs = BodyVelocity(shape, x, vs);
        float3 vr = v - vs; float vn = dot(vr, n);
        // Velocity-level unilateral contact. The gap recovery spans the common
        // coupling interval, not a tiny CFL substep. Both masses receive the same impulse.
        float push = d < 0 ? -d / _ContactDt : 0;
        float jn = max(0, push - vn) / (ContactScale(shape) / mass);
        float3 tangent = vr - vn*n; float speed = length(tangent);
        float3 impulse = jn*n;
        if (speed > 1e-9)
        {
            float3 direction = tangent/speed;
            float jt = min(_Friction*jn, mass*speed/ContactScale(shape));
            impulse -= jt*direction;
        }
        v += impulse / mass;
        Reaction(id, impulse, x, -n);
    }
    for (uint a = 0; a < _AxleCount; a++)
    {
        Axle ax = _Axles[a]; float3 o = x - ax.pos.xyz, nrm = ax.normal.xyz; float h = dot(o, nrm); float3 radial = o - h * nrm; float r = length(radial);
        if (abs(h) < ax.normal.w && r < ax.pos.w) { float3 t = r > 1e-6 ? cross(nrm, radial / r) : 0; v = t * dot(v, t); }
    }
    for (uint q = 0; q < _CuboidCount; q++)
    {
        Cuboid cb = _Cuboids[q];
        if (_Time >= cb.pos.w && _Time < cb.size.w && all(abs(x - cb.pos.xyz) < cb.size.xyz)) v = cb.velocity.xyz;
    }
    // Floor at the moving part's base: broken pieces land instead of drifting off.
    if (_Floor != 0 && dot(x, _FloorNormal) < _FloorOffset)
    {
        float vn = dot(v, _FloorNormal);
        if (vn < 0) { float3 vt = v - vn * _FloorNormal; float lt = length(vt); v = lt > 1e-9 ? vt * max(0, 1 + .8 * vn / lt) : 0; }
    }
    const uint padding = 3;
    if (c.x < padding && v.x < 0) v.x = 0; if (c.x >= _GridN - padding && v.x > 0) v.x = 0;
    if (c.y < padding && v.y < 0) v.y = 0; if (c.y >= _GridN - padding && v.y > 0) v.y = 0;
    if (c.z < padding && v.z < 0) v.z = 0; if (c.z >= _GridN - padding && v.z > 0) v.z = 0;
    _Grid[raw] = asuint(v.x); _Grid[raw + 1] = asuint(v.y); _Grid[raw + 2] = asuint(v.z);
}

[numthreads(64,1,1)] void Clear(uint3 group : SV_GroupID, uint local : SV_GroupIndex)
{
    uint bn = _GridN / 4, block = _BlockList[group.x];
    uint3 c = uint3(block % bn, block / bn % bn, block / (bn * bn)) * 4 + uint3(local & 3, (local >> 2) & 3, local >> 4);
    uint raw = (c.x + _GridN * (c.y + _GridN * c.z)) * 4;
    _Grid[raw] = 0; _Grid[raw + 1] = 0; _Grid[raw + 2] = 0; _Grid[raw + 3] = 0;
    if (local == 0) _BlockFlag[block] = 0;
}

groupshared uint tileMotion;
[numthreads(64,1,1)] void G2P(uint p : SV_DispatchThreadID, uint lane : SV_GroupIndex)
{
    // Reuse a compact Morton group's grid stencil in threadgroup memory. Large
    // deformations fall back to the original global reads without changing samples.
    if (lane == 0) { minX = minY = minZ = (int)_GridN - 1; maxX = maxY = maxZ = 0; tileMotion = 0; }
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
    if (cacheVolume <= NODE_CACHE)
        for (uint slot = lane; slot < cacheVolume; slot += 64)
        {
            uint3 c = cacheOrigin + uint3(slot % cacheSize.x, slot / cacheSize.x % cacheSize.y, slot / (cacheSize.x * cacheSize.y));
            uint raw = Node(c) * 4;
            uint x = _Grid[raw], y = _Grid[raw+1], z = _Grid[raw+2];
            nodeX[slot] = asint(x); nodeY[slot] = asint(y); nodeZ[slot] = asint(z);
            if (((x | y | z) & 0x7fffffffu) != 0) InterlockedOr(tileMotion, 1);
        }
    GroupMemoryBarrierWithGroupSync();
    if (p >= _ParticleCount) return;
    if (cacheVolume <= NODE_CACHE && tileMotion == 0)
    {
        // Exactly zero grid velocity implies zero APIC velocity and gradient.
        // Retain the constitutive projection already written by P2G, including history.
        Particle rest = _Particles[p];
        rest.x.xyz = clamp(rest.x.xyz, 3 * _Dx, (_GridN - 4) * _Dx);
        rest.v.xyz = 0; rest.c0.xyz = rest.c1.xyz = rest.c2.xyz = 0;
        _Particles[p] = rest; return;
    }
    Particle q = _Particles[p];
    int3 base; float3 fx; float3 w[3], dw[3]; Weights(q.x.xyz, base, fx, w, dw);
    float3 v = 0; float3x3 c = 0, gradV = 0;
    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++)
    {
        float weight = w[i].x * w[j].y * w[k].z;
        float3 grad = float3(dw[i].x * w[j].y * w[k].z, w[i].x * dw[j].y * w[k].z, w[i].x * w[j].y * dw[k].z) * _InvDx;
        int3 coordinate = clamp(base + int3(i, j, k), 0, (int)_GridN - 1);
        float3 gv;
        if (cacheVolume <= NODE_CACHE)
        {
            uint3 c = (uint3)coordinate - cacheOrigin;
            uint slot = c.x + cacheSize.x * (c.y + cacheSize.y * c.z);
            gv = float3(asfloat(nodeX[slot]), asfloat(nodeY[slot]), asfloat(nodeZ[slot]));
        }
        else
        {
            uint n = Node(coordinate) * 4;
            gv = float3(asfloat(_Grid[n]), asfloat(_Grid[n + 1]), asfloat(_Grid[n + 2]));
        }
        v += weight * gv; c += Outer(gv, float3(i, j, k) - fx) * (weight * _InvDx * 4); gradV += Outer(gv, grad);
    }
    float3x3 step = I3 + _Dt * gradV;
    float3x3 f = mul(step, Rows(q.f0, q.f1, q.f2)), r = mul(step, Rows(q.r0, q.r1, q.r2));
    float3 x = q.x.xyz + _Dt * v;
    // Contact is resolved on the grid. A second particle-level projection would
    // double-count impulses and bypass the shared rigidbody effective-mass solve.
    q.x.xyz = clamp(x, 3 * _Dx, (_GridN - 4) * _Dx);
    q.v.xyz = v;
    q.c0.xyz = c[0]; q.c1.xyz = c[1]; q.c2.xyz = c[2];
    q.f0.xyz = f[0]; q.f1.xyz = f[1]; q.f2.xyz = f[2];
    q.r0.xyz = r[0]; q.r1.xyz = r[1]; q.r2.xyz = r[2];
    _Particles[p] = q;
}

// Moving Gaussians ride on their bound particle: x = x_p + F d, Sigma' = F Sigma F^T, in object space.
[numthreads(64,1,1)] void Deform(uint i : SV_DispatchThreadID)
{
    if (i >= _DynamicCount) return;
    Gaussian g = _Source[i];
    uint primary = _SplatParticle[i]; Particle q = _Particles[primary];
    bool flowing = _Material == 2 || _Material == 6;
    float3 x;
    float3x3 f = I3;
    if (flowing)
    {
        // MPM sand and fluid particles advect; their fitted Gaussian covariance
        // is a sampling kernel, not a material line that should become a needle.
        x = q.x.xyz + _SplatOffset[i].xyz;
    }
    else
    {
        uint4 ids = _SplatNeighbors[i]; float4 w = _SplatWeights[i];
        Particle a0 = _Particles[ids.x], a1 = _Particles[ids.y], a2 = _Particles[ids.z], a3 = _Particles[ids.w];
        float3 rest = _ParticleRest[primary].xyz + _SplatOffset[i].xyz;
        x = rest + w.x * (a0.x.xyz - _ParticleRest[ids.x].xyz)
                 + w.y * (a1.x.xyz - _ParticleRest[ids.y].xyz)
                 + w.z * (a2.x.xyz - _ParticleRest[ids.z].xyz)
                 + w.w * (a3.x.xyz - _ParticleRest[ids.w].xyz);
        f = _RenderTotal != 0
            ? w.x * Rows(a0.r0, a0.r1, a0.r2) + w.y * Rows(a1.r0, a1.r1, a1.r2) + w.z * Rows(a2.r0, a2.r1, a2.r2) + w.w * Rows(a3.r0, a3.r1, a3.r2)
            : w.x * Rows(a0.f0, a0.f1, a0.f2) + w.y * Rows(a1.f0, a1.f1, a1.f2) + w.z * Rows(a2.f0, a2.f1, a2.f2) + w.w * Rows(a3.f0, a3.f1, a3.f2);
    }
    float3x3 basis = (float3x3)_Basis, a = mul(basis, mul(f, transpose(basis)));
    g.center.xyz = mul(_LocalFromSim, float4(x, 1)).xyz;
    g.axis0.xyz = mul(a, g.axis0.xyz); g.axis1.xyz = mul(a, g.axis1.xyz); g.axis2.xyz = mul(a, g.axis2.xyz);
    _Deformed[i] = g;
}
