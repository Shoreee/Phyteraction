"""Turn a PhysFlow scene (CVPR 2025 data release) into the runtime binary for Unity.

Reproduces PhysFlow's own preprocessing (simulation.py): opacity > threshold, moving part =
Gaussians within 0.5 / n_grid of moving_part_points.ply, config rotation, normalisation of the
moving part to unit extent centred at (1, 1, 1) in a [0, 2]^3 MPM domain. Everything else in the
reconstruction is kept as static scene. MPM particles are a sub-cell subsample of the moving
Gaussians (<= 8 per cell, volume dx^3 / count as in PhysFlow); every moving Gaussian is bound to
its nearest particle and rides on that particle's deformation gradient.

Usage: prepare_physflow.py <scene> [--budget N] [--dynamic-cap N]
Output: data/physflow/<scene>.bin and <scene>.json
"""
import argparse, sys, hashlib, json, struct
from pathlib import Path
import numpy as np
from plyfile import PlyData
from scipy.spatial import cKDTree

root = Path(__file__).resolve().parents[1] / 'data' / 'physflow'
# Metres per simulation unit (= real size of the moving part's longest side) and where its centre
# sits in the room. Explicit demo assumptions; PhysFlow itself has no metric scale.
# `refine` scales PhysFlow's grid. It stays at the paper's resolution: MPM's effective stiffness of
# thin parts (stems, cords) depends on dx, and the released materials were chosen at that dx.
SCENES = {
    'alocasia':   dict(meters=.45, height=1.05, refine=1),
    'carnation':  dict(meters=.32, height=1.05, refine=1),
    'hat':        dict(meters=.30, height=1.15, refine=1),
    'telephone':  dict(meters=.35, height=1.10, refine=1),
    'fox':        dict(meters=.25, height=1.00, refine=1),
    'jam':        dict(meters=.08, height=.95, refine=1),
    'kitchen':    dict(meters=.30, height=.98, refine=1),
    'plane_960':  dict(meters=.25, height=1.25, refine=1),  # tabletop model: propeller disc ~0.3 m
    'sandcastle': dict(meters=.30, height=.98, refine=.5),  # paper grid 200^3 is too costly for real time
}
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

