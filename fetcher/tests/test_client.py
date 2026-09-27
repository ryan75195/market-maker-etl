from __future__ import annotations

import asyncio
import json

import pytest

from mmfetch.client import MercariClient
from mmfetch.errors import NotFoundError, ProxyUnavailableError, UpstreamBlockedError, UpstreamErrorError
from mmfetch.session import MercariSession
from mmfetch.transport import ProxyError, TransportError, TransportResponse

FAR_FUTURE_EXP = 4_000_000_000


def _make_client(fake_transport, bootstrap_script, extra_script, max_attempts=5, max_concurrency=4):
    script = [*bootstrap_script(FAR_FUTURE_EXP), *extra_script]
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=max_attempts, clock=lambda: 0.0)
    client = MercariClient(
        transport=transport, session=session, max_attempts=max_attempts, max_concurrency=max_concurrency
    )
    return client, transport


def _item_response(item: dict | None) -> TransportResponse:
    return TransportResponse(status_code=200, text=json.dumps({"data": {"item": item}}))


def _search_response(items: list[dict]) -> TransportResponse:
    return TransportResponse(
        status_code=200, text=json.dumps({"data": {"search": {"count": len(items), "itemsList": items}}})
    )


def _graphql_error_response(message: str, exception: dict | None = None) -> TransportResponse:
    error = {"message": message, "path": ["item"], "extensions": {"exception": exception or {}}}
    return TransportResponse(status_code=200, text=json.dumps({"errors": [error], "data": None}))


def test_fetch_item_success_returns_raw_body_text(fake_transport, bootstrap_script):
    item = {"id": "m1", "status": "on_sale"}
    client, transport = _make_client(fake_transport, bootstrap_script, [_item_response(item)])

    body_text = asyncio.run(client.fetch_item("m1"))

    assert json.loads(body_text) == {"data": {"item": item}}


def test_fetch_item_null_item_raises_not_found(fake_transport, bootstrap_script):
    client, _ = _make_client(fake_transport, bootstrap_script, [_item_response(None)])

    with pytest.raises(NotFoundError):
        asyncio.run(client.fetch_item("m404"))


def test_fetch_item_record_not_found_exception_maps_to_not_found(fake_transport, bootstrap_script):
    response = _graphql_error_response(
        "Not found", {"code": "RecordNotFoundException", "status": 404}
    )
    client, _ = _make_client(fake_transport, bootstrap_script, [response])

    with pytest.raises(NotFoundError):
        asyncio.run(client.fetch_item("m404"))


def test_fetch_item_gone_exception_maps_to_not_found(fake_transport, bootstrap_script):
    response = _graphql_error_response(
        "Item is no longer available", {"code": "ItemNotAvailableException", "status": 410}
    )
    client, _ = _make_client(fake_transport, bootstrap_script, [response])

    with pytest.raises(NotFoundError):
        asyncio.run(client.fetch_item("m410"))


def test_graphql_errors_with_no_data_map_to_upstream_error(fake_transport, bootstrap_script):
    response = _graphql_error_response("Something broke", {"code": "InternalException", "status": 500})
    client, _ = _make_client(fake_transport, bootstrap_script, [response])

    with pytest.raises(UpstreamErrorError) as excinfo:
        asyncio.run(client.fetch_item("m1"))

    assert excinfo.value.detail == "Something broke"


def test_search_returns_raw_body_text(fake_transport, bootstrap_script):
    items = [{"id": "m1", "name": "iPhone"}]
    client, _ = _make_client(fake_transport, bootstrap_script, [_search_response(items)])

    body_text = asyncio.run(client.fetch_search({"query": "iphone", "itemStatuses": [1]}))

    assert json.loads(body_text)["data"]["search"]["itemsList"] == items


def test_proxy_error_maps_to_proxy_unavailable_after_two_attempts(fake_transport, bootstrap_script):
    client, transport = _make_client(
        fake_transport,
        bootstrap_script,
        [ProxyError("dummy proxy failure"), ProxyError("dummy proxy failure")],
        max_attempts=5,
    )

    with pytest.raises(ProxyUnavailableError):
        asyncio.run(client.fetch_item("m1"))

    post_calls = [call for call in transport.calls if call[0] == "POST"]
    assert len(post_calls) == 2


def test_407_response_maps_to_proxy_unavailable(fake_transport, bootstrap_script):
    response_407 = TransportResponse(status_code=407, text="")
    client, transport = _make_client(
        fake_transport, bootstrap_script, [response_407, response_407], max_attempts=5
    )

    with pytest.raises(ProxyUnavailableError):
        asyncio.run(client.fetch_item("m1"))

    post_calls = [call for call in transport.calls if call[0] == "POST"]
    assert len(post_calls) == 2


def test_challenge_403_retries_then_upstream_blocked(
    fake_transport, bootstrap_script, shell_ok, initialize_ok, make_jwt
):
    challenge = TransportResponse(status_code=403, text="Just a moment...")
    rebootstrap = [shell_ok(), initialize_ok(make_jwt(FAR_FUTURE_EXP))]
    script = [*bootstrap_script(FAR_FUTURE_EXP), challenge, *rebootstrap, challenge]
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=2, clock=lambda: 0.0)
    client = MercariClient(transport=transport, session=session, max_attempts=2, max_concurrency=4)

    with pytest.raises(UpstreamBlockedError):
        asyncio.run(client.fetch_item("m1"))

    assert transport.reset_count == 1


def test_401_triggers_rebootstrap_then_succeeds(
    fake_transport, bootstrap_script, shell_ok, initialize_ok, make_jwt
):
    unauthorized = TransportResponse(status_code=401, text="")
    rebootstrap = [shell_ok(), initialize_ok(make_jwt(FAR_FUTURE_EXP))]
    item = _item_response({"id": "m1"})
    script = [*bootstrap_script(FAR_FUTURE_EXP), unauthorized, *rebootstrap, item]
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=5, clock=lambda: 0.0)
    client = MercariClient(transport=transport, session=session, max_attempts=5, max_concurrency=4)

    body_text = asyncio.run(client.fetch_item("m1"))

    assert json.loads(body_text)["data"]["item"]["id"] == "m1"


def test_transport_error_retries_then_upstream_blocked(fake_transport, bootstrap_script):
    script = [
        *bootstrap_script(FAR_FUTURE_EXP),
        TransportError("connection reset"),
        *bootstrap_script(FAR_FUTURE_EXP),
        TransportError("connection reset"),
    ]
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=2, clock=lambda: 0.0)
    client = MercariClient(transport=transport, session=session, max_attempts=2, max_concurrency=4)

    with pytest.raises(UpstreamBlockedError):
        asyncio.run(client.fetch_item("m1"))

    assert transport.reset_count == 1
