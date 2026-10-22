"""Restore the official, non-commercial SpeedTree research payloads; never fetch on import."""
import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from repository_paths import REPOSITORY_ROOT

REVISION = "6032de37ee5cccfe5d86b5cf0959f66747de9342"
PREFIX = f"https://raw.githubusercontent.com/Unity-Technologies/WaterScenes/{REVISION}/"
DEFAULT_DESTINATION = REPOSITORY_ROOT / "apps/unity/Assets/Content/SpeedTree"


def restore(destination):
    manifest = json.loads((DEFAULT_DESTINATION / "source.json").read_text())
    if manifest["revision"] != REVISION:
        raise ValueError("Unverified official source revision")
    for item in manifest["files"]:
        relative = Path(item["file"])
        if relative.is_absolute() or ".." in relative.parts or not item["url"].startswith(PREFIX):
            raise ValueError("Invalid payload manifest entry")
        # Versioned import metadata preserves scene GUIDs and intentional URP remapping.
        if relative.suffix == ".meta":
            continue
        target = destination / relative
        if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() == item["sha256"]:
            continue
        with urllib.request.urlopen(item["url"], timeout=120) as response:
            data = response.read()
        if len(data) != item["bytes"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
            raise ValueError(f"Official payload hash mismatch: {relative}")
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_suffix(target.suffix + ".download")
        temporary.write_bytes(data)
        temporary.replace(target)
    print("Restored official SpeedTree research payloads (non-commercial only).")
    print("In Unity: Physics Hands > Import > Restore SpeedTree Research Mesh")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, default=DEFAULT_DESTINATION)
    args = parser.parse_args()
    restore(args.destination.resolve())


if __name__ == "__main__":
    main()
