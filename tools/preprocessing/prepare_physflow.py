"""Turn a PhysFlow scene (CVPR 2025 data release) into the runtime binary for Unity.

Reproduces PhysFlow's own preprocessing (simulation.py): opacity > threshold, moving part =
Gaussians within 0.5 / n_grid of moving_part_points.ply, config rotation, normalisation of the
moving part to unit extent centred at (1, 1, 1) in a [0, 2]^3 MPM domain. Everything else in the
reconstruction is kept as static scene. PhysFlow initializes one particle per moving Gaussian
(plus optional interior fill, disabled in the released configs), with volume dx^3 / cell count.
Each released moving Gaussian is a material particle; coverage-only extras are bound to that material without removing render Gaussians.

Usage: prepare_physflow.py <scene> [--refine N]
Output: data/physflow/<scene>.bin and <scene>.json
"""
import argparse, sys, hashlib, json, struct
from pathlib import Path
import numpy as np
from plyfile import PlyData
from scipy.spatial import cKDTree

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import RAW_DATA, PROCESSED_DATA

# Metres per simulation unit (= real size of the moving part's longest side) and where its centre
# sits in the room. Explicit demo assumptions; PhysFlow itself has no metric scale.
# `refine` scales PhysFlow's grid. It stays at the paper's resolution: MPM's effective stiffness of
# thin parts (stems, cords) depends on dx, and the released materials were chosen at that dx.

C0 = 0.28209479177387814

def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 22), b''): h.update(block)
    return h.hexdigest()

def rotation_about(axis, degrees):
    c, s = np.cos(np.radians(degrees)), np.sin(np.radians(degrees))
    return {0: np.array([[1, 0, 0], [0, c, -s], [0, s, c]]), 1: np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]),
            2: np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])}[axis]

def quaternion_matrix(q):
    q = q / np.linalg.norm(q, axis=1, keepdims=True); w, x, y, z = q.T
    return np.stack([np.stack([1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)], -1),
                     np.stack([2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)], -1),
                     np.stack([2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)], -1)], 1)

def load_gaussians(path):
    v = PlyData.read(str(path))['vertex']
    xyz = np.stack([v['x'], v['y'], v['z']], 1).astype(np.float64)
    opacity = 1 / (1 + np.exp(-np.asarray(v['opacity'], np.float64)))
    scale = np.exp(np.stack([v['scale_0'], v['scale_1'], v['scale_2']], 1).astype(np.float64))
    rotation = quaternion_matrix(np.stack([v['rot_0'], v['rot_1'], v['rot_2'], v['rot_3']], 1).astype(np.float64))
    color = np.clip(.5 + C0 * np.stack([v['f_dc_0'], v['f_dc_1'], v['f_dc_2']], 1), 0, 1)
    # First-order SH (view-dependent colour) per channel; 3DGS stores f_rest channel-major.
    rest = [p.name for p in v.properties if p.name.startswith('f_rest_')]; per = len(rest) // 3
    sh = np.zeros((len(xyz), 3, 3))
    if per >= 3:
        for c in range(3):
            for k in range(3): sh[:, c, k] = v[f'f_rest_{c * per + k}']
    return xyz, opacity, scale, rotation, color, sh

