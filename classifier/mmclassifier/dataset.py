from __future__ import annotations

import random
from dataclasses import dataclass
from typing import Any, Dict, List, Sequence, Tuple

from mmclassifier.rows import LabelledListing, build_rows

DEFAULT_SPLIT_SEED = 5
DEFAULT_FAMILY_REPEATS = 2


def split_listings(
    listings: Sequence[LabelledListing],
    val_count: int,
    rng: random.Random,
) -> Tuple[List[LabelledListing], List[LabelledListing]]:
    ordered = sorted(listings, key=lambda listing: listing.id)
    rng.shuffle(ordered)
    val_listings = ordered[:val_count]
    train_listings = ordered[val_count:]
    return train_listings, val_listings


def rows_for_listings(taxonomy: Dict[str, Any], listings: Sequence[LabelledListing]) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    for listing in listings:
        rows.extend(build_rows(taxonomy, listing))
    return rows


def sample_replay_rows(
    replay_pool: Sequence[Dict[str, Any]],
    count: int,
    rng: random.Random,
) -> List[Dict[str, Any]]:
    pool = list(replay_pool)
    if count >= len(pool):
        return pool
    return rng.sample(pool, count)


@dataclass
class BuildResult:
    train_rows: List[Dict[str, Any]]
    val_rows: List[Dict[str, Any]]
    listing_count: int
    val_listing_count: int
    family_row_count: int
    replay_row_count: int


def build_dataset(
    taxonomy: Dict[str, Any],
    listings: Sequence[LabelledListing],
    replay_pool: Sequence[Dict[str, Any]],
    val_listings: int,
    replay_count: int,
    seed: int = DEFAULT_SPLIT_SEED,
    family_repeats: int = DEFAULT_FAMILY_REPEATS,
) -> BuildResult:
    rng = random.Random(seed)

    train_listings, val_listings_selected = split_listings(listings, val_listings, rng)

    family_train_rows = rows_for_listings(taxonomy, train_listings)
    val_rows = rows_for_listings(taxonomy, val_listings_selected)

    replay_sample = sample_replay_rows(replay_pool, replay_count, rng)

    train_rows = family_train_rows * family_repeats + replay_sample
    rng.shuffle(train_rows)

    return BuildResult(
        train_rows=train_rows,
        val_rows=val_rows,
        listing_count=len(listings),
        val_listing_count=len(val_listings_selected),
        family_row_count=len(family_train_rows),
        replay_row_count=len(replay_sample),
    )
