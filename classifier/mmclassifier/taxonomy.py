from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List, Union


def load_taxonomy(path: Union[str, Path]) -> Dict[str, Any]:
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def all_question_names(taxonomy: Dict[str, Any]) -> List[str]:
    return list(taxonomy["questions"].keys())


def question_spec(taxonomy: Dict[str, Any], name: str) -> Dict[str, Any]:
    question = taxonomy["questions"][name]
    return {
        "type": "choice",
        "instructions": question["instructions"],
        "criteria": question["criteria"],
    }


def is_applicable(taxonomy: Dict[str, Any], name: str, gold_answers: Dict[str, str]) -> bool:
    clauses = taxonomy["questions"][name].get("askWhen")
    if not clauses:
        return True
    for clause in clauses:
        referenced_answer = gold_answers.get(clause["question"])
        if referenced_answer not in clause["anyOf"]:
            return False
    return True
