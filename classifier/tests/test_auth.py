from __future__ import annotations


def test_classify_without_api_key_configured_requires_no_auth(make_client, sample_questions, fixed_choice_seed):
    client = make_client({"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]}, api_key=None)

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
    )

    assert response.status_code == 200


def test_classify_with_api_key_configured_rejects_missing_bearer(
    make_client, sample_questions, fixed_choice_seed
):
    client = make_client(
        {"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]}, api_key="secret-token"
    )

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
    )

    assert response.status_code == 401


def test_classify_with_api_key_configured_rejects_wrong_bearer(
    make_client, sample_questions, fixed_choice_seed
):
    client = make_client(
        {"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]}, api_key="secret-token"
    )

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
        headers={"Authorization": "Bearer wrong-token"},
    )

    assert response.status_code == 401


def test_classify_with_api_key_configured_accepts_correct_bearer(
    make_client, sample_questions, fixed_choice_seed
):
    client = make_client(
        {"iphone-15": [fixed_choice_seed("other", {"other": 1.0})]}, api_key="secret-token"
    )

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
        headers={"Authorization": "Bearer secret-token"},
    )

    assert response.status_code == 200


def test_health_is_reachable_without_auth_even_when_api_key_configured(make_client):
    client = make_client({}, api_key="secret-token")

    response = client.get("/health")

    assert response.status_code == 200
