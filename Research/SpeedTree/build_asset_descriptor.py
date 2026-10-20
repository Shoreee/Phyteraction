"""Audit an imported SpeedTree FBX before assigning any physical parameters.

Usage: Research/GaussianWorld/.venv/bin/python Research/SpeedTree/build_asset_descriptor.py
The output is a representation-neutral contract for future Mesh and 3DGS physics adapters.
No stiffness, density, damping or contact coefficient is inferred from appearance here.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_EXPORTED = ROOT / "Research/SpeedTree/generated/boston_fern_mesh.json"
DEFAULT_OUTPUT = ROOT / "Research/SpeedTree/generated/boston_fern_descriptor.json"


def components(triangles: np.ndarray, vertex_count: int) -> tuple[int, list[int]]:
    parent = np.arange(vertex_count)

    def find(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = int(parent[x])
        return x

    for a, b, c in triangles:
        a, b, c = int(a), int(b), int(c)
        ra, rb, rc = find(a), find(b), find(c)
        parent[rb] = ra
        parent[rc] = ra
    used, counts = np.unique(triangles, return_counts=True)
    roots = np.fromiter((find(int(i)) for i in used), dtype=np.int64)
    _, sizes = np.unique(roots, return_counts=True)
    return len(sizes), sorted((int(x) for x in sizes), reverse=True)[:16]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=DEFAULT_EXPORTED)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    raw = json.loads(args.input.read_text())
    source = ROOT / raw["source"]
    parts = []
    overall_min = np.full(3, np.inf)
    overall_max = np.full(3, -np.inf)
    for part in raw["parts"]:
        v = np.asarray([[p["x"], p["y"], p["z"]] for p in part["vertices"]], dtype=np.float64)
        t = np.asarray(part["triangles"], dtype=np.int64).reshape(-1, 3)
        m = np.asarray(part["triangleMaterials"], dtype=np.int64)
        if not np.isfinite(v).all() or len(t) != len(m) or np.any(t < 0) or np.any(t >= len(v)):
            raise ValueError(f"Invalid mesh topology in {part['name']}")
        areas = np.linalg.norm(np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]]), axis=1) / 2
        overall_min = np.minimum(overall_min, v.min(0)); overall_max = np.maximum(overall_max, v.max(0))
        component_count, largest = components(t, len(v))
        materials = []
        for i, name in enumerate(part["materials"]):
            mask = m == i
            materials.append({"name": name, "triangles": int(mask.sum()), "surface_area_import_units2": float(areas[mask].sum())})
        parts.append({"name": part["name"], "vertices": len(v), "triangles": len(t),
                      "connected_components_by_shared_vertex": component_count,
                      "largest_components_vertices": largest,
                      "degenerate_triangles": int((areas < 1e-12).sum()),
                      "materials": materials})
    descriptor = {
        "schema_version": 1,
        "source": raw["source"], "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "license": raw["license"], "representation": "triangle_mesh", "coordinate_system": "Unity Y-up",
        "metric_scale_m_per_import_unit": None,
        "bounds_import_units": {"min": overall_min.tolist(), "max": overall_max.tolist()},
        "parts": parts,
        "known_boundary": None, "structural_graph": None, "material_posterior": None,
        "physics_ready": False,
        "blocked_by": ["metric scale unverified", "root attachment not identified",
                       "branch/leaf connectivity not identified", "no trained transferable material model"],
        "note": "Material names are visual labels, not physical ground truth. Disconnected FBX cards cannot safely be treated as elastic solids.",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(descriptor, indent=2, ensure_ascii=False) + "\n")
    print(f"{args.output}: {sum(p['vertices'] for p in parts)} vertices, "
          f"{sum(p['triangles'] for p in parts)} triangles, physics_ready=false")


if __name__ == "__main__":
    main()
