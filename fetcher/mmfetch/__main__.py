from __future__ import annotations

import logging

import uvicorn

from mmfetch.app import create_app
from mmfetch.client import MercariClient
from mmfetch.config import Settings
from mmfetch.session import MercariSession
from mmfetch.transport import CurlCffiTransport


def main() -> None:
    logging.basicConfig(level=logging.INFO)
    settings = Settings.from_env()

    transport = CurlCffiTransport(proxy_url=settings.proxy_url, timeout_seconds=settings.timeout_seconds)
    session = MercariSession(transport=transport, max_attempts=settings.max_attempts)
    client = MercariClient(
        transport=transport,
        session=session,
        max_attempts=settings.max_attempts,
        max_concurrency=settings.max_concurrency,
    )

    app = create_app(client=client)
    uvicorn.run(app, host=settings.host, port=settings.port)


if __name__ == "__main__":
    main()
