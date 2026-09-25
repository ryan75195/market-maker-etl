from __future__ import annotations

import logging

import uvicorn

from mmclassifier.app import create_app
from mmclassifier.config import Settings
from mmclassifier.models import ModelRegistry, load_laya_ensemble


def main() -> None:
    logging.basicConfig(level=logging.INFO)
    settings = Settings.from_env()

    registry = ModelRegistry(
        models_dir=settings.models_dir,
        device=settings.device,
        loader=load_laya_ensemble,
    )
    registry.preload(settings.preload)

    app = create_app(registry=registry, api_key=settings.api_key)
    uvicorn.run(app, host=settings.host, port=settings.port)


if __name__ == "__main__":
    main()
