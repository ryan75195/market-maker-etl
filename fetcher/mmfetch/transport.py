from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional, Protocol

from mmfetch.redact import redact


class ProxyError(Exception):
    pass


class TransportError(Exception):
    pass


@dataclass
class TransportResponse:
    status_code: int
    text: str


class Transport(Protocol):
    def get(self, url: str, headers: dict[str, str]) -> TransportResponse: ...

    def post_json(self, url: str, headers: dict[str, str], body: dict[str, Any]) -> TransportResponse: ...

    def reset(self) -> None: ...


def looks_like_proxy_failure(message: str) -> bool:
    lowered = message.lower()
    if "proxy" not in lowered:
        return False
    return "connect" in lowered or "tunnel" in lowered or "407" in lowered or "resolve" in lowered


class CurlCffiTransport:
    def __init__(self, proxy_url: Optional[str], timeout_seconds: float):
        self._proxy_url = proxy_url
        self._timeout_seconds = timeout_seconds
        self._session = self._new_session()

    def _new_session(self):
        from curl_cffi import requests as curl_requests

        session = curl_requests.Session(impersonate="chrome")
        if self._proxy_url:
            session.proxies = {"http": self._proxy_url, "https": self._proxy_url}
        return session

    def reset(self) -> None:
        self._session = self._new_session()

    def get(self, url: str, headers: dict[str, str]) -> TransportResponse:
        return self._send("GET", url, headers=headers)

    def post_json(self, url: str, headers: dict[str, str], body: dict[str, Any]) -> TransportResponse:
        return self._send("POST", url, headers=headers, json_body=body)

    def _send(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        json_body: Optional[dict[str, Any]] = None,
    ) -> TransportResponse:
        from curl_cffi.requests.exceptions import ProxyError as CurlProxyError
        from curl_cffi.requests.exceptions import RequestException

        try:
            response = self._session.request(
                method,
                url,
                headers=headers,
                json=json_body,
                timeout=self._timeout_seconds,
            )
        except CurlProxyError as exc:
            raise ProxyError(redact(str(exc), self._proxy_url)) from None
        except RequestException as exc:
            message = redact(str(exc), self._proxy_url)
            if looks_like_proxy_failure(message):
                raise ProxyError(message) from None
            raise TransportError(message) from None

        if response.status_code == 407:
            raise ProxyError("proxy responded with 407")

        return TransportResponse(status_code=response.status_code, text=response.text)
