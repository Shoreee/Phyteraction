float Distance(Shape s, float3 x, out float3 n, out float3 velocity)
{
    if (s.b.w != 0)
    {
        float3 centre = s.a.xyz + s.va.xyz * _ShapeTime, d = x - centre;
        float3 l = float3(dot(d, s.ax.xyz), dot(d, s.ay.xyz), dot(d, s.az.xyz)), c = clamp(l, -s.he.xyz, s.he.xyz);
        velocity = s.va.xyz;
        if (any(l != c)) { float3 delta = d - (s.ax.xyz * c.x + s.ay.xyz * c.y + s.az.xyz * c.z); float dist = length(delta); n = delta / max(dist, 1e-9); return dist; }
        float3 gap = s.he.xyz - abs(l);
        if (gap.x < gap.y && gap.x < gap.z) { n = s.ax.xyz * sign(l.x); return -gap.x; }
        if (gap.y < gap.z) { n = s.ay.xyz * sign(l.y); return -gap.y; }
        n = s.az.xyz * sign(l.z); return -gap.z;
    }
    float3 a = s.a.xyz + s.va.xyz * _ShapeTime, ab = s.b.xyz + s.vb.xyz * _ShapeTime - a;
    float t = saturate(dot(x - a, ab) / max(dot(ab, ab), 1e-12));
    float3 delta = x - (a + t * ab); float dist = length(delta);
    n = dist > 1e-9 ? delta / dist : float3(0, 1, 0);
    velocity = lerp(s.va.xyz, s.vb.xyz, t);
    return dist - s.a.w;
}

void Reaction(uint id, float3 j, float3 x)
{
    float m = length(j); uint r = id * 8;
    ReactionAdd(r, j.x); ReactionAdd(r + 1, j.y); ReactionAdd(r + 2, j.z); ReactionAdd(r + 3, m);
    ReactionAdd(r + 4, m * x.x); ReactionAdd(r + 5, m * x.y); ReactionAdd(r + 6, m * x.z);
}

groupshared uint candidates[96];
groupshared uint candidateCount;

