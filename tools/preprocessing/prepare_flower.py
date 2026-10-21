"""Select the author's exact segmented Gaussian IDs and rigidly orient the flower.
Requirements: numpy scipy plyfile. No synthesis, mesh conversion or retraining.
"""
from pathlib import Path
import hashlib,json
import numpy as np
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation
from plyfile import PlyData,PlyElement
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import RAW_DATA, PROCESSED_DATA

import argparse

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-root", type=Path, default=RAW_DATA / "flower")
    parser.add_argument("--output-root", type=Path, default=PROCESSED_DATA / "flower")
    args = parser.parse_args()
    root = args.input_root.resolve()
    output_root = args.output_root.resolve()
    output_root.mkdir(parents=True, exist_ok=True)
    raw=PlyData.read(root/'carnation.ply')['vertex'].data
    mask=PlyData.read(root/'carnation-mask.ply')['vertex'].data
    xyz=np.stack([raw[k] for k in ('x','y','z')],axis=1)
    query=np.stack([mask[k] for k in ('x','y','z')],axis=1)
    distance,indices=cKDTree(xyz).query(query,workers=-1)
    if np.max(distance)>1e-7:raise ValueError('Author mask is not an exact subset of the source Gaussians')
    indices=np.unique(indices);data=raw[indices].copy()
    # Author camera up is [0,-0.5,1] in projects/inference/configs/carnation.py.
    up=np.array([0.,-.5,1.]);up/=np.linalg.norm(up);target=np.array([0.,1.,0.]);axis=np.cross(up,target);axis/=np.linalg.norm(axis)
    rotation=Rotation.from_rotvec(axis*np.arccos(np.dot(up,target)))
    points=rotation.apply(xyz[indices])
    for i,k in enumerate(('x','y','z')):data[k]=points[:,i]
    quats=np.stack([data[k] for k in ('rot_1','rot_2','rot_3','rot_0')],axis=1)
    quats=(rotation*Rotation.from_quat(quats)).as_quat()
    for i,k in enumerate(('rot_1','rot_2','rot_3','rot_0')):data[k]=quats[:,i]
    output=output_root/'carnation-prepared.ply';PlyData([PlyElement.describe(data,'vertex')],text=False,byte_order='<').write(output)
    meta={'source':'https://huggingface.co/datasets/YunjinZhang/PhysDreamer','source_member':'physics_dreamer/carnations/point_cloud.ply','source_gaussians':len(raw),'selected_gaussians':len(data),'selection':'exact author clean_object_points.ply subset','max_selection_distance':float(np.max(distance)),'rotation_xyzw':rotation.as_quat().tolist(),'raw_sha256':hashlib.sha256((root/'carnation.ply').read_bytes()).hexdigest(),'prepared_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'runtime_scale':'height normalized to 0.3 m by GaussianPly.cs; this is an explicit demo scale, not measured scale','appearance':'source SH coefficients retained in PLY; runtime retains degree-zero and first-order SH'}
    (output_root/'preparation.json').write_text(json.dumps(meta,indent=2));print(json.dumps(meta,indent=2))

if __name__ == "__main__":
    main()
