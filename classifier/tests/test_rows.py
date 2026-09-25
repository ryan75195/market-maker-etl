from __future__ import annotations

from mmclassifier.rows import LabelledListing, build_rows, normalize_state

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


def _listing(answers: dict) -> LabelledListing:
    return LabelledListing(
        id="m1",
        state={"title": "t", "category": "c", "brand": "b", "description": "d"},
        answers=answers,
    )


def test_gated_question_included_when_askwhen_holds():
    listing = _listing({"item_type": "phone", "storage": "gb_128", "colour": "black"})

    rows = build_rows(TAXONOMY, listing)

    srcs = {row["src"] for row in rows}
    assert srcs == {"item_type", "storage", "colour"}
    colour_row = next(row for row in rows if row["src"] == "colour")
    assert colour_row["gold"] == "black"


def test_gated_question_dropped_when_askwhen_fails():
    listing = _listing({"item_type": "other", "storage": "not_applicable", "colour": "not_applicable"})

    rows = build_rows(TAXONOMY, listing)

    srcs = {row["src"] for row in rows}
    assert srcs == {"item_type"}


def test_chained_askwhen_requires_all_clauses():
    listing = _listing({"item_type": "phone", "storage": "not_stated", "colour": "black"})

    rows = build_rows(TAXONOMY, listing)

    srcs = {row["src"] for row in rows}
    assert "storage" in srcs
    assert "colour" not in srcs


def test_non_option_gold_is_dropped():
    listing = _listing({"item_type": "not_applicable", "storage": "gb_128", "colour": "black"})

    rows = build_rows(TAXONOMY, listing)

    assert rows == []


def test_row_shape_has_src_state_q_gold():
    listing = _listing({"item_type": "phone", "storage": "gb_128", "colour": "black"})

    rows = build_rows(TAXONOMY, listing)
    item_type_row = next(row for row in rows if row["src"] == "item_type")

    assert set(item_type_row.keys()) == {"src", "state", "q", "gold"}
    assert item_type_row["q"] == {
        "type": "choice",
        "instructions": "What is this?",
        "criteria": {"phone": "A phone.", "other": "Anything else."},
    }


def test_normalize_state_maps_category_and_truncates_description():
    raw = {"title": "t", "category": "c", "brand": "b", "description": "x" * 2000}

    state = normalize_state(raw)

    assert state == {
        "title": "t",
        "mercari_category": "c",
        "brand": "b",
        "description": "x" * 1200,
    }


def test_normalize_state_turns_empty_description_into_none():
    raw = {"title": "t", "category": "c", "brand": "b", "description": ""}

    state = normalize_state(raw)

    assert state["description"] is None


def test_normalize_state_prefers_existing_mercari_category_key():
    raw = {"title": "t", "mercari_category": "already-normalized", "brand": "b", "description": None}

    state = normalize_state(raw)

    assert state["mercari_category"] == "already-normalized"
