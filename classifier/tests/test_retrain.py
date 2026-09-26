from __future__ import annotations

import json
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Tuple

import pytest

from mmclassifier.retrain import (
    FamilyNotFoundError,
    compare_accuracy,
    parse_args,
    run,
)

TAXONOMY = {
    "questions": {
        "item_type": {
            "instructions": "What is this?",
            "criteria": {"phone": "A phone.", "case": "A case."},
        }
    }
}


def _write_json(path: Path, data: Any) -> None:
    path.write_text(json.dumps(data), encoding="utf-8")


def _write_jsonl(path: Path, rows: List[Dict[str, Any]]) -> None:
    path.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")


def _labels_export_jsonl() -> str:
    return json.dumps(
        {
            "listingId": "m2",
            "taxonomyVersion": 1,
            "state": {"title": "t2", "mercari_category": "c", "brand": "b", "description": "d"},
            "answers": {"item_type": "case"},
        }
    )


@dataclass
class FakeApiClient:
    families: List[Dict[str, Any]]
    labels_jsonl: str
    promote_calls: List[Tuple[int, str]] = field(default_factory=list)

    def get_families(self) -> List[Dict[str, Any]]:
        return self.families

    def get_labels_jsonl(self, family_id: int) -> str:
        return self.labels_jsonl

    def promote(self, family_id: int, model_name: str) -> None:
        self.promote_calls.append((family_id, model_name))


def _fake_trainer(calls: List[Dict[str, Any]]):
    def trainer(init: Path, data_dir: Path, out_dir: Path, seeds: str, epochs: float, device):
        calls.append({"init": init, "data_dir": data_dir, "out_dir": out_dir})
        return {"7": {"item_type": 1.0}}

    return trainer


def _fake_evaluator(accuracy_by_dir: Dict[Path, Dict[str, float]]) -> Callable[..., Dict[str, Any]]:
    def evaluator(model_dir: Path, taxonomy, listings, device, batch_size):
        return {"per_question_accuracy": accuracy_by_dir[model_dir]}

    return evaluator


def _write_base_labels(tmp_path: Path) -> Path:
    _write_json(
        tmp_path / "pool.json",
        [{"id": "m1", "title": "t1", "category": "c", "brand": "b", "description": "d"}],
    )
    _write_json(tmp_path / "labels_0.json", [{"id": "m1", "item_type": "phone", "ambiguous": ""}])
    return tmp_path / "labels_*.json"


def _write_test_labels(tmp_path: Path) -> str:
    _write_json(
        tmp_path / "test_pool.json",
        [{"id": "t1", "title": "t1", "category": "c", "brand": "b", "description": "d"}],
    )
    _write_json(tmp_path / "test_labels_0.json", [{"id": "t1", "item_type": "phone", "ambiguous": ""}])
    return str(tmp_path / "test_labels_*.json")


def _base_args(tmp_path: Path, models_dir: Path, promote: bool = False):
    base_labels_pattern = _write_base_labels(tmp_path)
    test_pattern = _write_test_labels(tmp_path)
    _write_json(tmp_path / "taxonomy.json", TAXONOMY)
    _write_jsonl(tmp_path / "replay.jsonl", [])

    argv = [
        "--family-key",
        "iphone-15",
        "--api",
        "http://unused.test",
        "--taxonomy",
        str(tmp_path / "taxonomy.json"),
        "--base-labels",
        str(base_labels_pattern),
        "--replay",
        str(tmp_path / "replay.jsonl"),
        "--test",
        test_pattern,
        "--models-dir",
        str(models_dir),
        "--init",
        str(tmp_path / "base-hist-seed7.pt"),
        "--val-listings",
        "0",
    ]
    if promote:
        argv.append("--promote")
    return parse_args(argv)


def _api_with_current_model(model_name: str = "iphone-15-base") -> FakeApiClient:
    return FakeApiClient(
        families=[{"id": 1, "key": "iphone-15", "modelName": model_name}],
        labels_jsonl=_labels_export_jsonl(),
    )


def test_run_creates_a_new_directory_and_never_touches_the_current_model(tmp_path: Path):
    models_dir = tmp_path / "models"
    current_model_dir = models_dir / "iphone-15-base"
    current_model_dir.mkdir(parents=True)
    (current_model_dir / "seed7.pt").write_bytes(b"weights")

    args = _base_args(tmp_path, models_dir)
    api = _api_with_current_model()
    fixed_now = datetime(2026, 1, 1, 12, 0, tzinfo=timezone.utc)
    expected_out_dir = models_dir / "iphone-15-202601011200"
    trainer_calls: List[Dict[str, Any]] = []
    evaluator = _fake_evaluator(
        {expected_out_dir: {"item_type": 0.9}, current_model_dir: {"item_type": 0.9}}
    )

    result = run(args, api_client=api, trainer=_fake_trainer(trainer_calls), evaluator=evaluator, now=fixed_now)

    assert result.out_dir == expected_out_dir
    assert (result.out_dir / "data" / "train.jsonl").exists()
    assert (result.out_dir / "data" / "val.jsonl").exists()
    assert (result.out_dir / "comparison.json").exists()
    assert (result.out_dir / "fetched-labels.jsonl").read_text(encoding="utf-8") == api.labels_jsonl
    assert sorted(p.name for p in current_model_dir.iterdir()) == ["seed7.pt"]
    assert (current_model_dir / "seed7.pt").read_bytes() == b"weights"
    assert trainer_calls[0]["out_dir"] == expected_out_dir


