from __future__ import annotations

import base64
import json
from typing import Any, Callable, Optional

import pytest

from mmfetch.transport import TransportResponse


def _encode_jwt_segment(segment: dict[str, Any]) -> str:
    raw = json.dumps(segment).encode()
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode()


def _make_jwt(exp: int, iat: Optional[int] = None) -> str:
    header = {"alg": "HS256", "typ": "JWT"}
    payload = {"exp": exp, "iat": iat if iat is not None else exp - 604800}
    return f"{_encode_jwt_segment(header)}.{_encode_jwt_segment(payload)}.signature"


def _shell_ok() -> TransportResponse:
    return TransportResponse(status_code=200, text="<html></html>")


def _initialize_ok(token: str, csrf: str = "csrf-token") -> TransportResponse:
    body = {"csrf": csrf, "isBot": False, "accessToken": token}
    return TransportResponse(status_code=200, text=json.dumps(body))


def _initialize_challenged() -> TransportResponse:
    return TransportResponse(status_code=403, text="Just a moment...")


def _bootstrap_script(exp: int, csrf: str = "csrf-token") -> list[TransportResponse]:
    return [_shell_ok(), _initialize_ok(_make_jwt(exp), csrf)]


class FakeTransport:
    def __init__(self, script: list[Any]):
        self._script = list(script)
        self.calls: list[tuple[str, str, dict[str, str], Any]] = []
        self.reset_count = 0

    def get(self, url: str, headers: dict[str, str]) -> TransportResponse:
        return self._advance("GET", url, headers, None)

    def post_json(self, url: str, headers: dict[str, str], body: Any) -> TransportResponse:
        return self._advance("POST", url, headers, body)

    def _advance(self, method: str, url: str, headers: dict[str, str], body: Any) -> TransportResponse:
        self.calls.append((method, url, headers, body))
        if not self._script:
            raise AssertionError(f"FakeTransport script exhausted on {method} {url}")
        item = self._script.pop(0)
        if isinstance(item, BaseException):
            raise item
        return item

    def reset(self) -> None:
        self.reset_count += 1


@pytest.fixture
def make_jwt() -> Callable[..., str]:
    return _make_jwt


@pytest.fixture
def shell_ok() -> Callable[[], TransportResponse]:
    return _shell_ok


@pytest.fixture
def initialize_ok() -> Callable[..., TransportResponse]:
    return _initialize_ok


@pytest.fixture
def initialize_challenged() -> Callable[[], TransportResponse]:
    return _initialize_challenged


@pytest.fixture
def bootstrap_script() -> Callable[..., list[TransportResponse]]:
    return _bootstrap_script


@pytest.fixture
def fake_transport() -> Callable[[list[Any]], FakeTransport]:
    return FakeTransport
