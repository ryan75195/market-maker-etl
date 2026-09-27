from __future__ import annotations

import pytest

from mmfetch.errors import UnsupportedUrlError
from mmfetch.urls import parse_url


def test_item_url_extracts_id():
    kind, target = parse_url("https://www.mercari.com/us/item/m71344610988/")
    assert kind == "item"
    assert target == "m71344610988"


def test_item_url_without_trailing_slash():
    kind, target = parse_url("https://www.mercari.com/us/item/m71344610988")
    assert kind == "item"
    assert target == "m71344610988"


def test_search_url_maps_keyword_to_query():
    kind, criteria = parse_url("https://www.mercari.com/search/?keyword=iphone+15")
    assert kind == "search"
    assert criteria["query"] == "iphone 15"


def test_search_url_maps_active_status_to_single_element_list():
    _, criteria = parse_url("https://www.mercari.com/search/?keyword=iphone&itemStatuses=1")
    assert criteria["itemStatuses"] == [1]


def test_search_url_maps_sold_status_to_two_and_three():
    _, criteria = parse_url("https://www.mercari.com/search/?keyword=iphone&itemStatuses=2")
    assert criteria["itemStatuses"] == [2, 3]


def test_search_url_passes_cents_through_unchanged():
    _, criteria = parse_url(
        "https://www.mercari.com/search/?keyword=iphone&minPrice=1000&maxPrice=50000"
    )
    assert criteria["minPrice"] == 1000
    assert criteria["maxPrice"] == 50000


def test_search_url_maps_brand_category_condition_ids():
    _, criteria = parse_url(
        "https://www.mercari.com/search/?keyword=iphone&brandIds=319&categoryIds=778&itemConditions=3"
    )
    assert criteria["brandIds"] == [319]
    assert criteria["categoryIds"] == [778]
    assert criteria["itemConditions"] == [3]


def test_search_url_offset_defaults_to_zero():
    _, criteria = parse_url("https://www.mercari.com/search/?keyword=iphone")
    assert criteria["offset"] == 0


def test_search_url_offset_is_read_when_present():
    _, criteria = parse_url("https://www.mercari.com/search/?keyword=iphone&offset=100")
    assert criteria["offset"] == 100


def test_search_url_length_is_always_100():
    _, criteria = parse_url("https://www.mercari.com/search/?keyword=iphone&offset=200")
    assert criteria["length"] == 100


@pytest.mark.parametrize(
    "url",
    [
        "https://www.mercari.com/",
        "https://www.mercari.com/us/item/",
        "https://www.mercari.com/us/item/m1/reviews",
        "https://www.ebay.com/search/?keyword=iphone",
        "http://www.mercari.com/search/?keyword=iphone",
        "not-a-url",
    ],
)
def test_unsupported_urls_raise(url):
    with pytest.raises(UnsupportedUrlError):
        parse_url(url)
