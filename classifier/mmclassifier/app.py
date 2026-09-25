from __future__ import annotations

import asyncio
from typing import Optional

from fastapi import FastAPI

from mmclassifier.models import ModelRegistry
from mmclassifier.routes import build_router


def create_app(registry: ModelRegistry, api_key: Optional[str] = None) -> FastAPI:
    app = FastAPI(title="mmclassifier")
    app.state.registry = registry
    app.state.api_key = api_key
    app.state.inference_lock = asyncio.Lock()
    app.include_router(build_router())
    return app
