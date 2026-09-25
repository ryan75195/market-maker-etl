from __future__ import annotations


def test_health_reports_device_and_not_yet_loaded_models(make_client):
    client = make_client({"iphone-15": []}, device="cpu")

    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["device"] == "cpu"
    assert body["models"] == {}


def test_health_reports_loaded_models_after_classify_call(make_client, sample_questions, fixed_choice_seed):
    client = make_client(
        {
            "iphone-15": [
                fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0}),
                fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0}),
            ]
        }
    )

    client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
    )

    response = client.get("/health")

    assert response.json()["models"] == {"iphone-15": 2}
