from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any, Dict, List, Sequence

from mmclassifier.dataset import DEFAULT_SPLIT_SEED, BuildResult, build_dataset
from mmclassifier.labels import load_label_sources
from mmclassifier.taxonomy import load_taxonomy


def load_replay_pool(path: Path) -> List[Dict[str, Any]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def write_jsonl(path: Path, rows: Sequence[Dict[str, Any]]) -> None:
    with path.open("w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False) + "\n")


def parse_args(argv: List[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="python -m mmclassifier.build")
    parser.add_argument("--taxonomy", required=True, type=Path)
    parser.add_argument(
        "--labels",
        action="append",
        required=True,
        metavar="FORMAT:PATTERN",
        help="Repeatable. '<format>:<pattern>', format is 'pilot' or 'export'. Merged and "
        "de-duplicated by listing id; later sources win.",
    )
    parser.add_argument("--replay", required=True, type=Path)
    parser.add_argument("--replay-count", type=int, default=3000)
    parser.add_argument("--val-listings", type=int, default=40)
    parser.add_argument("--seed", type=int, default=DEFAULT_SPLIT_SEED)
    parser.add_argument("--out", required=True, type=Path)
    return parser.parse_args(argv)


def run(args: argparse.Namespace) -> BuildResult:
    taxonomy = load_taxonomy(args.taxonomy)
    listings = load_label_sources(args.labels)
    replay_pool = load_replay_pool(args.replay)

    result = build_dataset(
        taxonomy=taxonomy,
        listings=listings,
        replay_pool=replay_pool,
        val_listings=args.val_listings,
        replay_count=args.replay_count,
        seed=args.seed,
    )

    args.out.mkdir(parents=True, exist_ok=True)
    write_jsonl(args.out / "train.jsonl", result.train_rows)
    write_jsonl(args.out / "val.jsonl", result.val_rows)
    return result


def main(argv: List[str] | None = None) -> None:
    args = parse_args(argv)
    result = run(args)
    print(
        f"listings {result.listing_count} (val {result.val_listing_count}) "
        f"family rows {result.family_row_count} replay {result.replay_row_count} "
        f"train {len(result.train_rows)} val {len(result.val_rows)}"
    )


if __name__ == "__main__":
    main()
