from __future__ import annotations

import argparse
import json
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from statistics import mean
from typing import Any, Callable, Dict, List, Optional, Protocol

from mmclassifier.build import load_replay_pool, write_jsonl
from mmclassifier.dataset import DEFAULT_SPLIT_SEED, build_dataset
from mmclassifier.evaluate import evaluate_model
from mmclassifier.labels import load_label_sources, load_labels
from mmclassifier.taxonomy import load_taxonomy

ACCURACY_DROP_TOLERANCE_POINTS = 2.0
FETCHED_LABELS_FILENAME = "fetched-labels.jsonl"
DATA_DIRNAME = "data"
COMPARISON_FILENAME = "comparison.json"

Trainer = Callable[[Path, Path, Path, str, float, Optional[str]], Dict[str, Dict[str, float]]]
Evaluator = Callable[[Path, Dict[str, Any], List[Any], Optional[str], int], Dict[str, Any]]


class FamilyNotFoundError(LookupError):
    def __init__(self, family_key: str) -> None:
        super().__init__(f"Unknown family key: {family_key!r}")
        self.family_key = family_key


class ApiClient(Protocol):
    def get_families(self) -> List[Dict[str, Any]]: ...

    def get_labels_jsonl(self, family_id: int) -> str: ...

    def promote(self, family_id: int, model_name: str) -> None: ...


