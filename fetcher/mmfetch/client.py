from __future__ import annotations

import asyncio
import json
from typing import Any

from starlette.concurrency import run_in_threadpool

from mmfetch.errors import NotFoundError, ProxyUnavailableError, UpstreamBlockedError, UpstreamErrorError
from mmfetch.queries import ITEM_QUERY, SEARCH_QUERY
from mmfetch.session import MercariSession
from mmfetch.transport import ProxyError, Transport, TransportError

GRAPHQL_URL = "https://www.mercari.com/v1/api"
MAX_PROXY_ATTEMPTS = 2
CHALLENGE_STATUS_CODES = {403}
NOT_FOUND_EXCEPTION_CODES = {"RecordNotFoundException", "ItemNotAvailableException"}
NOT_FOUND_STATUS_CODES = {404, 410}


class MercariClient:
    def __init__(
        self,
        transport: Transport,
        session: MercariSession,
        max_attempts: int,
        max_concurrency: int,
    ):
        self._transport = transport
        self._session = session
        self._max_attempts = max_attempts
        self._semaphore = asyncio.Semaphore(max_concurrency)

    async def fetch_item(self, item_id: str) -> str:
        body, text = await self._execute("itemDetail", ITEM_QUERY, {"id": item_id})
        if _safe_get(body, "data", "item") is None:
            raise NotFoundError()
        return text

    async def fetch_search(self, criteria: dict[str, Any]) -> str:
        _body, text = await self._execute("webSearch", SEARCH_QUERY, {"criteria": criteria})
        return text

    async def _execute(self, operation_name: str, query: str, variables: dict[str, Any]) -> tuple[dict, str]:
        async with self._semaphore:
            return await self._execute_with_retries(operation_name, query, variables)

    async def _execute_with_retries(
        self, operation_name: str, query: str, variables: dict[str, Any]
    ) -> tuple[dict, str]:
        proxy_attempts = 0

        for attempt in range(1, self._max_attempts + 1):
            token = await self._session.ensure_ready()
            headers = self._build_headers(token)
            payload = {"operationName": operation_name, "query": query, "variables": variables}

            try:
                response = await run_in_threadpool(self._transport.post_json, GRAPHQL_URL, headers, payload)
            except ProxyError as exc:
                proxy_attempts += 1
                if proxy_attempts >= MAX_PROXY_ATTEMPTS:
                    raise ProxyUnavailableError() from exc
                continue
            except TransportError as exc:
                if attempt >= self._max_attempts:
                    raise UpstreamBlockedError() from exc
                await self._session.force_refresh()
                self._transport.reset()
                continue

            if response.status_code == 401:
                await self._session.force_refresh()
                continue

            if response.status_code == 407:
                proxy_attempts += 1
                if proxy_attempts >= MAX_PROXY_ATTEMPTS:
                    raise ProxyUnavailableError()
                continue

            if response.status_code in CHALLENGE_STATUS_CODES or response.status_code >= 500:
                if attempt >= self._max_attempts:
                    raise UpstreamBlockedError()
                await self._session.force_refresh()
                self._transport.reset()
                continue

            body = json.loads(response.text)
            _raise_for_graphql_errors(body)
            return body, response.text

        raise UpstreamBlockedError()

    def _build_headers(self, token) -> dict[str, str]:
        headers = self._session.base_headers()
        headers.update(
            {
                "content-type": "application/json",
                "apollo-require-preflight": "true",
                "authorization": f"Bearer {token.access_token}",
                "x-csrf-token": token.csrf,
            }
        )
        return headers


def _safe_get(body: dict, *keys: str) -> Any:
    current: Any = body
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def _raise_for_graphql_errors(body: dict) -> None:
    errors = body.get("errors")
    if not errors or body.get("data"):
        return

    first_error = errors[0]
    if _is_not_found_error(first_error):
        raise NotFoundError()
    raise UpstreamErrorError(detail=first_error.get("message", "unknown upstream error"))


def _is_not_found_error(error: dict) -> bool:
    extensions = error.get("extensions") or {}
    exception = extensions.get("exception") or {}
    if exception.get("code") in NOT_FOUND_EXCEPTION_CODES:
        return True
    return exception.get("status") in NOT_FOUND_STATUS_CODES
