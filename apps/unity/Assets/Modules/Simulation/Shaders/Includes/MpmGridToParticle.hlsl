[numthreads(64,1,1)] void Grid(uint3 group : SV_GroupID, uint local : SV_GroupIndex)
{
    uint bn = _GridN / 4, block = _BlockList[group.x];
    uint3 b = uint3(block % bn, block / bn % bn, block / (bn * bn));
    if (local == 0) candidateCount = 0;
    GroupMemoryBarrierWithGroupSync();
    // Shapes whose bounds (at this substep) reach this 4^3 block.
    float3 lo = b * 4 * _Dx - _Skin, hi = (b * 4 + 3) * _Dx + _Skin;
    for (uint s = local; s < _ShapeCount; s += 64)
    {
        Shape sh = _Shapes[s]; float3 slo, shi;
        float3 a1 = sh.a.xyz + sh.va.xyz * _ShapeTime;
        if (sh.b.w != 0)
        {
            float3 e = abs(sh.ax.xyz) * sh.he.x + abs(sh.ay.xyz) * sh.he.y + abs(sh.az.xyz) * sh.he.z;
            slo = a1 - e; shi = a1 + e;
        }
        else
        {
            float3 b1 = sh.b.xyz + sh.vb.xyz * _ShapeTime;
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
    // Hand bones: no approach along the contact normal, Coulomb friction, gentle bounded push-out.
    // Material squeezed between opposing bones (a pinch) sticks to them so it can be held.
    float3 n0 = 0, stick = 0; uint touching = 0, first = 0, second = 0; bool pinch = false;
    for (uint k = 0; k < candidateCount; k++)
    {
        uint id = candidates[k]; float3 n, vs; float d = Distance(_Shapes[id], x, n, vs);
        if (d >= _Skin) continue;
        if (touching == 0) { n0 = n; first = id; } else if (!pinch && dot(n, n0) < -.2) { pinch = true; second = id; }
        touching++; stick += vs;
        float3 vr = v - vs; float vn = dot(vr, n);
        float push = d < 0 ? min(-d / _Dt * .2, _MaxPush) : 0;
        if (vn >= push) continue;
        float3 vt = vr - vn * n; float lt = length(vt);
        float3 nv = vs + (lt > 1e-9 ? vt * max(0, 1 + _Friction * (vn - push) / lt) : 0) + n * push;
        Reaction(id, mass * (nv - v), x);
        v = nv;
    }
    if (pinch)
    {
        float3 j = mass * (stick / touching - v) * .5;
        Reaction(first, j, x); Reaction(second, j, x);
        v = stick / touching;
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

[numthreads(64,1,1)] void G2P(uint p : SV_DispatchThreadID)
{
    if (p >= _ParticleCount) return;
    Particle q = _Particles[p];
    int3 base; float3 fx; float3 w[3], dw[3]; Weights(q.x.xyz, base, fx, w, dw);
    float3 v = 0; float3x3 c = 0, gradV = 0;
    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++)
    {
        float weight = w[i].x * w[j].y * w[k].z;
        float3 grad = float3(dw[i].x * w[j].y * w[k].z, w[i].x * dw[j].y * w[k].z, w[i].x * w[j].y * dw[k].z) * _InvDx;
        uint n = Node(base + int3(i, j, k)) * 4;
        float3 gv = float3(asfloat(_Grid[n]), asfloat(_Grid[n + 1]), asfloat(_Grid[n + 2]));
        v += weight * gv; c += Outer(gv, float3(i, j, k) - fx) * (weight * _InvDx * 4); gradV += Outer(gv, grad);
    }
    float speed = length(v); if (speed > _MaxParticleSpeed) v *= _MaxParticleSpeed / speed;
    float3x3 step = I3 + _Dt * gradV;
    float3x3 f = mul(step, Rows(q.f0, q.f1, q.f2)), r = mul(step, Rows(q.r0, q.r1, q.r2));
    float3 x = q.x.xyz + _Dt * v;
    // Grid contact supplies the collision velocity and material stress. At the
    // particle level only remove inward velocity; projecting each particle all
    // the way to a finger surface teleports it through its neighbours and tears
    // even a high-stiffness metal or foam object apart.
    if (_ShapeCount > 0 && all(x >= _ShapeLo) && all(x <= _ShapeHi))
        for (uint s = 0; s < _ShapeCount; s++)
        {
            float3 n, vs; float d = Distance(_Shapes[s], x, n, vs);
            if (d >= 0) continue;
            float vn = dot(v - vs, n);
            if (vn < 0) { v -= vn * n; Reaction(s, -q.v.w * _MassScale * vn * n, x); }
        }
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