@dataclass
class HttpApiClient:
    base_url: str

    def get_families(self) -> List[Dict[str, Any]]:
        return json.loads(self._get(f"{self.base_url}/api/families"))

    def get_labels_jsonl(self, family_id: int) -> str:
        return self._get(f"{self.base_url}/api/families/{family_id}/labels.jsonl")

    def promote(self, family_id: int, model_name: str) -> None:
        body = json.dumps({"modelName": model_name}).encode("utf-8")
        request = urllib.request.Request(
            f"{self.base_url}/api/families/{family_id}",
            data=body,
            method="PUT",
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(request) as response:
            response.read()

    def _get(self, url: str) -> str:
        with urllib.request.urlopen(url) as response:
            return response.read().decode("utf-8")


@dataclass
class Comparison:
    candidate_accuracy: Dict[str, float]
    current_accuracy: Dict[str, float]
    candidate_mean: float
    current_mean: float
    dropped_questions: Dict[str, float]
    passed: bool


def compare_accuracy(candidate_accuracy: Dict[str, float], current_accuracy: Dict[str, float]) -> Comparison:
    shared_questions = sorted(set(candidate_accuracy) & set(current_accuracy))
    candidate_mean = mean(candidate_accuracy[name] for name in shared_questions) if shared_questions else 0.0
    current_mean = mean(current_accuracy[name] for name in shared_questions) if shared_questions else 0.0

    dropped_questions = {
        name: (current_accuracy[name] - candidate_accuracy[name]) * 100
        for name in shared_questions
        if (current_accuracy[name] - candidate_accuracy[name]) * 100 > ACCURACY_DROP_TOLERANCE_POINTS
    }

    passed = bool(shared_questions) and candidate_mean >= current_mean and not dropped_questions

    return Comparison(
        candidate_accuracy=candidate_accuracy,
        current_accuracy=current_accuracy,
        candidate_mean=candidate_mean,
        current_mean=current_mean,
        dropped_questions=dropped_questions,
        passed=passed,
    )


@dataclass
class RetrainResult:
    out_dir: Path
    comparison: Comparison
    comparison_path: Path
    promoted: bool


def default_trainer(
    init: Path, data_dir: Path, out_dir: Path, seeds: str, epochs: float, device: Optional[str]
) -> Dict[str, Dict[str, float]]:
    from mmclassifier import train

    train_args = train.parse_args(
        [
            "--init",
            str(init),
            "--data",
            str(data_dir),
            "--seeds",
            seeds,
            "--epochs",
            str(epochs),
            "--out",
            str(out_dir),
            *(["--device", device] if device else []),
        ]
    )
    return train.run(train_args)


def default_evaluator(
    model_dir: Path,
    taxonomy: Dict[str, Any],
    listings: List[Any],
    device: Optional[str],
    batch_size: int,
) -> Dict[str, Any]:
    from mmclassifier.models import load_laya_ensemble

    resolved_device = device or _default_device()
    return evaluate_model(model_dir, taxonomy, listings, resolved_device, batch_size, load_laya_ensemble)


def _default_device() -> str:
    try:
        import torch

        return "cuda" if torch.cuda.is_available() else "cpu"
    except ImportError:
        return "cpu"


def new_model_dir_name(family_key: str, now: datetime) -> str:
    return f"{family_key}-{now.strftime('%Y%m%d%H%M')}"


def run(
    args: argparse.Namespace,
    api_client: Optional[ApiClient] = None,
    trainer: Trainer = default_trainer,
    evaluator: Evaluator = default_evaluator,
    now: Optional[datetime] = None,
) -> RetrainResult:
    api = api_client or HttpApiClient(args.api)
    moment = now or datetime.now(timezone.utc)

    families = api.get_families()
    family = next((candidate for candidate in families if candidate["key"] == args.family_key), None)
    if family is None:
        raise FamilyNotFoundError(args.family_key)

    out_dir = Path(args.models_dir) / new_model_dir_name(args.family_key, moment)
    out_dir.mkdir(parents=True, exist_ok=False)

    fetched_labels_path = out_dir / FETCHED_LABELS_FILENAME
    fetched_labels_path.write_text(api.get_labels_jsonl(family["id"]), encoding="utf-8")

    taxonomy = load_taxonomy(args.taxonomy)
    listings = load_label_sources([f"pilot:{args.base_labels}", f"export:{fetched_labels_path}"])
    replay_pool = load_replay_pool(args.replay)

    build_result = build_dataset(
        taxonomy=taxonomy,
        listings=listings,
        replay_pool=replay_pool,
        val_listings=args.val_listings,
        replay_count=args.replay_count,
        seed=args.seed,
    )
    data_dir = out_dir / DATA_DIRNAME
    data_dir.mkdir(parents=True, exist_ok=True)
    write_jsonl(data_dir / "train.jsonl", build_result.train_rows)
    write_jsonl(data_dir / "val.jsonl", build_result.val_rows)

    trainer(Path(args.init), data_dir, out_dir, args.seeds, args.epochs, args.device)

    test_listings = load_labels(args.test, "pilot")
    candidate_report = evaluator(out_dir, taxonomy, test_listings, args.device, args.batch_size)
    current_model_dir = Path(args.models_dir) / family["modelName"]
    current_report = evaluator(current_model_dir, taxonomy, test_listings, args.device, args.batch_size)

    comparison = compare_accuracy(
        candidate_report["per_question_accuracy"], current_report["per_question_accuracy"]
    )
    comparison_payload = {
        "candidateModel": out_dir.name,
        "currentModel": family["modelName"],
        "candidateAccuracy": comparison.candidate_accuracy,
        "currentAccuracy": comparison.current_accuracy,
        "candidateMean": comparison.candidate_mean,
        "currentMean": comparison.current_mean,
        "droppedQuestions": comparison.dropped_questions,
        "passed": comparison.passed,
    }
    comparison_path = out_dir / COMPARISON_FILENAME
    comparison_path.write_text(json.dumps(comparison_payload, indent=2), encoding="utf-8")

    promoted = False
    if args.promote and comparison.passed:
        api.promote(family["id"], out_dir.name)
        promoted = True

    return RetrainResult(out_dir=out_dir, comparison=comparison, comparison_path=comparison_path, promoted=promoted)


def parse_args(argv: List[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="python -m mmclassifier.retrain")
    parser.add_argument("--family-key", required=True)
    parser.add_argument("--api", required=True)
    parser.add_argument("--taxonomy", required=True, type=Path)
    parser.add_argument("--base-labels", required=True)
    parser.add_argument("--replay", required=True, type=Path)
    parser.add_argument("--test", required=True)
    parser.add_argument("--models-dir", required=True, type=Path)
    parser.add_argument("--init", required=True, type=Path)
    parser.add_argument("--promote", action="store_true")
    parser.add_argument("--replay-count", type=int, default=3000)
    parser.add_argument("--val-listings", type=int, default=40)
    parser.add_argument("--seed", type=int, default=DEFAULT_SPLIT_SEED)
    parser.add_argument("--seeds", default="7,11,13")
    parser.add_argument("--epochs", type=float, default=3)
    parser.add_argument("--device", default=None)
    parser.add_argument("--batch-size", type=int, default=32)
    return parser.parse_args(argv)


def main(argv: List[str] | None = None) -> None:
    args = parse_args(argv)
    result = run(args)
    print(f"candidate {result.out_dir}")
    print(
        f"candidate mean {result.comparison.candidate_mean:.1%} "
        f"current mean {result.comparison.current_mean:.1%} "
        f"passed={result.comparison.passed}"
    )
    if result.comparison.dropped_questions:
        print(f"dropped questions: {result.comparison.dropped_questions}")
    print(f"wrote {result.comparison_path}")
    if args.promote:
        print("promoted" if result.promoted else "not promoted")


if __name__ == "__main__":
    main()
