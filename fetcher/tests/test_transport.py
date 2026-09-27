from __future__ import annotations

import pytest
from curl_cffi.requests.exceptions import ProxyError as CurlProxyError
from curl_cffi.requests.exceptions import ReadTimeout

from mmfetch.transport import CurlCffiTransport, ProxyError, TransportError

DUMMY_PROXY = "http://dummyuser:dummypass@dummy-proxy.example:8080"


class _FakeCurlResponse:
    def __init__(self, status_code: int, text: str = ""):
        self.status_code = status_code
        self.text = text


class _FakeCurlSession:
    def __init__(self, exception: BaseException | None = None, response: _FakeCurlResponse | None = None):
        self._exception = exception
        self._response = response
        self.proxies: dict[str, str] = {}

    def request(self, method, url, headers=None, json=None, timeout=None):
        if self._exception is not None:
            raise self._exception
        return self._response


def _transport_with_fake_session(fake_session: _FakeCurlSession) -> CurlCffiTransport:
    transport = CurlCffiTransport(proxy_url=DUMMY_PROXY, timeout_seconds=5)
    transport._session = fake_session
    return transport


def test_curl_proxy_error_is_redacted_and_reraised_as_proxy_error():
    exc = CurlProxyError(f"Failed to connect to proxy {DUMMY_PROXY}: connection refused")
    transport = _transport_with_fake_session(_FakeCurlSession(exception=exc))

    with pytest.raises(ProxyError) as excinfo:
        transport.get("https://www.mercari.com/v1/initialize", headers={})

    assert DUMMY_PROXY not in str(excinfo.value)
    assert "dummypass" not in str(excinfo.value)


def test_generic_request_exception_is_redacted_and_reraised_as_transport_error():
    exc = ReadTimeout(f"Timed out reading from proxy {DUMMY_PROXY}")
    transport = _transport_with_fake_session(_FakeCurlSession(exception=exc))

    with pytest.raises(TransportError) as excinfo:
        transport.get("https://www.mercari.com/v1/initialize", headers={})

    assert DUMMY_PROXY not in str(excinfo.value)


def test_407_response_raises_proxy_error():
    transport = _transport_with_fake_session(_FakeCurlSession(response=_FakeCurlResponse(407)))

    with pytest.raises(ProxyError):
        transport.get("https://www.mercari.com/v1/initialize", headers={})


def test_successful_response_is_returned_as_is():
    transport = _transport_with_fake_session(_FakeCurlSession(response=_FakeCurlResponse(200, "ok")))

    response = transport.get("https://www.mercari.com/v1/initialize", headers={})

    assert response.status_code == 200
    assert response.text == "ok"


def test_reset_builds_a_new_underlying_session_with_proxy_configured():
    transport = CurlCffiTransport(proxy_url=DUMMY_PROXY, timeout_seconds=5)
    first_session = transport._session

    transport.reset()

    assert transport._session is not first_session
    assert transport._session.proxies.get("https") == DUMMY_PROXY
