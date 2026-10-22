float Distance(Shape s, float3 x, out float3 n, out float3 velocity)
{
    if (s.b.w != 0)
    {
        float3 centre = lerp(s.previousA.xyz, s.a.xyz, _ShapeTime), d = x - centre;
        float3 l = float3(dot(d, s.ax.xyz), dot(d, s.ay.xyz), dot(d, s.az.xyz)), c = clamp(l, -s.he.xyz, s.he.xyz);
        velocity = s.va.xyz;
        if (any(l != c)) { float3 delta = d - (s.ax.xyz * c.x + s.ay.xyz * c.y + s.az.xyz * c.z); float dist = length(delta); n = delta / max(dist, 1e-9); return dist; }
        float3 gap = s.he.xyz - abs(l);
        if (gap.x < gap.y && gap.x < gap.z) { n = s.ax.xyz * (l.x < 0 ? -1 : 1); return -gap.x; }
        if (gap.y < gap.z) { n = s.ay.xyz * (l.y < 0 ? -1 : 1); return -gap.y; }
        n = s.az.xyz * (l.z < 0 ? -1 : 1); return -gap.z;
    }
    float3 a = lerp(s.previousA.xyz, s.a.xyz, _ShapeTime), ab = lerp(s.previousB.xyz, s.b.xyz, _ShapeTime) - a;
    float t = saturate(dot(x - a, ab) / max(dot(ab, ab), 1e-12));
    float3 delta = x - (a + t * ab); float dist = length(delta);
    n = dist > 1e-9 ? delta / dist : float3(0, 1, 0);
    velocity = lerp(s.va.xyz, s.vb.xyz, t);
    return dist - s.a.w;
}

float3 BodyVelocity(Shape shape, float3 position, float3 fallback)
{
    if (shape.he.w < 1) return fallback;
    uint id = (uint)shape.he.w - 1, r = id * 16;
    CouplingBody body = _Bodies[id];
    float3 impulse = float3(asfloat(_BodySnapshot[r]), asfloat(_BodySnapshot[r+1]), asfloat(_BodySnapshot[r+2]));
    float3 torque = float3(asfloat(_BodySnapshot[r+4]), asfloat(_BodySnapshot[r+5]), asfloat(_BodySnapshot[r+6]));
    float3 omega = body.omega.xyz - mul(Rows(body.inertia0, body.inertia1, body.inertia2), torque);
    return body.velocity.xyz - body.centreInvMass.w * impulse + cross(omega, position - body.centreInvMass.xyz);
}
float ContactScale(Shape shape)
{
    if (shape.he.w < 1) return 1;
    return 1 + asfloat(_ContactBound[(uint)shape.he.w-1]);
}
void BoundAdd(uint address, float value)
{
    uint expected = _ContactBound[address], original;
    [allow_uav_condition] for (uint i=0;i<4096;i++)
    {
        InterlockedCompareExchange(_ContactBound[address],expected,asuint(asfloat(expected)+value),original);
        if(original==expected) break;
        expected=original;
    }
}
void Reaction(uint shapeId, float3 j, float3 x, float3 outwardNormal)
{
    Shape shape = _Shapes[shapeId];
    if (shape.he.w < 1) return;
    uint id = (uint)shape.he.w-1, r = id * 16;
    float3 torque = cross(x - _Bodies[id].centreInvMass.xyz, j);
    float m = length(j);
    if (m == 0) return;
    ReactionAdd(r, j.x); ReactionAdd(r+1, j.y); ReactionAdd(r+2, j.z); ReactionAdd(r+3, m);
    ReactionAdd(r+4, torque.x); ReactionAdd(r+5, torque.y); ReactionAdd(r+6, torque.z);
    ReactionAdd(r+8, m*outwardNormal.x); ReactionAdd(r+9, m*outwardNormal.y); ReactionAdd(r+10, m*outwardNormal.z);
    ReactionAdd(r+12, m*x.x); ReactionAdd(r+13, m*x.y); ReactionAdd(r+14, m*x.z);
}

groupshared uint candidates[96];
groupshared uint candidateCount;

