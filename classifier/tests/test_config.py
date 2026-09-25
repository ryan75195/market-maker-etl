from __future__ import annotations

from pathlib import Path

from mmclassifier.config import Settings


def test_settings_from_env_defaults_batch_size_to_16(monkeypatch, tmp_path: Path) -> None:
    monkeypatch.setenv("MODELS_DIR", str(tmp_path))
    monkeypatch.delenv("CLASSIFIER_BATCH_SIZE", raising=False)

    settings = Settings.from_env()

    assert settings.batch_size == 16


def test_settings_from_env_reads_batch_size_override(monkeypatch, tmp_path: Path) -> None:
    monkeypatch.setenv("MODELS_DIR", str(tmp_path))
    monkeypatch.setenv("CLASSIFIER_BATCH_SIZE", "32")

    settings = Settings.from_env()

    assert settings.batch_size == 32
