from __future__ import annotations

from typing import Any
from urllib.parse import parse_qs, urlsplit

from mmfetch.errors import UnsupportedUrlError

MERCARI_HOSTS = {"www.mercari.com", "mercari.com"}
ITEM_PATH_PREFIX = "/us/item/"
SEARCH_PATH = "/search/"
SEARCH_LENGTH = 100
ITEM_STATUS_MAP = {"1": [1], "2": [2, 3]}


def parse_url(url: str) -> tuple[str, Any]:
    parts = urlsplit(url)
    if parts.scheme != "https" or parts.netloc not in MERCARI_HOSTS:
        raise UnsupportedUrlError()

    if parts.path.startswith(ITEM_PATH_PREFIX):
        item_id = parts.path[len(ITEM_PATH_PREFIX) :].strip("/")
        if not item_id or "/" in item_id:
            raise UnsupportedUrlError()
        return "item", item_id

    if parts.path == SEARCH_PATH:
        return "search", build_search_criteria(parts.query)

    raise UnsupportedUrlError()


def build_search_criteria(query_string: str) -> dict[str, Any]:
    params = parse_qs(query_string)
    criteria: dict[str, Any] = {"length": SEARCH_LENGTH}

    keyword_values = params.get("keyword")
    criteria["query"] = keyword_values[0] if keyword_values else ""

    item_statuses = _map_item_statuses(params.get("itemStatuses"))
    if item_statuses:
        criteria["itemStatuses"] = item_statuses

    _add_int_list(criteria, params, "brandIds")
    _add_int_list(criteria, params, "categoryIds")
    _add_int_list(criteria, params, "itemConditions")
    _add_int(criteria, params, "minPrice")
    _add_int(criteria, params, "maxPrice")

    offset_values = params.get("offset")
    criteria["offset"] = int(offset_values[0]) if offset_values else 0

    return criteria


def _map_item_statuses(raw_values: list[str] | None) -> list[int]:
    if not raw_values:
        return []

    mapped: list[int] = []
    for raw_value in raw_values:
        for token in raw_value.split(","):
            token = token.strip()
            if not token:
                continue
            for status in ITEM_STATUS_MAP.get(token, [int(token)] if token.isdigit() else []):
                if status not in mapped:
                    mapped.append(status)
    return mapped


def _add_int_list(criteria: dict[str, Any], params: dict[str, list[str]], key: str) -> None:
    raw_values = params.get(key)
    if not raw_values:
        return

    values: list[int] = []
    for raw_value in raw_values:
        for token in raw_value.split(","):
            token = token.strip()
            if token:
                values.append(int(token))

    if values:
        criteria[key] = values


def _add_int(criteria: dict[str, Any], params: dict[str, list[str]], key: str) -> None:
    raw_values = params.get(key)
    if raw_values:
        criteria[key] = int(raw_values[0])