def test_run_raises_instead_of_overwriting_an_existing_candidate_directory(tmp_path: Path):
    models_dir = tmp_path / "models"
    args = _base_args(tmp_path, models_dir)
    fixed_now = datetime(2026, 1, 1, 12, 0, tzinfo=timezone.utc)
    (models_dir / "iphone-15-202601011200").mkdir(parents=True)
    api = _api_with_current_model()

    with pytest.raises(FileExistsError):
        run(args, api_client=api, trainer=_fake_trainer([]), evaluator=_fake_evaluator({}), now=fixed_now)


def test_run_raises_when_family_key_is_unknown(tmp_path: Path):
    models_dir = tmp_path / "models"
    args = _base_args(tmp_path, models_dir)
    api = FakeApiClient(families=[], labels_jsonl="")

    with pytest.raises(FamilyNotFoundError):
        run(args, api_client=api, trainer=_fake_trainer([]), evaluator=_fake_evaluator({}))


def test_compare_accuracy_passes_when_candidate_is_at_least_as_good():
    comparison = compare_accuracy({"a": 0.9, "b": 0.8}, {"a": 0.85, "b": 0.8})

    assert comparison.passed is True
    assert comparison.dropped_questions == {}


def test_compare_accuracy_fails_when_mean_accuracy_drops():
    comparison = compare_accuracy({"a": 0.7, "b": 0.7}, {"a": 0.9, "b": 0.9})

    assert comparison.passed is False


def test_compare_accuracy_fails_when_a_single_question_drops_more_than_two_points():
    comparison = compare_accuracy({"a": 0.95, "b": 0.5}, {"a": 0.9, "b": 0.55})

    assert comparison.passed is False
    assert "b" in comparison.dropped_questions


def test_compare_accuracy_tolerates_a_small_drop_within_two_points():
    comparison = compare_accuracy({"a": 0.95, "b": 0.891}, {"a": 0.9, "b": 0.9})

    assert comparison.passed is True
    assert comparison.dropped_questions == {}


def test_compare_accuracy_fails_when_there_are_no_shared_questions():
    comparison = compare_accuracy({"a": 0.9}, {"b": 0.9})

    assert comparison.passed is False


def test_promote_calls_the_api_only_when_comparison_passes(tmp_path: Path):
    models_dir = tmp_path / "models"
    current_model_dir = models_dir / "iphone-15-base"
    current_model_dir.mkdir(parents=True)

    args = _base_args(tmp_path, models_dir, promote=True)
    api = _api_with_current_model()
    fixed_now = datetime(2026, 1, 1, 12, 0, tzinfo=timezone.utc)
    expected_out_dir = models_dir / "iphone-15-202601011200"
    evaluator = _fake_evaluator(
        {expected_out_dir: {"item_type": 0.95}, current_model_dir: {"item_type": 0.9}}
    )

    result = run(args, api_client=api, trainer=_fake_trainer([]), evaluator=evaluator, now=fixed_now)

    assert result.promoted is True
    assert api.promote_calls == [(1, expected_out_dir.name)]


def test_promote_is_skipped_when_comparison_fails(tmp_path: Path):
    models_dir = tmp_path / "models"
    current_model_dir = models_dir / "iphone-15-base"
    current_model_dir.mkdir(parents=True)

    args = _base_args(tmp_path, models_dir, promote=True)
    api = _api_with_current_model()
    fixed_now = datetime(2026, 1, 1, 12, 1, tzinfo=timezone.utc)
    expected_out_dir = models_dir / "iphone-15-202601011201"
    evaluator = _fake_evaluator(
        {expected_out_dir: {"item_type": 0.5}, current_model_dir: {"item_type": 0.9}}
    )

    result = run(args, api_client=api, trainer=_fake_trainer([]), evaluator=evaluator, now=fixed_now)

    assert result.promoted is False
    assert api.promote_calls == []


def test_promote_flag_not_set_never_calls_the_api_even_when_passing(tmp_path: Path):
    models_dir = tmp_path / "models"
    current_model_dir = models_dir / "iphone-15-base"
    current_model_dir.mkdir(parents=True)

    args = _base_args(tmp_path, models_dir, promote=False)
    api = _api_with_current_model()
    fixed_now = datetime(2026, 1, 1, 12, 2, tzinfo=timezone.utc)
    expected_out_dir = models_dir / "iphone-15-202601011202"
    evaluator = _fake_evaluator(
        {expected_out_dir: {"item_type": 0.95}, current_model_dir: {"item_type": 0.9}}
    )

    result = run(args, api_client=api, trainer=_fake_trainer([]), evaluator=evaluator, now=fixed_now)

    assert result.promoted is False
    assert api.promote_calls == []
