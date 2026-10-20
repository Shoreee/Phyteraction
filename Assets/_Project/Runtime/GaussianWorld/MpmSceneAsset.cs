using System;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Order matches PhysFlow's material ids (mpm_solver_warp).
    public enum MpmMaterial { Elastic = 0, Metal = 1, Sand = 2, Foam = 3, Plasticine = 4, NonNewtonian = 6 }

    // GPU layouts (see Mpm.compute). Times are simulation seconds since reset.
    [Serializable] public struct MpmModifier
    {
        // point.w type (0 impulse, 1 translation, 2 rotation), size.w start, vec.w end,
        // normal.w rotation speed, h1.w translation speed.
        public Vector4 point, size, vec, normal, h1, h2;
    }
    [Serializable] public struct MpmCuboid { public Vector4 point, size, velocity; } // point.w start, size.w end
    [Serializable] public struct MpmAxle { public Vector4 point, normal; }          // point.w radius, normal.w half height
    // A rigid piece of an assembled toy (local metres): rest pose of its oriented box and its neighbours
    // in brickNeighbors[neighborStart, neighborStart + neighborCount).
    [Serializable] public struct BrickPiece
    {
        public Vector3 center, halfExtents;
        public Quaternion rotation;
        public float mass;
        public int neighborStart, neighborCount;
        [Tooltip("Rests on the support: can be put back without an attached neighbour.")] public bool grounded;
    }

    // One PhysFlow scene: the whole reconstruction as splats (moving part first), MPM particles in
    // PhysFlow's normalised simulation space, and the released per-scene physics config.
    [PreferBinarySerialization]
    public sealed class MpmSceneAsset : ScriptableObject
    {
        public string scene, provenance, sourceSha256;
        public Gaussian[] splats;
        public int dynamicCount;
        [Tooltip("Static splats right after the dynamic ones that belong to the object's support.")] public int nearCount;
        [Tooltip("First-order SH per splat, three float4 (r1 r2 r3 g1 | g2 g3 b1 b2 | b3); empty for DC-only colour.")] public Vector4[] sh = Array.Empty<Vector4>();
        [Tooltip("Maps local directions into the capture frame the SH were fitted in.")] public Matrix4x4 shFromLocal = Matrix4x4.identity;
        public Bounds bounds;
        // Per moving splat: bound particle and offset from it (simulation units).
        public int[] binding;
        public Vector3[] offsets;
        public Vector3[] particles;
        public float[] volume;
        // Per particle: impulse gain (PhysFlow dt / PhysFlow particle mass) and sequential-release hold time.
        public float[] impulseGain;
        public float[] holdTime;
        public Matrix4x4 localFromSim;
        public float metersPerUnit, height;
        public int gridN;
        public float dx;
        [Header("Material (SI, PhysFlow config scaled by 1e7 / 1e5 / 1e4)")]
        public MpmMaterial material;
        public float youngs, poisson, density, yieldStress, viscosity, frictionAngle, xi, cohesion;
        public bool hardening;
        [Tooltip("Splats follow the total deformation (plastic flow) instead of the elastic part.")]
        public bool renderTotal;
        [Tooltip("Config gravity for the paper replay, simulation units / s^2.")]
        public Vector3 gravity;
        [Tooltip("Vertical up in simulation space (mpm_space_vertical_upward_axis).")]
        public Vector3 up = Vector3.up;
        [Tooltip("Critical principal log stretch for fracture; 0 disables (not part of PhysFlow).")]
        public float fractureStrain;
        [Tooltip("Yield stress multiplier outside the paper replay (not part of PhysFlow; its released yield values are unrefined).")]
        public float interactionYieldScale = 1;
        [Header("Hand interaction profile (not part of PhysFlow; the paper replay keeps the config)")]
        [Tooltip("Replace the config material under hand interaction, e.g. a plush fox as elastic instead of a foam that yields at 100 Pa.")]
        public bool overrideMaterial;
        public MpmMaterial interactionMaterial;
        [Tooltip("Young's modulus under hand interaction (Pa); 0 keeps the config value.")]
        public float interactionYoungs;
        [Tooltip("Displaced material falls under gravity (granular or pasty scenes); otherwise only detached pieces do.")]
        public bool looseGravity;
        [Tooltip("Grid velocity decay per second (PhysFlow: grid_v_damping_scale per 1e-4 s).")]
        public float dampingRate;
        public MpmCuboid[] cuboids = Array.Empty<MpmCuboid>();
        public MpmModifier[] modifiers = Array.Empty<MpmModifier>();
        public MpmAxle[] axles = Array.Empty<MpmAxle>();
        public float replaySeconds = 2;
        [Header("Rigid bricks under hand interaction (not part of PhysFlow)")]
        public BrickPiece[] bricks = Array.Empty<BrickPiece>();
        public int[] brickNeighbors = Array.Empty<int>();
        [Tooltip("Per moving splat: its brick.")] public int[] splatBrick = Array.Empty<int>();

        public float Mu => youngs / (2 * (1 + poisson));
        public float Lambda => youngs * poisson / ((1 + poisson) * (1 - 2 * poisson));
        // P-wave speed in simulation units per second, for the explicit CFL step.
        public float WaveSpeed => Mathf.Sqrt((Lambda + 2 * Mu) / density);
        public MpmMaterial MaterialFor(bool replay) => !replay && overrideMaterial ? interactionMaterial : material;
        public float YoungsFor(bool replay) => !replay && interactionYoungs > 0 ? interactionYoungs : youngs;
        public float MuFor(bool replay) => YoungsFor(replay) / (2 * (1 + poisson));
        public float LambdaFor(bool replay) => YoungsFor(replay) * poisson / ((1 + poisson) * (1 - 2 * poisson));
        public float WaveSpeedFor(bool replay) => Mathf.Sqrt((LambdaFor(replay) + 2 * MuFor(replay)) / density);
        public bool RenderTotalFor(bool replay) { var m = MaterialFor(replay); return m == MpmMaterial.Metal || m == MpmMaterial.Plasticine || m == MpmMaterial.Foam || m == MpmMaterial.NonNewtonian; }
    }
}
