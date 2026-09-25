from __future__ import annotations


def test_unknown_model_returns_404(make_client, sample_questions, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]})

    response = client.post(
        "/v1/classify",
        json={"model": "ps5-controller", "questions": sample_questions, "states": [{"title": "x"}]},
    )

    assert response.status_code == 404


def test_too_many_states_returns_413(make_client, sample_questions, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]})

    response = client.post(
        "/v1/classify",
        json={
            "model": "iphone-15",
            "questions": sample_questions,
            "states": [{"title": "x"}] * 257,
        },
    )

    assert response.status_code == 413


def test_too_many_questions_returns_413(make_client, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]})

    questions = {
        f"question_{index}": {
            "type": "choice",
            "instructions": "pick one",
            "criteria": {"a": "A", "b": "B"},
        }
        for index in range(33)
    }

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": questions, "states": [{"title": "x"}]},
    )

    assert response.status_code == 413


def test_oversized_state_returns_413(make_client, sample_questions, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]})

    response = client.post(
        "/v1/classify",
        json={
            "model": "iphone-15",
            "questions": sample_questions,
            "states": [{"description": "x" * 50_001}],
        },
    )

    assert response.status_code == 413


def test_question_type_must_be_choice_returns_422(make_client, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]})

    response = client.post(
        "/v1/classify",
        json={
            "model": "iphone-15",
            "questions": {
                "item_type": {
                    "type": "score",
                    "instructions": "pick one",
                    "criteria": {"a": "A", "b": "B"},
                }
            },
            "states": [{"title": "x"}],
        },
    )

    assert response.status_code == 422


def test_question_needs_at_least_two_criteria_returns_422(make_client, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]})

    response = client.post(
        "/v1/classify",
        json={
            "model": "iphone-15",
            "questions": {
                "item_type": {
                    "type": "choice",
                    "instructions": "pick one",
                    "criteria": {"a": "A"},
                }
            },
            "states": [{"title": "x"}],
        },
    )

    assert response.status_code == 422
