from __future__ import annotations

from fastapi import FastAPI

from mmfetch.client import MercariClient
from mmfetch.routes import build_router


def create_app(client: MercariClient) -> FastAPI:
    app = FastAPI(title="mmfetch")
    app.state.client = client
    app.include_router(build_router())
    return app
