from __future__ import annotations

import pytest

from mmclassifier.ensemble import aggregate_ensemble

QUESTIONS = {
    "item_type": {
        "type": "choice",
        "instructions": "What is this?",
        "criteria": {"a": "Option A.", "b": "Option B."},
    }
}


def test_averages_probabilities_across_seeds(per_state_seed):
    seeds = [
        per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 0.9, "b": 0.1}}}]),
        per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 0.6, "b": 0.4}}}]),
        per_state_seed([{"item_type": {"choice": "b", "probabilities": {"a": 0.3, "b": 0.7}}}]),
    ]

    results = aggregate_ensemble(seeds, [{"title": "x"}], QUESTIONS)

    answer = results[0]["answers"]["item_type"]
    assert answer["probabilities"]["a"] == pytest.approx(0.6)
    assert answer["probabilities"]["b"] == pytest.approx(0.4)
    assert answer["choice"] == "a"
    assert answer["confidence"] == pytest.approx(0.6)


def test_agreement_is_fraction_of_seeds_matching_ensemble_choice(per_state_seed):
    seeds = [
        per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 0.9, "b": 0.1}}}]),
        per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 0.8, "b": 0.2}}}]),
        per_state_seed([{"item_type": {"choice": "b", "probabilities": {"a": 0.2, "b": 0.8}}}]),
    ]

    results = aggregate_ensemble(seeds, [{"title": "x"}], QUESTIONS)

    answer = results[0]["answers"]["item_type"]
    assert answer["choice"] == "a"
    assert answer["agreement"] == pytest.approx(2 / 3)


def test_full_agreement_when_all_seeds_match(per_state_seed):
    seeds = [
        per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 1.0, "b": 0.0}}}]),
        per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 1.0, "b": 0.0}}}]),
    ]

    results = aggregate_ensemble(seeds, [{"title": "x"}], QUESTIONS)

    assert results[0]["answers"]["item_type"]["agreement"] == pytest.approx(1.0)


def test_result_order_matches_state_order(per_state_seed):
    seeds = [
        per_state_seed(
            [
                {"item_type": {"choice": "a", "probabilities": {"a": 0.9, "b": 0.1}}},
                {"item_type": {"choice": "b", "probabilities": {"a": 0.2, "b": 0.8}}},
            ]
        )
    ]

    results = aggregate_ensemble(seeds, [{"title": "first"}, {"title": "second"}], QUESTIONS)

    assert results[0]["answers"]["item_type"]["choice"] == "a"
    assert results[1]["answers"]["item_type"]["choice"] == "b"


def test_each_seed_receives_the_same_questions_and_states(per_state_seed):
    seed_a = per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 1.0, "b": 0.0}}}])
    seed_b = per_state_seed([{"item_type": {"choice": "a", "probabilities": {"a": 1.0, "b": 0.0}}}])

    states = [{"title": "x"}]
    aggregate_ensemble([seed_a, seed_b], states, QUESTIONS)

    assert seed_a.calls[0][0] == states
    assert seed_b.calls[0][0] == states
    assert seed_a.calls[0][1] == QUESTIONS
