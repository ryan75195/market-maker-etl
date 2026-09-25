from __future__ import annotations

import json
from pathlib import Path

import pytest

from mmclassifier.labels import load_export_labels, load_labels, load_pilot_labels


def _write_json(path: Path, data) -> None:
    path.write_text(json.dumps(data), encoding="utf-8")


def test_pilot_format_joins_labels_to_pool_by_id(tmp_path: Path):
    pool = [
        {"id": "m1", "title": "iPhone 15", "category": "cat", "brand": "Apple", "description": "d1"},
        {"id": "m2", "title": "Case", "category": "cat2", "brand": "", "description": ""},
    ]
    labels = [
        {"id": "m1", "item_type": "phone", "ambiguous": ""},
        {"id": "m2", "item_type": "case", "ambiguous": "guess"},
    ]
    _write_json(tmp_path / "pool.json", pool)
    _write_json(tmp_path / "labels_0.json", labels)

    listings = load_pilot_labels(str(tmp_path / "labels_*.json"))

    assert {listing.id for listing in listings} == {"m1", "m2"}
    m1 = next(listing for listing in listings if listing.id == "m1")
    assert m1.state == pool[0]
    assert m1.answers == {"item_type": "phone"}


def test_pilot_format_skips_labels_with_no_matching_pool_entry(tmp_path: Path):
    pool = [{"id": "m1", "title": "t", "category": "c", "brand": "b", "description": "d"}]
    labels = [
        {"id": "m1", "item_type": "phone", "ambiguous": ""},
        {"id": "missing", "item_type": "phone", "ambiguous": ""},
    ]
    _write_json(tmp_path / "pool.json", pool)
    _write_json(tmp_path / "labels_0.json", labels)

    listings = load_pilot_labels(str(tmp_path / "labels_*.json"))

    assert [listing.id for listing in listings] == ["m1"]


def test_pilot_format_merges_multiple_state_pool_files(tmp_path: Path):
    _write_json(
        tmp_path / "pool.json",
        [{"id": "m1", "title": "t1", "category": "c", "brand": "b", "description": "d"}],
    )
    _write_json(
        tmp_path / "test.json",
        [{"id": "m2", "title": "t2", "category": "c", "brand": "b", "description": "d"}],
    )
    _write_json(
        tmp_path / "test_labels_0.json",
        [
            {"id": "m1", "item_type": "phone", "ambiguous": ""},
            {"id": "m2", "item_type": "case", "ambiguous": ""},
        ],
    )

    listings = load_pilot_labels(str(tmp_path / "test_labels_*.json"))

    assert {listing.id for listing in listings} == {"m1", "m2"}


def test_export_format_reads_review_queue_jsonl(tmp_path: Path):
    path = tmp_path / "export.jsonl"
    lines = [
        {
            "listingId": "m1",
            "taxonomyVersion": 1,
            "state": {"title": "t", "mercari_category": "c", "brand": "b", "description": "d"},
            "answers": {"item_type": "phone"},
        },
        {
            "listingId": "m2",
            "taxonomyVersion": 1,
            "state": {"title": "t2", "mercari_category": "c2", "brand": "b2", "description": None},
            "answers": {"item_type": "case"},
        },
    ]
    path.write_text("\n".join(json.dumps(line) for line in lines), encoding="utf-8")

    listings = load_export_labels(str(path))

    assert [listing.id for listing in listings] == ["m1", "m2"]
    assert listings[0].state == lines[0]["state"]
    assert listings[0].answers == {"item_type": "phone"}


def test_export_format_skips_blank_lines(tmp_path: Path):
    path = tmp_path / "export.jsonl"
    record = {
        "listingId": "m1",
        "taxonomyVersion": 1,
        "state": {"title": "t", "mercari_category": "c", "brand": "b", "description": "d"},
        "answers": {"item_type": "phone"},
    }
    path.write_text(f"\n{json.dumps(record)}\n\n", encoding="utf-8")

    listings = load_export_labels(str(path))

    assert len(listings) == 1


def test_load_labels_dispatches_on_format(tmp_path: Path):
    _write_json(
        tmp_path / "pool.json",
        [{"id": "m1", "title": "t", "category": "c", "brand": "b", "description": "d"}],
    )
    _write_json(tmp_path / "labels_0.json", [{"id": "m1", "item_type": "phone", "ambiguous": ""}])

    listings = load_labels(str(tmp_path / "labels_*.json"), "pilot")

    assert len(listings) == 1


def test_load_labels_rejects_unknown_format():
    with pytest.raises(ValueError):
        load_labels("anything", "unknown-format")


def test_missing_glob_match_raises(tmp_path: Path):
    with pytest.raises(FileNotFoundError):
        load_pilot_labels(str(tmp_path / "nope_*.json"))
