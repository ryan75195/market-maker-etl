from __future__ import annotations

import pytest

from mmclassifier.evaluate import build_questions, score_predictions
from mmclassifier.rows import LabelledListing

TAXONOMY = {
    "questions": {
        "item_type": {
            "instructions": "What is this?",
            "criteria": {"phone": "A phone.", "other": "Anything else."},
        },
        "storage": {
            "instructions": "How much storage?",
            "criteria": {"gb_128": "128GB.", "gb_256": "256GB.", "not_applicable": "N/A."},
            "askWhen": [{"question": "item_type", "anyOf": ["phone"]}],
        },
    }
}


def _listing(listing_id: str, item_type: str, storage: str) -> LabelledListing:
    return LabelledListing(
        id=listing_id,
        state={"title": "t", "category": "c", "brand": "b", "description": None},
        answers={"item_type": item_type, "storage": storage},
    )


def _prediction(item_type_choice: str, item_type_conf: float, storage_choice: str, storage_conf: float):
    return {
        "answers": {
            "item_type": {"choice": item_type_choice, "confidence": item_type_conf, "probabilities": {}},
            "storage": {"choice": storage_choice, "confidence": storage_conf, "probabilities": {}},
        }
    }


def test_build_questions_forces_choice_type_for_every_question():
    questions = build_questions(TAXONOMY)

    assert set(questions.keys()) == {"item_type", "storage"}
    assert all(question["type"] == "choice" for question in questions.values())


def test_storage_is_scored_only_when_gold_item_type_makes_it_applicable():
    listings = [
        _listing("m1", "phone", "gb_128"),
        _listing("m2", "other", "not_applicable"),
    ]
    predictions = [
        _prediction("phone", 0.95, "gb_128", 0.95),
        _prediction("other", 0.95, "gb_128", 0.95),
    ]

    report = score_predictions(TAXONOMY, listings, predictions)

    assert report["per_question_accuracy"]["item_type"] == 1.0
    assert report["per_question_accuracy"]["storage"] == 1.0
    assert report["scored_answers"] == 3


def test_wrong_prediction_on_inapplicable_question_is_not_scored():
    listings = [_listing("m1", "other", "not_applicable")]
    predictions = [_prediction("other", 0.95, "gb_256", 0.4)]

    report = score_predictions(TAXONOMY, listings, predictions)

    assert "storage" not in report["per_question_accuracy"]
    assert report["scored_answers"] == 1
    assert report["mistakes"] == []


def test_confidence_split_buckets_at_point_nine():
    listings = [_listing("m1", "phone", "gb_128"), _listing("m2", "phone", "gb_256")]
    predictions = [
        _prediction("phone", 0.95, "gb_128", 0.95),
        _prediction("phone", 0.5, "gb_128", 0.5),
    ]

    report = score_predictions(TAXONOMY, listings, predictions)

    assert report["confidence_split"]["high"]["count"] == 2
    assert report["confidence_split"]["high"]["accuracy"] == 1.0
    assert report["confidence_split"]["low"]["count"] == 2
    assert report["confidence_split"]["low"]["accuracy"] == 0.5
    assert report["flagged_fraction"] == pytest.approx(0.5)


def test_mistakes_list_records_gold_and_predicted_choice():
    listings = [_listing("m1", "phone", "gb_128")]
    predictions = [_prediction("phone", 0.9, "gb_256", 0.6)]

    report = score_predictions(TAXONOMY, listings, predictions)

    assert report["mistakes"] == [
        {
            "listing_id": "m1",
            "question": "storage",
            "gold": "gb_128",
            "choice": "gb_256",
            "confidence": 0.6,
        }
    ]


def test_no_scored_answers_yields_zero_flagged_fraction():
    report = score_predictions(TAXONOMY, [], [])

    assert report["scored_answers"] == 0
    assert report["flagged_fraction"] == 0.0
    assert report["confidence_split"]["high"]["accuracy"] is None
    assert report["confidence_split"]["low"]["accuracy"] is None
