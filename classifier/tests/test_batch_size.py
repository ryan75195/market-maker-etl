from __future__ import annotations


def test_classify_passes_default_batch_size_to_predictor(make_client, sample_questions, fixed_choice_seed):
    seed = fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0})
    client = make_client({"iphone-15": [seed]})

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
    )

    assert response.status_code == 200
    [(_, _, batch_size)] = seed.calls
    assert batch_size == 16


def test_classify_passes_configured_batch_size_to_predictor(make_client, sample_questions, fixed_choice_seed):
    seed = fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0})
    client = make_client({"iphone-15": [seed]}, batch_size=4)

    response = client.post(
        "/v1/classify",
        json={"model": "iphone-15", "questions": sample_questions, "states": [{"title": "x"}]},
    )

    assert response.status_code == 200
    [(_, _, batch_size)] = seed.calls
    assert batch_size == 4


def test_systemone_passes_configured_batch_size_to_predictor(make_client, sample_questions, fixed_choice_seed):
    seed = fixed_choice_seed("iphone_15_family", {"iphone_15_family": 1.0, "other": 0.0})
    client = make_client({"iphone-15": [seed]}, batch_size=8)

    response = client.post(
        "/v1/systemone",
        json={"model": "iphone-15", "state": {"title": "x"}, "questions": sample_questions},
    )

    assert response.status_code == 200
    [(_, _, batch_size)] = seed.calls
    assert batch_size == 8
