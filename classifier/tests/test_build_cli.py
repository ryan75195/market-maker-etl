from __future__ import annotations

import json
from pathlib import Path

from mmclassifier.build import load_replay_pool, parse_args, run, write_jsonl


def _write_json(path: Path, data) -> None:
    path.write_text(json.dumps(data), encoding="utf-8")


def _write_jsonl(path: Path, rows) -> None:
    path.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")


def test_run_writes_train_and_val_jsonl(tmp_path: Path):
    taxonomy = {
        "questions": {
            "item_type": {
                "instructions": "What is this?",
                "criteria": {"phone": "A phone.", "other": "Anything else."},
            }
        }
    }
    _write_json(tmp_path / "taxonomy.json", taxonomy)

    pool = [
        {"id": f"m{i}", "title": f"t{i}", "category": "c", "brand": "b", "description": "d"} for i in range(20)
    ]
    labels = [{"id": f"m{i}", "item_type": "phone", "ambiguous": ""} for i in range(20)]
    _write_json(tmp_path / "pool.json", pool)
    _write_json(tmp_path / "labels_0.json", labels)

    replay_rows = [{"src": "replay", "state": {}, "q": {}, "gold": str(i)} for i in range(50)]
    _write_jsonl(tmp_path / "replay.jsonl", replay_rows)

    out_dir = tmp_path / "out"
    args = parse_args(
        [
            "--taxonomy",
            str(tmp_path / "taxonomy.json"),
            "--labels",
            f"pilot:{tmp_path / 'labels_*.json'}",
            "--replay",
            str(tmp_path / "replay.jsonl"),
            "--replay-count",
            "10",
            "--val-listings",
            "5",
            "--out",
            str(out_dir),
        ]
    )

    result = run(args)

    assert (out_dir / "train.jsonl").exists()
    assert (out_dir / "val.jsonl").exists()

    train_lines = (out_dir / "train.jsonl").read_text(encoding="utf-8").splitlines()
    val_lines = (out_dir / "val.jsonl").read_text(encoding="utf-8").splitlines()

    assert len(train_lines) == len(result.train_rows)
    assert len(val_lines) == len(result.val_rows)
    assert result.val_listing_count == 5
    assert result.family_row_count == 15
    assert result.replay_row_count == 10


def test_run_merges_multiple_label_sources_and_later_source_wins(tmp_path: Path):
    taxonomy = {
        "questions": {
            "item_type": {
                "instructions": "What is this?",
                "criteria": {"phone": "A phone.", "case": "A case."},
            }
        }
    }
    _write_json(tmp_path / "taxonomy.json", taxonomy)

    pool = [{"id": "m1", "title": "t1", "category": "c", "brand": "b", "description": "d"}]
    pilot_labels = [{"id": "m1", "item_type": "phone", "ambiguous": ""}]
    _write_json(tmp_path / "pool.json", pool)
    _write_json(tmp_path / "labels_0.json", pilot_labels)

    export_path = tmp_path / "export.jsonl"
    export_path.write_text(
        json.dumps(
            {
                "listingId": "m1",
                "taxonomyVersion": 1,
                "state": {"title": "t1", "mercari_category": "c", "brand": "b", "description": "d"},
                "answers": {"item_type": "case"},
            }
        ),
        encoding="utf-8",
    )

    _write_jsonl(tmp_path / "replay.jsonl", [])

    out_dir = tmp_path / "out"
    args = parse_args(
        [
            "--taxonomy",
            str(tmp_path / "taxonomy.json"),
            "--labels",
            f"pilot:{tmp_path / 'labels_*.json'}",
            "--labels",
            f"export:{export_path}",
            "--replay",
            str(tmp_path / "replay.jsonl"),
            "--val-listings",
            "0",
            "--out",
            str(out_dir),
        ]
    )

    result = run(args)

    assert result.listing_count == 1
    assert result.family_row_count == 1
    train_rows = [json.loads(line) for line in (out_dir / "train.jsonl").read_text(encoding="utf-8").splitlines()]
    assert all(row["gold"] == "case" for row in train_rows)


def test_load_replay_pool_reads_jsonl(tmp_path: Path):
    path = tmp_path / "replay.jsonl"
    rows = [{"src": "a"}, {"src": "b"}]
    _write_jsonl(path, rows)

    loaded = load_replay_pool(path)

    assert loaded == rows


def test_write_jsonl_round_trips(tmp_path: Path):
    path = tmp_path / "out.jsonl"
    rows = [{"a": 1}, {"a": 2}]

    write_jsonl(path, rows)

    loaded = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]
    assert loaded == rows
