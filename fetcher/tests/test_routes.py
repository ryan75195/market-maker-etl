from __future__ import annotations

from fastapi.testclient import TestClient

from mmfetch.app import create_app
from mmfetch.errors import NotFoundError, ProxyUnavailableError, UpstreamBlockedError, UpstreamErrorError


class FakeClient:
    def __init__(self, item_result=None, item_error=None, search_result=None, search_error=None):
        self.item_result = item_result
        self.item_error = item_error
        self.search_result = search_result
        self.search_error = search_error
        self.search_criteria = None

    async def fetch_item(self, item_id: str) -> str:
        if self.item_error is not None:
            raise self.item_error
        return self.item_result

    async def fetch_search(self, criteria) -> str:
        self.search_criteria = criteria
        if self.search_error is not None:
            raise self.search_error
        return self.search_result


def _client_for(fake_client: FakeClient) -> TestClient:
    return TestClient(create_app(client=fake_client))


def test_health_returns_ok():
    response = _client_for(FakeClient()).get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


def test_fetch_item_success():
    fake = FakeClient(item_result='{"data":{"item":{"id":"m1"}}}')
    response = _client_for(fake).post(
        "/fetch", json={"url": "https://www.mercari.com/us/item/m1/"}
    )

    assert response.status_code == 200
    assert response.json() == {"kind": "item", "body": '{"data":{"item":{"id":"m1"}}}'}


def test_fetch_search_success_maps_url_to_criteria():
    fake = FakeClient(search_result='{"data":{"search":{"count":0,"itemsList":[]}}}')
    response = _client_for(fake).post(
        "/fetch", json={"url": "https://www.mercari.com/search/?keyword=iphone&itemStatuses=2"}
    )

    assert response.status_code == 200
    assert response.json()["kind"] == "search"
    assert fake.search_criteria["query"] == "iphone"
    assert fake.search_criteria["itemStatuses"] == [2, 3]


def test_fetch_unsupported_url_returns_400():
    response = _client_for(FakeClient()).post("/fetch", json={"url": "https://www.ebay.com/foo"})

    assert response.status_code == 400
    assert response.json() == {"error": "unsupported_url"}


def test_fetch_not_found_returns_404():
    fake = FakeClient(item_error=NotFoundError())
    response = _client_for(fake).post(
        "/fetch", json={"url": "https://www.mercari.com/us/item/m404/"}
    )

    assert response.status_code == 404
    assert response.json() == {"error": "not_found"}


def test_fetch_proxy_unavailable_returns_502():
    fake = FakeClient(item_error=ProxyUnavailableError())
    response = _client_for(fake).post(
        "/fetch", json={"url": "https://www.mercari.com/us/item/m1/"}
    )

    assert response.status_code == 502
    assert response.json() == {"error": "proxy_unavailable"}


def test_fetch_upstream_blocked_returns_503():
    fake = FakeClient(item_error=UpstreamBlockedError())
    response = _client_for(fake).post(
        "/fetch", json={"url": "https://www.mercari.com/us/item/m1/"}
    )

    assert response.status_code == 503
    assert response.json() == {"error": "upstream_blocked"}


def test_fetch_upstream_error_returns_502_with_detail():
    fake = FakeClient(item_error=UpstreamErrorError(detail="boom"))
    response = _client_for(fake).post(
        "/fetch", json={"url": "https://www.mercari.com/us/item/m1/"}
    )

    assert response.status_code == 502
    assert response.json() == {"error": "upstream_error", "detail": "boom"}
