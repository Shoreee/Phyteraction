// f0.w release time; F (elastic) and R (render deformation) stored by rows.
struct Shape { float4 a, b, va, vb, ax, ay, az, he; }; // a.w radius, b.w isBox
struct Modifier { float4 pos, size, vec, normal, h1, h2; }; // pos.w type, size.w start, vec.w end, normal.w rotation, h1.w translation
struct Cuboid { float4 pos, size, velocity; };            // pos.w start, size.w end
struct Axle { float4 pos, normal; };                      // pos.w radius, normal.w half height
#include "../../../GaussianData/ShaderLibrary/Gaussian.hlsl"

RWStructuredBuffer<Particle> _Particles;
RWStructuredBuffer<uint> _Grid;         // 4 floats (as bits) per node: momentum, mass; velocity after Grid
RWStructuredBuffer<uint> _BlockFlag, _BlockList, _Counter, _Args;
RWStructuredBuffer<uint> _Reaction;     // 8 per shape: impulse xyz, magnitude, moment xyz
StructuredBuffer<Shape> _Shapes;
StructuredBuffer<Modifier> _Modifiers;
StructuredBuffer<Cuboid> _Cuboids;
StructuredBuffer<Axle> _Axles;
StructuredBuffer<Gaussian> _Source;
RWStructuredBuffer<Gaussian> _Deformed;
StructuredBuffer<uint> _SplatParticle;
StructuredBuffer<float4> _SplatOffset;
StructuredBuffer<uint4> _SplatNeighbors;
StructuredBuffer<float4> _SplatWeights;
StructuredBuffer<float4> _ParticleRest;

uint _ParticleCount, _GridN, _Blocks, _ShapeCount, _ModifierCount, _CuboidCount, _AxleCount, _Material, _Script, _Hardening, _DynamicCount, _RenderTotal, _ReferenceConstitutive;
float _Dx, _InvDx, _Dt, _Time, _ShapeTime, _Mu, _Lambda, _Viscosity, _FrictionAlpha, _Cohesion, _Xi, _Damping, _Friction, _Skin, _MaxPush, _MaxParticleSpeed, _FloorOffset;
uint _Floor, _GravityMode; // gravity: 0 none, 1 all particles, 2 detached pieces only, 3 detached or hand-touched material
// Mass scaling (explicit FEM practice): inertia x _MassScale raises the stable step; static stiffness is unchanged.
float _MassScale;
float3 _FloorNormal;
float3 _ShapeLo, _ShapeHi; // bounds of all shapes over the step, simulation units
float3 _Gravity;
float4x4 _LocalFromSim;
float4x4 _Basis;

static const float3x3 I3 = float3x3(1,0,0, 0,1,0, 0,0,1);
float3x3 Rows(float4 a, float4 b, float4 c) { return float3x3(a.xyz, b.xyz, c.xyz); }
float3x3 Outer(float3 a, float3 b) { return float3x3(a.x * b, a.y * b, a.z * b); }
float3x3 Columns(float3 a, float3 b, float3 c) { return transpose(float3x3(a, b, c)); }
float3x3 Diag(float3 d) { return float3x3(d.x,0,0, 0,d.y,0, 0,0,d.z); }

// P2G accumulates in fixed point with native integer atomics (float adds would need CAS loops
// that retry under contention). Quanta: 2^23 per heaviest particle mass (and per that mass at a
// reference speed for momentum), leaving ~250 particle masses of headroom per node.
float _FixedMass, _FixedMomentum;
void FixedAdd(uint address, float value, float scale)
{
    InterlockedAdd(_Grid[address], asuint((int)round(clamp(value * scale, -2.0e9, 2.0e9))));
}
void ReactionAdd(uint address, float value)
{
    uint expected = _Reaction[address], original;
    [allow_uav_condition] for (uint i = 0; i < 4096; i++)
    {
        InterlockedCompareExchange(_Reaction[address], expected, asuint(asfloat(expected) + value), original);
        if (original == expected) break;
        expected = original;
    }
}

