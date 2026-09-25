from __future__ import annotations

from typing import Any, Dict, Iterable, List, Protocol, Sequence


class SeedPredictor(Protocol):
    def predict_batch(
        self,
        states: Sequence[Any],
        questions: Dict[str, Any],
        batch_size: int | None = None,
    ) -> List[Dict[str, Any]]: ...


def aggregate_ensemble(
    seeds: Sequence[SeedPredictor],
    states: Sequence[Any],
    questions: Dict[str, Any],
    batch_size: int | None = None,
) -> List[Dict[str, Dict[str, Dict[str, Any]]]]:
    per_seed_results = [
        seed.predict_batch(list(states), questions, batch_size=batch_size) for seed in seeds
    ]

    aggregated: List[Dict[str, Dict[str, Dict[str, Any]]]] = []
    for state_index in range(len(states)):
        answers = {
            question_id: aggregate_answer(
                seed_result[state_index]["answers"][question_id] for seed_result in per_seed_results
            )
            for question_id in questions
        }
        aggregated.append({"answers": answers})
    return aggregated


def aggregate_answer(seed_answers: Iterable[Dict[str, Any]]) -> Dict[str, Any]:
    seed_answers = list(seed_answers)
    member_count = len(seed_answers)

    totals: Dict[str, float] = {}
    for answer in seed_answers:
        for option, probability in answer["probabilities"].items():
            totals[option] = totals.get(option, 0.0) + probability
    averaged_probabilities = {option: total / member_count for option, total in totals.items()}

    choice = max(averaged_probabilities, key=averaged_probabilities.get)
    confidence = averaged_probabilities[choice]
    agreement = sum(1 for answer in seed_answers if answer["choice"] == choice) / member_count

    return {
        "choice": choice,
        "confidence": confidence,
        "agreement": agreement,
        "probabilities": averaged_probabilities,
    }
