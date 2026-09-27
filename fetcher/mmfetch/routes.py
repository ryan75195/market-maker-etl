from __future__ import annotations

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse

from mmfetch.client import MercariClient
from mmfetch.errors import MercariFetchError, UnsupportedUrlError, UpstreamErrorError
from mmfetch.schemas import FetchRequest, HealthResponse
from mmfetch.urls import parse_url


def get_client(request: Request) -> MercariClient:
    return request.app.state.client


def build_router() -> APIRouter:
    router = APIRouter()

    @router.get("/health", response_model=HealthResponse)
    def health() -> HealthResponse:
        return HealthResponse(status="ok")

    @router.post("/fetch")
    async def fetch(payload: FetchRequest, request: Request) -> JSONResponse:
        try:
            kind, target = parse_url(payload.url)
        except UnsupportedUrlError as exc:
            return _error_response(exc)

        client = get_client(request)
        try:
            if kind == "item":
                body_text = await client.fetch_item(target)
            else:
                body_text = await client.fetch_search(target)
        except MercariFetchError as exc:
            return _error_response(exc)

        return JSONResponse(status_code=200, content={"kind": kind, "body": body_text})

    return router


def _error_response(exc: MercariFetchError) -> JSONResponse:
    payload: dict[str, str] = {"error": exc.error_code}
    if isinstance(exc, UpstreamErrorError):
        payload["detail"] = exc.detail
    return JSONResponse(status_code=exc.status_code, content=payload)
