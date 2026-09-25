from __future__ import annotations

import glob as glob_module
import json
from pathlib import Path
from typing import Any, Callable, Dict, List

from mmclassifier.rows import LabelledListing

ANSWER_EXCLUDED_KEYS = {"id", "ambiguous"}


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def _expand_glob(pattern: str) -> List[Path]:
    matches = sorted(glob_module.glob(pattern))
    if not matches:
        raise FileNotFoundError(f"No label files matched: {pattern}")
    return [Path(match) for match in matches]


def load_pilot_labels(pattern: str) -> List[LabelledListing]:
    label_paths = _expand_glob(pattern)
    directory = label_paths[0].parent
    excluded_names = {path.name for path in label_paths}

    states: Dict[str, Dict[str, Any]] = {}
    for candidate in sorted(directory.glob("*.json")):
        if candidate.name in excluded_names:
            continue
        try:
            data = _read_json(candidate)
        except json.JSONDecodeError:
            continue
        if not isinstance(data, list):
            continue
        for entry in data:
            if isinstance(entry, dict) and "id" in entry and "title" in entry:
                states[entry["id"]] = entry

    listings: List[LabelledListing] = []
    for path in label_paths:
        for label in _read_json(path):
            state = states.get(label["id"])
            if state is None:
                continue
            answers = {key: value for key, value in label.items() if key not in ANSWER_EXCLUDED_KEYS}
            listings.append(LabelledListing(id=label["id"], state=state, answers=answers))
    return listings


def load_export_labels(pattern: str) -> List[LabelledListing]:
    listings: List[LabelledListing] = []
    for path in _expand_glob(pattern):
        for line in path.read_text(encoding="utf-8").splitlines():
            stripped = line.strip()
            if not stripped:
                continue
            record = json.loads(stripped)
            listings.append(
                LabelledListing(
                    id=record["listingId"],
                    state=record["state"],
                    answers=dict(record["answers"]),
                )
            )
    return listings


LOADERS: Dict[str, Callable[[str], List[LabelledListing]]] = {
    "pilot": load_pilot_labels,
    "export": load_export_labels,
}


def load_labels(pattern: str, labels_format: str) -> List[LabelledListing]:
    try:
        loader = LOADERS[labels_format]
    except KeyError as exc:
        raise ValueError(f"Unknown labels format: {labels_format!r}") from exc
    return loader(pattern)
