"""Repository paths independent of the caller's working directory."""
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
RAW_DATA = REPOSITORY_ROOT / "data" / "raw"
PROCESSED_DATA = REPOSITORY_ROOT / "data" / "processed"
ARTIFACTS = REPOSITORY_ROOT / "artifacts"
