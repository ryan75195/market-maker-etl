from __future__ import annotations

import json
from pathlib import Path

import pytest

from mmclassifier.train import (
    compute_step_count,
    linear_warmup_decay,
    load_jsonl,
    parse_args,
    parse_seed_list,
)


def test_parse_seed_list_splits_on_commas():
    assert parse_seed_list("7,11,13") == [7, 11, 13]


def test_parse_seed_list_ignores_blank_entries():
    assert parse_seed_list("7,,11,") == [7, 11]


def test_compute_step_count_rounds_up_full_epochs():
    assert compute_step_count(train_size=100, epochs=3, batch_size=16) == 21


def test_compute_step_count_scales_with_fractional_epochs():
    assert compute_step_count(train_size=160, epochs=0.5, batch_size=16) == 5


def test_linear_warmup_decay_increases_during_warmup():
    factor_start = linear_warmup_decay(0, 100, 10)
    factor_end_of_warmup = linear_warmup_decay(9, 100, 10)

    assert factor_end_of_warmup > factor_start


def test_linear_warmup_decay_decreases_after_warmup():
    factor_mid = linear_warmup_decay(50, 100, 10)
    factor_late = linear_warmup_decay(90, 100, 10)

    assert factor_late < factor_mid


def test_linear_warmup_decay_approaches_zero_at_final_step():
    assert linear_warmup_decay(99, 100, 10) < 0.05


def test_load_jsonl_skips_blank_lines(tmp_path: Path):
    path = tmp_path / "rows.jsonl"
    path.write_text('{"a": 1}\n\n{"a": 2}\n', encoding="utf-8")

    rows = load_jsonl(path)

    assert rows == [{"a": 1}, {"a": 2}]


def test_parse_args_defaults_match_pilot_recipe():
    args = parse_args(["--init", "init.pt", "--data", "data", "--out", "out"])

    assert args.seeds == "7,11,13"
    assert args.epochs == 3
