from __future__ import annotations

import asyncio
import base64
import json
import time
from dataclasses import dataclass
from typing import Callable, Optional

from starlette.concurrency import run_in_threadpool

from mmfetch.transport import ProxyError, Transport, TransportError

SHELL_URL = "https://www.mercari.com/search/?keyword=iphone"
INITIALIZE_URL = "https://www.mercari.com/v1/initialize"
TOKEN_REFRESH_MARGIN_SECONDS = 3600


class BootstrapFailedError(Exception):
    pass


@dataclass(frozen=True)
class TokenState:
    access_token: str
    csrf: str
    exp: int


def decode_jwt_exp(token: str) -> int:
    segments = token.split(".")
    if len(segments) < 2:
        raise ValueError("access token is not a JWT")

    payload_b64 = segments[1]
    padding = "=" * (-len(payload_b64) % 4)
    payload = json.loads(base64.urlsafe_b64decode(payload_b64 + padding))
    return int(payload["exp"])


class MercariSession:
    def __init__(
        self,
        transport: Transport,
        max_attempts: int,
        clock: Callable[[], float] = time.time,
    ):
        self._transport = transport
        self._max_attempts = max_attempts
        self._clock = clock
        self._lock = asyncio.Lock()
        self._token: Optional[TokenState] = None

    @staticmethod
    def base_headers() -> dict[str, str]:
        return {
            "x-platform": "web",
            "x-double-web": "1",
            "x-app-version": "1",
        }

    def _needs_refresh(self) -> bool:
        if self._token is None:
            return True
        return (self._token.exp - self._clock()) < TOKEN_REFRESH_MARGIN_SECONDS

    async def ensure_ready(self) -> TokenState:
        if not self._needs_refresh():
            return self._token
        async with self._lock:
            if self._needs_refresh():
                await self._bootstrap_locked()
        return self._token

    async def force_refresh(self) -> TokenState:
        async with self._lock:
            await self._bootstrap_locked()
        return self._token

    async def _bootstrap_locked(self) -> None:
        last_error: Optional[BaseException] = None
        for attempt in range(1, self._max_attempts + 1):
            if attempt > 1:
                self._transport.reset()
            try:
                token = await run_in_threadpool(self._bootstrap_once)
            except (TransportError, ProxyError) as exc:
                last_error = exc
                continue
            if token is None:
                last_error = BootstrapFailedError("bootstrap challenged")
                continue
            self._token = token
            return
        raise BootstrapFailedError(str(last_error) if last_error else "bootstrap failed")

    def _bootstrap_once(self) -> Optional[TokenState]:
        shell_response = self._transport.get(SHELL_URL, headers={})
        if shell_response.status_code >= 400:
            return None

        init_headers = self.base_headers()
        init_headers["accept"] = "application/json"
        init_response = self._transport.get(INITIALIZE_URL, headers=init_headers)
        if init_response.status_code != 200:
            return None

        body = json.loads(init_response.text)
        access_token = body.get("accessToken")
        csrf = body.get("csrf")
        if not access_token or not csrf:
            return None

        return TokenState(access_token=access_token, csrf=csrf, exp=decode_jwt_exp(access_token))
