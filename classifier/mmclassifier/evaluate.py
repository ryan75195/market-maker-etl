from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any, Callable, Dict, List

from mmclassifier.labels import load_labels
from mmclassifier.rows import LabelledListing, normalize_state
from mmclassifier.taxonomy import all_question_names, is_applicable, load_taxonomy, question_spec

CONFIDENCE_THRESHOLD = 0.9

EnsembleLoader = Callable[[str, Path, str], Any]


def build_questions(taxonomy: Dict[str, Any]) -> Dict[str, Any]:
    return {name: question_spec(taxonomy, name) for name in all_question_names(taxonomy)}


def score_predictions(
    taxonomy: Dict[str, Any],
    listings: List[LabelledListing],
    predictions: List[Dict[str, Any]],
) -> Dict[str, Any]:
    per_question_hits: Dict[str, int] = {}
    per_question_totals: Dict[str, int] = {}
    bucket_hits = {"high": 0, "low": 0}
    bucket_totals = {"high": 0, "low": 0}
    mistakes: List[Dict[str, Any]] = []

    for listing, prediction in zip(listings, predictions):
        for name in all_question_names(taxonomy):
            gold = listing.answers.get(name)
            criteria = taxonomy["questions"][name]["criteria"]
            if gold not in criteria:
                continue
            if not is_applicable(taxonomy, name, listing.answers):
                continue

            answer = prediction["answers"][name]
            choice = answer["choice"]
            confidence = answer["confidence"]
            correct = choice == gold

            per_question_totals[name] = per_question_totals.get(name, 0) + 1
            per_question_hits[name] = per_question_hits.get(name, 0) + int(correct)

            bucket = "high" if confidence >= CONFIDENCE_THRESHOLD else "low"
            bucket_totals[bucket] += 1
            bucket_hits[bucket] += int(correct)

            if not correct:
                mistakes.append(
                    {
                        "listing_id": listing.id,
                        "question": name,
                        "gold": gold,
                        "choice": choice,
                        "confidence": confidence,
                    }
                )

    per_question_accuracy = {
        name: per_question_hits[name] / per_question_totals[name] for name in per_question_totals
    }
    total_scored = bucket_totals["high"] + bucket_totals["low"]
    confidence_split = {
        bucket: {
            "count": bucket_totals[bucket],
            "accuracy": (bucket_hits[bucket] / bucket_totals[bucket]) if bucket_totals[bucket] else None,
        }
        for bucket in ("high", "low")
    }
    flagged_fraction = bucket_totals["low"] / total_scored if total_scored else 0.0

    return {
        "per_question_accuracy": per_question_accuracy,
        "confidence_split": confidence_split,
        "flagged_fraction": flagged_fraction,
        "scored_answers": total_scored,
        "mistakes": mistakes,
    }


def print_report(report: Dict[str, Any]) -> None:
    print(f"scored {report['scored_answers']} answers")
    for name, accuracy in sorted(report["per_question_accuracy"].items()):
        print(f"  {name}: {accuracy:.1%}")
    for bucket in ("high", "low"):
        stats = report["confidence_split"][bucket]
        accuracy = stats["accuracy"]
        label = f">= {CONFIDENCE_THRESHOLD:.2f}" if bucket == "high" else f"< {CONFIDENCE_THRESHOLD:.2f}"
        if accuracy is None:
            print(f"  confidence {label} (n=0)")
        else:
            print(f"  confidence {label} (n={stats['count']}): {accuracy:.1%}")
    print(f"flagged fraction: {report['flagged_fraction']:.1%}")
    print(f"mistakes: {len(report['mistakes'])}")
    for mistake in report["mistakes"]:
        print(f"  {mistake}")


def evaluate_model(
    model_dir: Path,
    taxonomy: Dict[str, Any],
    listings: List[LabelledListing],
    device: str,
    batch_size: int,
    ensemble_loader: EnsembleLoader,
) -> Dict[str, Any]:
    questions = build_questions(taxonomy)
    states = [normalize_state(listing.state) for listing in listings]

    ensemble = ensemble_loader(model_dir.name, model_dir, device)
    predictions = ensemble.predict(states, questions, batch_size=batch_size)

    return score_predictions(taxonomy, listings, predictions)


def parse_args(argv: List[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="python -m mmclassifier.evaluate")
    parser.add_argument("--model-dir", required=True, type=Path)
    parser.add_argument("--taxonomy", required=True, type=Path)
    parser.add_argument("--test", required=True)
    parser.add_argument("--labels-format", choices=["pilot", "export"], default="pilot")
    parser.add_argument("--device", default=None)
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--out", type=Path, default=None)
    return parser.parse_args(argv)


def run(args: argparse.Namespace) -> Dict[str, Any]:
    import torch

    from mmclassifier.models import load_laya_ensemble

    device = args.device or ("cuda" if torch.cuda.is_available() else "cpu")

    taxonomy = load_taxonomy(args.taxonomy)
    listings = load_labels(args.test, args.labels_format)
    report = evaluate_model(args.model_dir, taxonomy, listings, device, args.batch_size, load_laya_ensemble)

    out_path = args.out or (args.model_dir / "evaluation.json")
    out_path.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print_report(report)
    print(f"wrote {out_path}")
    return report


def main(argv: List[str] | None = None) -> None:
    args = parse_args(argv)
    run(args)


if __name__ == "__main__":
    main()
