from __future__ import annotations

import json
from pathlib import Path

from mmclassifier.taxonomy import all_question_names, is_applicable, load_taxonomy, question_spec

TAXONOMY = {
    "questions": {
        "item_type": {
            "instructions": "What is this?",
            "criteria": {"phone": "A phone.", "other": "Anything else."},
        },
        "storage": {
            "instructions": "How much storage?",
            "criteria": {"gb_128": "128GB.", "not_stated": "Not stated."},
            "askWhen": [{"question": "item_type", "anyOf": ["phone"]}],
        },
        "colour": {
            "instructions": "What colour?",
            "criteria": {"black": "Black.", "white": "White."},
            "askWhen": [
                {"question": "item_type", "anyOf": ["phone"]},
                {"question": "storage", "anyOf": ["gb_128"]},
            ],
        },
    }
}


def test_all_question_names_preserves_taxonomy_order():
    assert all_question_names(TAXONOMY) == ["item_type", "storage", "colour"]


def test_question_spec_drops_askwhen_and_forces_choice_type():
    spec = question_spec(TAXONOMY, "storage")

    assert spec == {
        "type": "choice",
        "instructions": "How much storage?",
        "criteria": {"gb_128": "128GB.", "not_stated": "Not stated."},
    }


def test_question_without_askwhen_is_always_applicable():
    assert is_applicable(TAXONOMY, "item_type", {}) is True


def test_question_applicable_when_single_clause_holds():
    assert is_applicable(TAXONOMY, "storage", {"item_type": "phone"}) is True


def test_question_not_applicable_when_single_clause_fails():
    assert is_applicable(TAXONOMY, "storage", {"item_type": "other"}) is False


def test_question_needs_all_clauses_to_hold():
    assert is_applicable(TAXONOMY, "colour", {"item_type": "phone", "storage": "gb_128"}) is True
    assert is_applicable(TAXONOMY, "colour", {"item_type": "phone", "storage": "not_stated"}) is False


def test_missing_referenced_answer_is_not_applicable():
    assert is_applicable(TAXONOMY, "storage", {}) is False


def test_load_taxonomy_reads_utf8_sig_json(tmp_path: Path):
    path = tmp_path / "taxonomy.json"
    path.write_bytes(b"\xef\xbb\xbf" + json.dumps(TAXONOMY).encode("utf-8"))

    loaded = load_taxonomy(path)

    assert loaded == TAXONOMY
