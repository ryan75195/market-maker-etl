from __future__ import annotations

import threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from mmclassifier.models import Ensemble, ModelRegistry


def test_get_is_safe_against_concurrent_callers(models_dir, fixed_choice_seed):
    model_dir = models_dir / "iphone-15"
    model_dir.mkdir(parents=True)
    (model_dir / "seed0.pt").write_bytes(b"")

    load_calls: list[str] = []
    load_started = threading.Event()
    release_load = threading.Event()

    def slow_loader(name: str, model_dir_: Path, device: str) -> Ensemble:
        load_calls.append(name)
        load_started.set()
        release_load.wait(timeout=5)
        seed = fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0})
        return Ensemble(name=name, seeds=[seed], device=device)

    registry = ModelRegistry(models_dir=models_dir, device="cpu", loader=slow_loader)

    with ThreadPoolExecutor(max_workers=2) as executor:
        first = executor.submit(registry.get, "iphone-15")
        assert load_started.wait(timeout=5)
        second = executor.submit(registry.get, "iphone-15")

        release_load.set()

        first_ensemble = first.result(timeout=5)
        second_ensemble = second.result(timeout=5)

    assert load_calls == ["iphone-15"]
    assert first_ensemble is second_ensemble
