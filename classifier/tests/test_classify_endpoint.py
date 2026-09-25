from __future__ import annotations

import pytest


def test_classify_returns_averaged_answers_in_state_order(make_client, sample_questions, fixed_choice_seed):
    client = make_client(
        {
            "iphone-15": [
                fixed_choice_seed("iphone_15_family", {"iphone_15_family": 0.9, "other": 0.1}),
                fixed_choice_seed("iphone_15_family", {"iphone_15_family": 0.8, "other": 0.2}),
            ]
        }
    )

    response = client.post(
        "/v1/classify",
        json={
            "model": "iphone-15",
            "questions": sample_questions,
            "states": [{"title": "first"}, {"title": "second"}],
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "iphone-15"
    assert body["members"] == 2
    assert len(body["results"]) == 2

    for result in body["results"]:
        answer = result["answers"]["item_type"]
        assert answer["choice"] == "iphone_15_family"
        assert answer["confidence"] == pytest.approx(0.85)
        assert answer["agreement"] == pytest.approx(1.0)


def test_classify_result_order_matches_state_order(make_client, sample_questions, per_state_seed):
    seed = per_state_seed(
        [
            {"item_type": {"choice": "iphone_15_family", "probabilities": {"iphone_15_family": 0.9, "other": 0.1}}},
            {"item_type": {"choice": "other", "probabilities": {"iphone_15_family": 0.1, "other": 0.9}}},
        ]
    )
    client = make_client({"iphone-15": [seed]})

    response = client.post(
        "/v1/classify",
        json={
            "model": "iphone-15",
            "questions": sample_questions,
            "states": [{"title": "phone listing"}, {"title": "unrelated listing"}],
        },
    )

    body = response.json()
    assert body["results"][0]["answers"]["item_type"]["choice"] == "iphone_15_family"
    assert body["results"][1]["answers"]["item_type"]["choice"] == "other"
