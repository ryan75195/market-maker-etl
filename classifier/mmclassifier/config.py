from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path
from typing import List, Optional


def _default_device() -> str:
    try:
        import torch

        return "cuda" if torch.cuda.is_available() else "cpu"
    except ImportError:
        return "cpu"


@dataclass(frozen=True)
class Settings:
    models_dir: Path
    device: str
    api_key: Optional[str]
    preload: List[str]
    host: str
    port: int

    @classmethod
    def from_env(cls) -> "Settings":
        models_dir = os.environ.get("MODELS_DIR")
        if not models_dir:
            raise RuntimeError("MODELS_DIR environment variable is required")

        preload_raw = os.environ.get("CLASSIFIER_PRELOAD", "")
        preload = [name.strip() for name in preload_raw.split(",") if name.strip()]

        return cls(
            models_dir=Path(models_dir),
            device=os.environ.get("CLASSIFIER_DEVICE") or _default_device(),
            api_key=os.environ.get("CLASSIFIER_API_KEY") or None,
            preload=preload,
            host=os.environ.get("CLASSIFIER_HOST", "0.0.0.0"),
            port=int(os.environ.get("CLASSIFIER_PORT", "8765")),
        )
