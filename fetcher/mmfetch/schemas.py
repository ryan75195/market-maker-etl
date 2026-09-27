from __future__ import annotations

from pydantic import BaseModel


class FetchRequest(BaseModel):
    url: str


class HealthResponse(BaseModel):
    status: str