def find_scene(name):
    for folder in (root / name, root / 'phys_dreamer' / name, root / 'realworld' / name):
        if (folder / 'point_cloud').exists(): return folder
    raise FileNotFoundError(f'{name}: extract {name}.zip (without images) into {root}')

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('scene'); ap.add_argument('--budget', type=int, default=3 << 17)
    ap.add_argument('--dynamic-cap', type=int, default=3 << 16)
    ap.add_argument('--refine', type=float, default=0, help='grid refinement over PhysFlow n_grid (default per scene)')
    ap.add_argument('--per-axis', type=int, default=0, help='particles per cell per axis (default 2 on refined grids, else 3)'); a = ap.parse_args()
    folder = find_scene(a.scene); config = json.loads((root / 'configs' / f'{a.scene}_config.json').read_text())
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
    refine = a.refine or SCENES[a.scene]['refine']; cells = int(round(n_grid * refine)); dx = grid_lim / cells
    per_axis = a.per_axis or (3 if refine == 1 else 2)
    keep = (opacity > config.get('opacity_threshold', .02)) & np.isfinite(xyz).all(1) & np.isfinite(scale).all(1) & np.isfinite(rotation).all((1, 2))
    moving_points = PlyData.read(str(folder / 'moving_part_points.ply'))['vertex']
    moving_points = np.stack([moving_points['x'], moving_points['y'], moving_points['z']], 1).astype(np.float64)
    distance = np.full(len(xyz), np.inf); finite = np.isfinite(xyz).all(1)
    distance[finite] = cKDTree(moving_points).query(xyz[finite], workers=-1)[0]
    moving = keep & (distance <= .5 / n_grid) & (np.arange(len(xyz)) < n_main)
    static = keep & ~moving

    rot = np.eye(3)
    for degrees, axis in zip(config.get('rotation_degree', []), config.get('rotation_axis', [])): rot = rotation_about(axis, degrees) @ rot
    rotated = np.einsum('ij,nj->ni', rot, xyz)
    lo, hi = rotated[moving].min(0), rotated[moving].max(0)
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
    meters = SCENES[a.scene]['meters']; centre = (sim[moving].min(0) + sim[moving].max(0)) / 2
    local_from_sim = np.eye(4); local_from_sim[:3, :3] = meters * basis; local_from_sim[:3, 3] = -meters * basis @ centre

    # The viewer stands ~0.6 m in front of the moving part (local -z). Static splats between them,
    # far away (> 3 m) or huge (3 sigma > 0.5 m) are floaters from that novel viewpoint and dropped.
    local = np.einsum('ij,nj->ni', local_from_sim[:3, :3], sim) + local_from_sim[:3, 3]
    lo_m, hi_m = local[moving].min(0), local[moving].max(0)
    size_m = 3 * scale.max(1) * sim_scale * meters
    in_front = (local[:, 2] < lo_m[2] - .08) & (local[:, 1] > lo_m[1] - .02)
    static &= ~in_front & (np.linalg.norm(local, axis=1) < 3) & (size_m < .5)
    # Wide splats centred near the moving part are background haze, not part of it.
    # Large needles caught by the moving mask (table or plate texture at its rim) streak from new views.
    sorted_all = np.sort(scale, 1); needle_all = (sorted_all[:, 2] > 6 * sorted_all[:, 1]) & (size_m > .015)
    haze = moving & ((size_m > .06) | needle_all); moving &= ~haze; static |= haze & (size_m < .5)
    print(f'moving splats demoted to background: {int(haze.sum())}', file=sys.stderr)

    # Render budget: all moving Gaussians up to the cap, then the most visible static ones.
    weight = opacity * np.sort(scale, 1)[:, 1:].prod(1)
    moving_ids = np.flatnonzero(moving); static_ids = np.flatnonzero(static)
    if len(moving_ids) > a.dynamic_cap: moving_ids = moving_ids[np.argsort(-weight[moving_ids])[:a.dynamic_cap]]
    static_ids = static_ids[np.argsort(-weight[static_ids])[:max(0, a.budget - len(moving_ids))]]
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

    # Particles: one moving Gaussian per sub-cell (<= per_axis^3 per MPM cell), PhysFlow's volume rule.
    all_moving = np.flatnonzero(moving); p_sim = sim[all_moving]
    sub = np.floor(p_sim / (dx / per_axis)).astype(np.int64)
    _, first = np.unique(sub[:, 0] * 1_000_003 ** 2 + sub[:, 1] * 1_000_003 + sub[:, 2], return_index=True)
    particles = p_sim[np.sort(first)]
    def cell_counts(points, query, h):
        c = np.floor(points / h).astype(np.int64); keys = c[:, 0] * 1_000_003 ** 2 + c[:, 1] * 1_000_003 + c[:, 2]
        u, counts = np.unique(keys, return_counts=True)
        q = np.floor(query / h).astype(np.int64); qk = q[:, 0] * 1_000_003 ** 2 + q[:, 1] * 1_000_003 + q[:, 2]
        return counts[np.searchsorted(u, qk)]
    # `paper` = PhysFlow particles per PhysFlow cell; scripted impulses act on those particle masses.
    own = cell_counts(particles, particles, dx); paper = cell_counts(p_sim, particles, paper_dx)
    volume = dx ** 3 / own
    if config['material'] == 'sand': volume[:] = volume.mean()
    _, binding = cKDTree(particles).query(sim[moving_ids], workers=-1)
    offsets = sim[moving_ids] - particles[binding]

    out = root / f'{a.scene}.bin'
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
        'meters_per_sim_unit': meters, 'height': SCENES[a.scene]['height'], 'moving_extent_m': extent.round(4).tolist(),
        'local_from_sim': local_from_sim.round(8).flatten().tolist(), 'per_axis': per_axis, 'up_sim': up.tolist(), 'forward_sim': forward.round(6).tolist(),
        'sh_degree': 1, 'sh_from_local': np.linalg.inv(linear).round(8).flatten().tolist(),
        'output_sha256': sha256(out),
        'assumptions': 'metric scale and placement are demo choices; material values are the released PhysFlow configs (GPT-4 initialisation), not refined values',
    }
    out.with_suffix('.json').write_text(json.dumps(report, indent=2))
    print(json.dumps({k: report[k] for k in ('scene', 'splats_near', 'gaussians_source', 'gaussians_moving', 'splats', 'splats_dynamic', 'particles', 'max_particles_per_cell', 'paper_particles_per_cell_median', 'moving_extent_m')}))

if __name__ == '__main__':
    main()
