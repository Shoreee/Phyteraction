"""Bake PhysDreamer's optimized Young's modulus field and boundary mask onto the prepared flower.
Reproduces projects/inference/demo.py (setup_simulation, get_material_params) and
physdreamer/fields/triplane_field.py with numpy. Tensors are read as raw float32 storages;
the pickle is only disassembled for verification, never unpickled.
Output: data/carnation-baked.ply = prepared PLY + `youngs_sim` (PhysDreamer sim units) + `frozen`.
"""
from pathlib import Path
import hashlib, io, json, pickletools, subprocess, zipfile
import numpy as np
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation
from plyfile import PlyData, PlyElement

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import RAW_DATA, PROCESSED_DATA

import argparse
MODELS_URL = 'https://hf-mirror.com/datasets/YunjinZhang/PhysDreamer/resolve/main/models.zip'
MEMBER = 'models/physdreamer/carnations/model/sim_fields.pt'
INIT_YOUNG = 2140628.25  # projects/inference/configs/carnation.py simulate_cfg["init_young"]
GRID_SIZE = 64           # carnation.py simulate_cfg["grid_size"]; freeze threshold is 0.5 / grid_size

def fetch_models(root):
    path = root / 'physdreamer-models.zip'
    if not path.exists():
        subprocess.run(['curl', '--fail', '--location', '--silent', '--show-error', '-o', str(path), MODELS_URL], check=True)
    return path

def read_fields(path):
    with zipfile.ZipFile(path) as models, zipfile.ZipFile(io.BytesIO(models.read(MEMBER))) as pt:
        prefix = pt.namelist()[0].split('/')[0]
        listing = io.StringIO(); pickletools.dis(pt.read(prefix + '/data.pkl'), out=listing)
        order = [line.split("'")[1] for line in listing.getvalue().splitlines() if "BINUNICODE '" in line]
        keys = ['aabb', 'kplanes_encoding.plane_coefs.0', 'kplanes_encoding.plane_coefs.1', 'kplanes_encoding.plane_coefs.2',
                'decoder.layers.0.weight', 'decoder.layers.0.bias', 'decoder.layers.1.weight', 'decoder.layers.1.bias']
        found = [k for k in order if k in keys]
        if found != keys: raise ValueError(f'Unexpected sim_fields layout: {found}')
        if pt.read(prefix + '/byteorder').decode() != 'little': raise ValueError('Expected little-endian storages')
        raw = [np.frombuffer(pt.read(f'{prefix}/data/{i}'), dtype='<f4') for i in range(8)]
    shapes = [(2, 3), (32, 8, 8), (32, 8, 8), (32, 8, 8), (32, 32), (32,), (1, 32), (1,)]
    t = [r.reshape(s) for r, s in zip(raw, shapes)]
    return dict(aabb=t[0], planes=t[1:4], w0=t[4], b0=t[5], w1=t[6], b1=t[7]), hashlib.sha256(path.read_bytes()).hexdigest()

def grid_sample(plane, uv):
    # F.grid_sample(align_corners=True, padding_mode="border"); uv[:,0] indexes width, uv[:,1] height.
    c, h, w = plane.shape
    x = np.clip((uv[:, 0] + 1) * .5 * (w - 1), 0, w - 1); y = np.clip((uv[:, 1] + 1) * .5 * (h - 1), 0, h - 1)
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); x1 = np.minimum(x0 + 1, w - 1); y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[:, None]; fy = (y - y0)[:, None]
    p = plane.transpose(1, 2, 0)
    return (p[y0, x0] * (1 - fx) * (1 - fy) + p[y0, x1] * fx * (1 - fy) + p[y1, x0] * (1 - fx) * fy + p[y1, x1] * fx * fy)

def youngs(fields, sim):
    u = (sim - fields['aabb'][0]) / (fields['aabb'][1] - fields['aabb'][0]) * 2 - 1
    feature = sum(grid_sample(plane, u[:, pair]) for plane, pair in zip(fields['planes'], ([0, 1], [0, 2], [1, 2])))
    # einsum avoids spurious FP flags from Accelerate BLAS on Apple silicon.
    hidden = np.maximum(np.einsum('nj,ij->ni', feature, fields['w0']) + fields['b0'], 0)
    out = np.einsum('nj,ij->ni', hidden, fields['w1']) + fields['b1']
    return np.clip(INIT_YOUNG + 1000 * out[:, 0], 1.0, 5e8), u

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-root", type=Path, default=RAW_DATA / "flower")
    parser.add_argument("--output-root", type=Path, default=PROCESSED_DATA / "flower")
    args = parser.parse_args()
    raw_root = args.input_root.resolve()
    root = args.output_root.resolve()
    root.mkdir(parents=True, exist_ok=True)
    models = fetch_models(raw_root); fields, models_sha = read_fields(models)
    meta = json.loads((root / 'preparation.json').read_text())
    prepared = PlyData.read(root / 'carnation-prepared.ply')['vertex'].data
    rotation = Rotation.from_quat(meta['rotation_xyzw'])
    raw = rotation.inv().apply(np.stack([prepared[k] for k in ('x', 'y', 'z')], axis=1))
    # demo.py: scalar scale/shift from all coordinates of the simulated foreground Gaussians.
    scale = (raw.max() - raw.min()) * 1.8; shift = -raw.min() + (raw.max() - raw.min()) * .25
    sim = (raw + shift) / scale
    E, u = youngs(fields, sim)
    inside = float(np.mean(np.all(np.abs(u) <= 1, axis=1)))
    moving = PlyData.read(raw_root / 'carnation-moving.ply')['vertex'].data
    moving_sim = (np.stack([moving[k] for k in ('x', 'y', 'z')], axis=1) + shift) / scale
    distance, _ = cKDTree(moving_sim).query(sim, workers=-1)
    frozen = (distance > .5 / GRID_SIZE).astype(np.float32)
    out = np.empty(len(prepared), dtype=prepared.dtype.descr + [('youngs_sim', '<f4'), ('frozen', '<f4')])
    for name in prepared.dtype.names: out[name] = prepared[name]
    out['youngs_sim'] = E; out['frozen'] = frozen
    target = root / 'carnation-baked.ply'
    PlyData([PlyElement.describe(out, 'vertex')], text=False, byte_order='<').write(target)
    report = {
        'source': 'https://huggingface.co/datasets/YunjinZhang/PhysDreamer', 'member': MEMBER, 'models_sha256': models_sha,
        'formula': 'E = clamp(init_young + 1000 * TriplaneFields(sim_xyz), 1, 5e8); nu = 0.3; density = 2000; g = 0 (PhysDreamer demo.py)',
        'sim_scale': float(scale), 'sim_shift': float(shift),
        'flower_height_sim_units': float(np.ptp(prepared['y']) / scale),
        'fraction_inside_field_aabb': inside, 'frozen_fraction': float(frozen.mean()),
        'youngs_sim_percentiles_1_50_99': [float(v) for v in np.percentile(E, [1, 50, 99])],
        'baked_sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
    }
    (root / 'material-bake.json').write_text(json.dumps(report, indent=2)); print(json.dumps(report, indent=2))

if __name__ == "__main__":
    main()
