from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class Settings:
    host: str
    port: int
    proxy_url: Optional[str]
    max_concurrency: int
    max_attempts: int
    timeout_seconds: float

    @classmethod
    def from_env(cls) -> "Settings":
        return cls(
            host=os.environ.get("FETCHER_HOST", "127.0.0.1"),
            port=int(os.environ.get("FETCHER_PORT", "8766")),
            proxy_url=os.environ.get("FETCHER_PROXY_URL") or None,
            max_concurrency=int(os.environ.get("FETCHER_MAX_CONCURRENCY", "8")),
            max_attempts=int(os.environ.get("FETCHER_MAX_ATTEMPTS", "5")),
            timeout_seconds=float(os.environ.get("FETCHER_TIMEOUT_SECONDS", "40")),
        )
