from __future__ import annotations

import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from fastapi.testclient import TestClient

from mmclassifier.app import create_app
from mmclassifier.models import Ensemble, ModelRegistry


def _touch_seed(models_dir: Path, name: str) -> None:
    model_dir = models_dir / name
    model_dir.mkdir(parents=True, exist_ok=True)
    (model_dir / "seed0.pt").write_bytes(b"")


def _make_slow_loader(fixed_choice_seed, load_calls, load_started, release_load, wait_timeout=5):
    def slow_loader(name: str, model_dir: Path, device: str) -> Ensemble:
        load_calls.append(name)
        load_started.set()
        release_load.wait(timeout=wait_timeout)
        seed = fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0})
        return Ensemble(name=name, seeds=[seed], device=device)

    return slow_loader


def test_concurrent_requests_for_unloaded_model_load_it_only_once(
    models_dir, sample_questions, fixed_choice_seed
):
    _touch_seed(models_dir, "iphone-15")

    load_calls: list[str] = []
    load_started = threading.Event()
    release_load = threading.Event()

    registry = ModelRegistry(
        models_dir=models_dir,
        device="cpu",
        loader=_make_slow_loader(fixed_choice_seed, load_calls, load_started, release_load),
    )
    request_body = {"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]}

    with TestClient(create_app(registry=registry, api_key=None)) as client:
        with ThreadPoolExecutor(max_workers=2) as executor:
            first = executor.submit(client.post, "/v1/classify", json=request_body)
            assert load_started.wait(timeout=5)
            second = executor.submit(client.post, "/v1/classify", json=request_body)

            release_load.set()

            first_response = first.result(timeout=5)
            second_response = second.result(timeout=5)

    assert load_calls == ["iphone-15"]
    assert first_response.status_code == 200
    assert second_response.status_code == 200


def test_health_responds_while_slow_load_is_in_progress(models_dir, sample_questions, fixed_choice_seed):
    _touch_seed(models_dir, "iphone-15")

    load_started = threading.Event()
    release_load = threading.Event()

    registry = ModelRegistry(
        models_dir=models_dir,
        device="cpu",
        loader=_make_slow_loader(fixed_choice_seed, [], load_started, release_load, wait_timeout=30),
    )
    request_body = {"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]}

    with TestClient(create_app(registry=registry, api_key=None)) as client:
        with ThreadPoolExecutor(max_workers=1) as executor:
            classify_future = executor.submit(client.post, "/v1/classify", json=request_body)
            assert load_started.wait(timeout=5)

            started_at = time.monotonic()
            health_response = client.get("/health")
            health_elapsed = time.monotonic() - started_at

            release_load.set()
            classify_response = classify_future.result(timeout=35)

    assert health_elapsed < 2.0
    assert health_response.status_code == 200
    assert health_response.json()["models"] == {}
    assert classify_response.status_code == 200
