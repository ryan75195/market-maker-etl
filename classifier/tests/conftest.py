from __future__ import annotations

from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

import pytest
from fastapi.testclient import TestClient

from mmclassifier.app import create_app
from mmclassifier.models import Ensemble, ModelRegistry


class FakeSeedPredictor:
    def __init__(self, respond: Callable[[List[Any], Dict[str, Any]], List[Dict[str, Any]]]) -> None:
        self._respond = respond
        self.calls: List[Any] = []

    def predict_batch(
        self, states: List[Any], questions: Dict[str, Any], batch_size: Optional[int] = None
    ) -> List[Dict[str, Any]]:
        self.calls.append((states, questions, batch_size))
        return self._respond(states, questions)


def _fixed_choice_seed(choice: str, probabilities: Dict[str, float]) -> FakeSeedPredictor:
    def respond(states: List[Any], questions: Dict[str, Any]) -> List[Dict[str, Any]]:
        answers = {
            question_id: {"choice": choice, "probabilities": probabilities} for question_id in questions
        }
        return [{"answers": answers} for _ in states]

    return FakeSeedPredictor(respond)


def _per_state_seed(answers_by_state: List[Dict[str, Dict[str, Any]]]) -> FakeSeedPredictor:
    def respond(states: List[Any], questions: Dict[str, Any]) -> List[Dict[str, Any]]:
        return [{"answers": answers_by_state[index]} for index in range(len(states))]

    return FakeSeedPredictor(respond)


def _touch_model_dir(models_dir: Path, name: str, seed_count: int) -> None:
    model_dir = models_dir / name
    model_dir.mkdir(parents=True, exist_ok=True)
    for index in range(seed_count):
        (model_dir / f"seed{index}.pt").write_bytes(b"")


@pytest.fixture
def fixed_choice_seed() -> Callable[[str, Dict[str, float]], FakeSeedPredictor]:
    return _fixed_choice_seed


@pytest.fixture
def per_state_seed() -> Callable[[List[Dict[str, Dict[str, Any]]]], FakeSeedPredictor]:
    return _per_state_seed


@pytest.fixture
def models_dir(tmp_path: Path) -> Path:
    return tmp_path / "models"


@pytest.fixture
def sample_questions() -> Dict[str, Any]:
    return {
        "item_type": {
            "type": "choice",
            "instructions": "What is this?",
            "criteria": {
                "iphone_15_family": "An iPhone 15.",
                "other": "Anything else.",
            },
        }
    }


@pytest.fixture
def make_client(models_dir: Path):
    def _make_client(
        seeds_by_name: Dict[str, List[FakeSeedPredictor]],
        api_key: Optional[str] = None,
        device: str = "cpu",
        batch_size: int = 16,
    ) -> TestClient:
        for name, seeds in seeds_by_name.items():
            _touch_model_dir(models_dir, name, len(seeds))

        def loader(name: str, model_dir: Path, device_: str) -> Ensemble:
            return Ensemble(name=name, seeds=seeds_by_name[name], device=device_)

        registry = ModelRegistry(models_dir=models_dir, device=device, loader=loader)
        app = create_app(registry=registry, api_key=api_key, batch_size=batch_size)
        return TestClient(app)

    return _make_client
