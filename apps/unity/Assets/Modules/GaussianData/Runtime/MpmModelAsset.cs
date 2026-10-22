using System;
using UnityEngine;

namespace PhysicsHands.GaussianData
{
    // Order matches PhysFlow's material ids (mpm_solver_warp).
    public enum MpmMaterial
    {
        Elastic = 0,
        Metal = 1,
        Sand = 2,
        Foam = 3,
        Plasticine = 4,
        NonNewtonian = 6
    }

    // GPU layouts (see Mpm.compute). Times are simulation seconds since reset.
    [Serializable]
    public struct MpmModifier
    {
        // point.w type (0 impulse, 1 translation, 2 rotation), size.w start, vec.w end,
        // normal.w rotation speed, h1.w translation speed.
        public Vector4 point, size, vec, normal, h1, h2;
    }

    [Serializable]
    public struct MpmCuboid
    {
        public Vector4 point, size, velocity;
    } // point.w start, size.w end

    [Serializable]
    public struct MpmAxle
    {
        public Vector4 point, normal;
    } // point.w radius, normal.w half height

    // One PhysFlow scene: the whole reconstruction as splats (moving part first), MPM particles in
    // PhysFlow's normalised simulation space, and the released per-scene physics config.
    [PreferBinarySerialization]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "MpmSceneAsset")]
    public sealed class MpmModelAsset : ScriptableObject
    {
        public string scene, provenance, sourceSha256;
        [Tooltip("Empty on earlier assets means author initialization, not trained output.")]
        public string materialOrigin = "physflow-author-initialization";
        public string trainingCommit, materialCheckpointSha256;
        public GaussianRenderAsset renderData;
        public int dynamicCount;
        [Tooltip("Static splats right after the dynamic ones that belong to the object's support.")]
        public int nearCount;
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
        [Tooltip("PhysFlow config substep_dt; all currently released scenes use 1e-4 s.")]
        public float substepDt = 1e-4f;
        [Header("Validated interactive integration (source data retained)")]
        public bool allowInteractionQuadrature;
        [Range(2, 8)]
        public int integrationSubdivisions = 2;
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
        [Tooltip("Grid velocity decay per second (PhysFlow: grid_v_damping_scale per 1e-4 s).")]
        public float dampingRate;
        public MpmCuboid[] cuboids = Array.Empty<MpmCuboid>();
        public MpmModifier[] modifiers = Array.Empty<MpmModifier>();
        public MpmAxle[] axles = Array.Empty<MpmAxle>();
        public float replaySeconds = 2;
        public float Mu => youngs / (2 * (1 + poisson));
        public float Lambda => youngs * poisson / ((1 + poisson) * (1 - 2 * poisson));
        // P-wave speed in simulation units per second, for the explicit CFL step.
        public float WaveSpeed => Mathf.Sqrt((Lambda + 2 * Mu) / density);
        public bool UsesTotalDeformation => renderTotal || material == MpmMaterial.NonNewtonian;
    }
}
