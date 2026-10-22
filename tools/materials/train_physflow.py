"""Preflight or launch the upstream CUDA training program; does not implement a second trainer."""
import argparse
import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import RAW_DATA, ARTIFACTS

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scene")
    parser.add_argument("--physflow-root", type=Path, required=True)
    parser.add_argument("--model-root", type=Path, default=RAW_DATA / "physflow")
    parser.add_argument("--output-root", type=Path, default=ARTIFACTS / "training" / "physflow")
    parser.add_argument("--check", action="store_true", help="Report missing hardware/dependencies/data without starting training")
    parser.add_argument("--epochs", type=int, default=10)
    args = parser.parse_args()
    upstream = args.physflow_root.resolve()
    model = args.model_root.resolve() / args.scene
    config = args.model_root.resolve() / "configs" / f"{args.scene}_config.json"
    errors = []
    cuda = False
    device = None
    if sys.version_info[:2] != (3, 9):
        errors.append("Use the upstream validated Python 3.9 environment")
    required_modules = ("warp", "taichi", "numpy", "scipy", "torchvision", "cv2", "omegaconf", "wandb", "pytorch3d", "point_cloud_utils", "diff_plane_rasterization", "simple_knn")
    for module in required_modules:
        if importlib.util.find_spec(module) is None:
            errors.append(f"Missing upstream dependency: {module}")
    if shutil.which("nvcc") is None:
        errors.append("CUDA toolkit nvcc is missing; the upstream rasterizer and kNN extensions require compilation")
    if importlib.util.find_spec("torch") is None:
        errors.append("Install upstream PyTorch CUDA dependencies in this Python environment")
    else:
        import torch
        cuda = torch.cuda.is_available()
        if cuda:
            free, total = torch.cuda.mem_get_info()
            device = {"name": torch.cuda.get_device_name(), "free_bytes": free, "total_bytes": total, "torch_cuda": torch.version.cuda}
            # The pinned entry point reserves this pool before constructing the PyTorch flow/render losses.
            if free < 8 * 1024 ** 3:
                errors.append("Pinned simulation.py requests an 8 GiB Taichi pool; available VRAM is insufficient. Validate a separately versioned low-memory trainer before launching")
        if not cuda: errors.append("The upstream differentiable solver requires an NVIDIA CUDA GPU; MPS is not a substitute")
    for path in [upstream / "simulation.py", model / "point_cloud", model / "cameras.json", config]:
        if not path.exists(): errors.append(f"Missing {path}")
    if not (model / "images_generated").is_dir() or not any((model / "images_generated").glob("*")):
        errors.append("Missing images_generated guidance frames; prepare them using the upstream video_generator.py and required model weights")
    commit = subprocess.check_output(["git", "-C", str(upstream), "rev-parse", "HEAD"], text=True).strip() if (upstream / ".git").exists() else None
    if commit is None: errors.append("Use a versioned upstream checkout to record the training commit")
    elif commit != "6beba6746fad4590f5b63ce6fa85c3b862b182d9": errors.append("Unverified trainer commit; use the supported upstream revision or validate the importer before training")
    elif subprocess.run(["git", "-C", str(upstream), "diff", "--quiet", "HEAD"], check=False).returncode:
        errors.append("Commit trainer modifications and validate their material schema before training")
    status = {"scene": args.scene, "cuda": cuda, "device": device, "python": sys.version.split()[0], "upstream_commit": commit, "ready": not errors, "errors": errors}
    print(json.dumps(status, indent=2))
    if args.check: return
    if errors: raise SystemExit("Training preflight failed; no training was started")
    args.output_root.mkdir(parents=True, exist_ok=True)
    # Official simulation.py appends its own sampling/epoch suffix.
    checkpoint_directory = args.output_root.resolve() / f"{args.scene}_ds1.0_ep{args.epochs}" / "ckpt"
    command = [sys.executable, str(upstream / "simulation.py"), "--model_path", str(model), "--physics_config", str(config), "--output_path", str(args.output_root.resolve() / args.scene), "--n_epoch", str(args.epochs)]
    (args.output_root / f"{args.scene}-run.json").write_text(json.dumps({**status, "command": command, "checkpoint_directory": str(checkpoint_directory)}, indent=2))
    print(f"Official training checkpoints: {checkpoint_directory}")
    subprocess.run(command, cwd=upstream, check=True)

if __name__ == "__main__":
    main()
