from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List

from mmclassifier.taxonomy import all_question_names, is_applicable, question_spec

MAX_DESCRIPTION_CHARS = 1200


@dataclass
class LabelledListing:
    id: str
    state: Dict[str, Any]
    answers: Dict[str, str]


def normalize_state(raw_state: Dict[str, Any]) -> Dict[str, Any]:
    description = raw_state.get("description") or None
    if description is not None:
        description = description[:MAX_DESCRIPTION_CHARS]
    return {
        "title": raw_state.get("title"),
        "mercari_category": raw_state.get("mercari_category", raw_state.get("category")),
        "brand": raw_state.get("brand"),
        "description": description,
    }


def build_rows(taxonomy: Dict[str, Any], listing: LabelledListing) -> List[Dict[str, Any]]:
    state = normalize_state(listing.state)
    rows: List[Dict[str, Any]] = []
    for name in all_question_names(taxonomy):
        gold = listing.answers.get(name)
        criteria = taxonomy["questions"][name]["criteria"]
        if gold not in criteria:
            continue
        if not is_applicable(taxonomy, name, listing.answers):
            continue
        rows.append({"src": name, "state": state, "q": question_spec(taxonomy, name), "gold": gold})
    return rows