def find_scene(name, root):
    for folder in (root / name, root / 'phys_dreamer' / name, root / 'realworld' / name):
        if (folder / 'point_cloud').exists(): return folder
    raise FileNotFoundError(f'{name}: extract {name}.zip (without images) into {root}')

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('scene')
    ap.add_argument('--scene-settings', type=Path, default=Path(__file__).with_name('scene_scales.json'))
    ap.add_argument('--input-root', type=Path, default=RAW_DATA / 'physflow')
    ap.add_argument('--output-root', type=Path, default=PROCESSED_DATA / 'physflow')
    ap.add_argument('--refine', type=float, default=0, help='grid refinement over PhysFlow n_grid (default per scene)'); a = ap.parse_args()
    root = a.input_root.resolve(); output = a.output_root.resolve(); output.mkdir(parents=True, exist_ok=True)
    scenes = json.loads(Path(a.scene_settings).read_text())
    folder = find_scene(a.scene, root); config = json.loads((root / 'configs' / f'{a.scene}_config.json').read_text())
    iterations = sorted((folder / 'point_cloud').glob('iteration_*'), key=lambda p: int(p.name.split('_')[1]))
    ply = iterations[-1] / 'point_cloud.ply'
    xyz, opacity, scale, rotation, color, sh = load_gaussians(ply)
    inpaint = folder / 'inpaint_points.ply'
    if inpaint.exists():
        extra = load_gaussians(inpaint); n_main = len(xyz)
        xyz, opacity, scale, rotation, color, sh = [np.concatenate([u, w]) for u, w in zip((xyz, opacity, scale, rotation, color, sh), extra)]
    else: n_main = len(xyz)

    n_grid = config.get('n_grid', 50); grid_lim = config.get('grid_lim', 2.0); paper_dx = grid_lim / n_grid
    # Finer than PhysFlow's grid so finger-sized contacts resolve; continuum parameters are unchanged.
    refine = a.refine or scenes[a.scene]['refine']; cells = int(round(n_grid * refine)); dx = grid_lim / cells
    keep = (opacity > config.get('opacity_threshold', .02)) & np.isfinite(xyz).all(1) & np.isfinite(scale).all(1) & np.isfinite(rotation).all((1, 2))
    moving_points = PlyData.read(str(folder / 'moving_part_points.ply'))['vertex']
    moving_points = np.stack([moving_points['x'], moving_points['y'], moving_points['z']], 1).astype(np.float64)
    distance = np.full(len(xyz), np.inf); nearest = np.zeros(len(xyz), np.int64); finite = np.isfinite(xyz).all(1)
    distance[finite], nearest[finite] = cKDTree(moving_points).query(xyz[finite], workers=-1)
    # A fitted Gaussian belongs to the moving part when its 3-sigma covariance support reaches
    # the released segmentation point cloud; centre-only tests strand large surface splats.
    direction = xyz - moving_points[nearest]
    direction /= np.maximum(distance[:, None], 1e-12)
    local_direction = np.einsum('nji,nj->ni', rotation, direction)
    support = 3 * np.sqrt(np.sum(local_direction * local_direction * scale * scale, axis=1))
    moving = keep & (distance <= .5 / n_grid + support) & (np.arange(len(xyz)) < n_main)
    static = keep & ~moving

    rot = np.eye(3)
    for degrees, axis in zip(config.get('rotation_degree', []), config.get('rotation_axis', [])): rot = rotation_about(axis, degrees) @ rot
    rotated = np.einsum('ij,nj->ni', rot, xyz)
    # Boundary cuboids were authored in the release's centre-mask frame. Enlarging
    # render coverage must never rebase that frame (it moves the fixed wall regions).
    released_moving = keep & (distance <= .5 / n_grid) & (np.arange(len(xyz)) < n_main)
    lo, hi = rotated[released_moving].min(0), rotated[released_moving].max(0)
    sim_scale = 1 / np.max(hi - lo); sim_mean = (lo + hi) / 2
    sim = (rotated - sim_mean) * sim_scale + 1

    # Room frame: sim up axis -> +y, the paper's default camera looks along +z. The sim frame is
    # right-handed and Unity left-handed, so this basis is a reflection (det = -1) by design.
    up = np.array(config['mpm_space_vertical_upward_axis'], np.float64); up /= np.linalg.norm(up)
    cameras = json.loads((folder / 'cameras.json').read_text()); camera = cameras[min(config.get('default_camera_index', 0), len(cameras) - 1)]
    forward = rot @ np.array(camera['rotation'])[:, 2]; forward -= up * forward.dot(up)
    if np.linalg.norm(forward) < 1e-3: forward = np.cross(up, [1, 0, 0])
    forward /= np.linalg.norm(forward); right = np.cross(forward, up)
    basis = np.stack([right, up, forward]); assert np.linalg.det(basis) < 0
    meters = scenes[a.scene]['meters']; centre = (sim[moving].min(0) + sim[moving].max(0)) / 2
    local_from_sim = np.eye(4); local_from_sim[:3, :3] = meters * basis; local_from_sim[:3, 3] = -meters * basis @ centre

    # Preserve every source splat passing the PhysFlow opacity and moving-part masks.
    # View-dependent cleanup belongs in the renderer, never in the imported source data.
    local = np.einsum('ij,nj->ni', local_from_sim[:3, :3], sim) + local_from_sim[:3, 3]
    lo_m, hi_m = local[moving].min(0), local[moving].max(0)
    size_m = 3 * scale.max(1) * sim_scale * meters

    # Keep the full moving and static source sets.
    moving_ids = np.flatnonzero(moving); static_ids = np.flatnonzero(static)
    # Compact static splats within 8 cm of the moving part (pot, base, support) first: the compact
    # mixed-reality view draws only the moving part and these.
    gap = np.linalg.norm(np.maximum(np.maximum(lo_m - local[static_ids], local[static_ids] - hi_m), 0), axis=1)
    # Supports sit low; splats beside or behind the upper part are background that streaks from new views.
    low = (local[static_ids, 1] < lo_m[1] + .35 * (hi_m[1] - lo_m[1])) & (local[static_ids, 2] < hi_m[2] + .02)
    # Needles (one axis > 6x the middle one) and faint splats were fitted for the capture views;
    # from the tabletop viewpoint they read as white spikes around the object.
    sorted_scale = np.sort(scale[static_ids], 1); needle = sorted_scale[:, 2] > 6 * sorted_scale[:, 1]
    near = (gap < .08) & low & (size_m[static_ids] < .05) & ~needle & (opacity[static_ids] > .2)
    # The compact view (dynamic + near) stays within one 2^18 sort.
    near &= np.cumsum(near) <= (1 << 18) - len(moving_ids)
    static_ids = np.concatenate([static_ids[near], static_ids[~near]])
    ids = np.concatenate([moving_ids, static_ids]); n_dynamic = len(moving_ids); n_near = int(near.sum())

    linear = local_from_sim[:3, :3] @ rot * sim_scale
    centres = np.einsum('ij,nj->ni', local_from_sim[:3, :3], sim[ids]) + local_from_sim[:3, 3]
    axes = np.einsum('ij,njk->nik', linear, rotation[ids] * scale[ids][:, None, :])  # columns are scaled axes
    splats = np.zeros((len(ids), 20), np.float32)
    splats[:, 0:3] = centres; splats[:, 3] = 1
    for k in range(3): splats[:, 4 + 4 * k:7 + 4 * k] = axes[:, :, k]
    splats[:, 16:19] = color[ids]; splats[:, 19] = opacity[ids]

    # PhysFlow initializes one particle at every moving Gaussian and assigns volume dx^3 / cell count.
    all_moving = np.flatnonzero(released_moving); p_sim = sim[all_moving]
    # Coverage-only extra splats follow the closest released material particle.
    # Do not turn background Gaussians with long covariance tails into material
    # outside the paper's [0,2]^3 simulation domain. All render splats are retained.
    _, render_particle = cKDTree(p_sim).query(sim[moving_ids], workers=-1)
    def cell_counts(points, query, h):
        c = np.floor(points / h).astype(np.int64); keys = c[:, 0] * 1_000_003 ** 2 + c[:, 1] * 1_000_003 + c[:, 2]
        u, counts = np.unique(keys, return_counts=True)
        q = np.floor(query / h).astype(np.int64); qk = q[:, 0] * 1_000_003 ** 2 + q[:, 1] * 1_000_003 + q[:, 2]
        return counts[np.searchsorted(u, qk)]
    # `paper` = PhysFlow particles per PhysFlow cell; scripted impulses act on those particle masses.
    # Morton order keeps adjacent GPU lanes in nearby cells for shared P2G transfer.
    # This is a permutation, preserving every particle and every rendered Gaussian.
    cell = np.clip(np.floor(p_sim / dx).astype(np.int64), 0, cells - 1)
    morton = np.zeros(len(cell), np.uint64)
    for bit in range(10):
        for axis in range(3): morton |= ((cell[:, axis].astype(np.uint64) >> bit) & 1) << (3 * bit + axis)
    particle_order = np.argsort(morton, kind='stable')
    inverse_order = np.empty(len(particle_order), np.uint32)
    inverse_order[particle_order] = np.arange(len(particle_order), dtype=np.uint32)
    particles = p_sim[particle_order]
    own = cell_counts(particles, particles, dx); paper = cell_counts(p_sim, particles, paper_dx)
    volume = dx ** 3 / own
    if config['material'] == 'sand': volume[:] = volume.mean()
    binding = inverse_order[render_particle]
    offsets = sim[moving_ids] - particles[binding]

    out = output / f'{a.scene}.bin'
    with open(out, 'wb') as f:
        f.write(struct.pack('<iii', len(ids), n_dynamic, len(particles)))
        f.write(splats.astype('<f4').tobytes()); f.write(binding.astype('<u4').tobytes()); f.write(offsets.astype('<f4').tobytes())
        f.write(particles.astype('<f4').tobytes()); f.write(volume.astype('<f4').tobytes()); f.write(paper.astype('<f4').tobytes())
        packed = np.zeros((len(ids), 12), np.float32); packed[:, :9] = sh[ids].reshape(-1, 9)  # r1 r2 r3 g1 g2 g3 b1 b2 b3
        f.write(packed.astype('<f4').tobytes())
    extent = (sim[moving].max(0) - sim[moving].min(0)) * meters
    report = {
        'scene': a.scene, 'source_ply': str(ply.relative_to(root)), 'source_sha256': sha256(ply), 'config': f'configs/{a.scene}_config.json',
        'gaussians_source': int(len(xyz)), 'gaussians_opaque': int(keep.sum()), 'gaussians_moving': int(moving.sum()), 'gaussians_static': int(static.sum()),
        'splats': int(len(ids)), 'splats_dynamic': int(n_dynamic), 'splats_near': n_near, 'particles': int(len(particles)), 'max_particles_per_cell': int(own.max()),
        'paper_particles_per_cell_median': float(np.median(paper)), 'n_grid': n_grid, 'refine': refine, 'cells': cells, 'grid_lim': grid_lim,
        'meters_per_sim_unit': meters, 'height': scenes[a.scene]['height'], 'moving_extent_m': extent.round(4).tolist(),
        'local_from_sim': local_from_sim.round(8).flatten().tolist(), 'up_sim': up.tolist(), 'forward_sim': forward.round(6).tolist(),
        'sh_degree': 1, 'sh_from_local': np.linalg.inv(linear).round(8).flatten().tolist(),
        'output_sha256': sha256(out), 'normalization': 'released centre-mask bounds', 'particle_order': 'Morton permutation, no decimation',
        'assumptions': 'metric scale and placement are demo choices; material values are the released PhysFlow configs (GPT-4 initialisation), not refined values',
    }
    out.with_suffix('.json').write_text(json.dumps(report, indent=2))
    print(json.dumps({k: report[k] for k in ('scene', 'splats_near', 'gaussians_source', 'gaussians_moving', 'splats', 'splats_dynamic', 'particles', 'max_particles_per_cell', 'paper_particles_per_cell_median', 'moving_extent_m')}))

if __name__ == '__main__':
    main()
