"""Validate an official PhysFlow scalar sim.json and attach traceable material provenance.

The pinned official solver stores each optimized material parameter as a one-element tensor;
sim.json is lossless for those fields. Spatially varying forks require a different importer.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import RAW_DATA, PROCESSED_DATA

SUPPORTED_TRAINING_COMMIT = "6beba6746fad4590f5b63ce6fa85c3b862b182d9"

FIELDS = {"E", "nu", "yield_stress", "plastic_viscosity", "friction_angle"}

def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def convert(scene, checkpoint, output_root, raw_root, commit):
    config_path = raw_root / "configs" / f"{scene}_config.json"
    report_path = output_root / f"{scene}.json"
    config = json.loads(config_path.read_text())
    report = json.loads(report_path.read_text())
    source = json.loads(checkpoint.read_text())
    unsupported = set(config.get("lr", {})) - FIELDS
    if unsupported: raise ValueError(f"Unsupported optimized material fields: {sorted(unsupported)}")
    expected = set(config.get("lr", {}))
    if not expected or not expected.issubset(source):
        raise ValueError(f"Checkpoint must include all optimized scalar fields: {sorted(expected)}")
    values = {}
    for name in expected:
        value = source[name]
        if not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(value):
            raise ValueError(f"{name}: expected a finite scalar from official sim.json; arrays/means from spatial forks are unsupported")
        if name == "nu" and not (0 <= value < .5):
            raise ValueError("nu must be in [0, 0.5)")
        if name == "E" and value <= 0 or name in {"yield_stress", "plastic_viscosity"} and value < 0:
            raise ValueError(f"Invalid {name}: {value}")
        if name == "friction_angle" and not (0 <= value < 90):
            raise ValueError("friction_angle must be in degrees, in [0, 90)")
        values[name] = float(value)
    if len(commit) != 40 or any(c not in "0123456789abcdef" for c in commit):
        raise ValueError("A full upstream training commit SHA is required")
    if commit != SUPPORTED_TRAINING_COMMIT:
        raise ValueError("Unverified trainer version: validate tensor shapes and units before extending the supported commit")
    result = {"schema": 1, "scene": scene, "material": config["material"],
              "parameter_origin": "physflow-trained-scalars", "training_commit": commit,
              "checkpoint_sha256": sha256(checkpoint), "config_sha256": sha256(config_path),
              "source_sha256": report["source_sha256"], "prepared_sha256": report["output_sha256"],
              "units": "PhysFlow raw parameters: E x1e7, yield_stress x1e5, plastic_viscosity x1e4; nu dimensionless; friction_angle degrees",
              "parameters": values}
    target = output_root / f"{scene}.trained-material.json"
    target.write_text(json.dumps(result, indent=2) + "\n")
    return target

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scene")
    parser.add_argument("--checkpoint", required=True, type=Path, help="Official output/ckpt/sim.json")
    parser.add_argument("--training-commit", required=True)
    parser.add_argument("--raw-root", type=Path, default=RAW_DATA / "physflow")
    parser.add_argument("--output-root", type=Path, default=PROCESSED_DATA / "physflow")
    args = parser.parse_args()
    print(convert(args.scene, args.checkpoint.resolve(), args.output_root.resolve(), args.raw_root.resolve(), args.training_commit))

if __name__ == "__main__":
    main()
