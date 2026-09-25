from __future__ import annotations

import random

from mmclassifier.dataset import build_dataset, sample_replay_rows, split_listings
from mmclassifier.rows import LabelledListing

TAXONOMY = {
    "questions": {
        "item_type": {
            "instructions": "What is this?",
            "criteria": {"phone": "A phone.", "other": "Anything else."},
        },
    }
}


def _listings(count: int) -> list[LabelledListing]:
    return [
        LabelledListing(
            id=f"m{index}",
            state={"title": f"t{index}", "category": "c", "brand": "b", "description": None},
            answers={"item_type": "phone"},
        )
        for index in range(count)
    ]


def test_split_is_deterministic_for_a_fixed_seed():
    listings = _listings(20)

    train_a, val_a = split_listings(listings, val_count=5, rng=random.Random(5))
    train_b, val_b = split_listings(listings, val_count=5, rng=random.Random(5))

    assert [listing.id for listing in train_a] == [listing.id for listing in train_b]
    assert [listing.id for listing in val_a] == [listing.id for listing in val_b]


def test_split_produces_no_overlap_and_covers_all_listings():
    listings = _listings(20)

    train, val = split_listings(listings, val_count=5, rng=random.Random(5))

    assert len(val) == 5
    assert len(train) == 15
    assert {listing.id for listing in train} & {listing.id for listing in val} == set()
    assert {listing.id for listing in train} | {listing.id for listing in val} == {
        listing.id for listing in listings
    }


def test_split_is_independent_of_input_order():
    listings = _listings(20)
    shuffled = list(reversed(listings))

    train_a, val_a = split_listings(listings, val_count=5, rng=random.Random(5))
    train_b, val_b = split_listings(shuffled, val_count=5, rng=random.Random(5))

    assert [listing.id for listing in train_a] == [listing.id for listing in train_b]
    assert [listing.id for listing in val_a] == [listing.id for listing in val_b]


def test_sample_replay_rows_caps_at_pool_size():
    pool = [{"row": index} for index in range(10)]

    sampled = sample_replay_rows(pool, count=50, rng=random.Random(1))

    assert sampled == pool


def test_sample_replay_rows_respects_requested_count():
    pool = [{"row": index} for index in range(100)]

    sampled = sample_replay_rows(pool, count=30, rng=random.Random(1))

    assert len(sampled) == 30
    assert all(row in pool for row in sampled)


def test_build_dataset_mixes_family_rows_and_replay_at_requested_counts():
    listings = _listings(50)
    replay_pool = [{"src": "replay", "state": {}, "q": {}, "gold": "x"} for _ in range(5000)]

    result = build_dataset(
        taxonomy=TAXONOMY,
        listings=listings,
        replay_pool=replay_pool,
        val_listings=10,
        replay_count=3000,
    )

    assert result.listing_count == 50
    assert result.val_listing_count == 10
    assert result.family_row_count == 40
    assert result.replay_row_count == 3000
    assert len(result.train_rows) == 40 * 2 + 3000
    assert len(result.val_rows) == 10


def test_build_dataset_is_deterministic_for_the_same_seed():
    listings = _listings(50)
    replay_pool = [{"src": "replay", "state": {}, "q": {}, "gold": str(i)} for i in range(5000)]

    result_a = build_dataset(
        taxonomy=TAXONOMY, listings=listings, replay_pool=replay_pool, val_listings=10, replay_count=3000, seed=5
    )
    result_b = build_dataset(
        taxonomy=TAXONOMY, listings=listings, replay_pool=replay_pool, val_listings=10, replay_count=3000, seed=5
    )

    assert result_a.train_rows == result_b.train_rows
    assert result_a.val_rows == result_b.val_rows


def test_build_dataset_replay_pool_smaller_than_requested_count_uses_whole_pool():
    listings = _listings(50)
    replay_pool = [{"src": "replay", "state": {}, "q": {}, "gold": str(i)} for i in range(100)]

    result = build_dataset(
        taxonomy=TAXONOMY, listings=listings, replay_pool=replay_pool, val_listings=10, replay_count=3000
    )

    assert result.replay_row_count == 100
