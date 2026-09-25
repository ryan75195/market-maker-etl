from __future__ import annotations

import pytest


def test_systemone_response_shape(make_client, sample_questions, fixed_choice_seed):
    client = make_client(
        {
            "iphone-15": [
                fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0}),
            ]
        }
    )

    response = client.post(
        "/v1/systemone",
        json={
            "model": "iphone-15",
            "state": {"title": "iPhone 15 Pro Max"},
            "questions": sample_questions,
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "iphone-15"
    assert body["usage"] == {"input_tokens": 0, "output_tokens": 0}

    answer = body["answers"]["item_type"]
    assert answer["type"] == "choice"
    assert answer["choice"] == "iphone_15_family"
    assert answer["confidence"] == pytest.approx(1.0)
    assert "agreement" not in answer
    assert answer["probabilities"] == {"iphone_15_family": 1.0, "other": 0.0}
