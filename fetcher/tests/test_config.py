from __future__ import annotations

from mmfetch.config import Settings


def test_defaults_when_env_empty(monkeypatch):
    for name in [
        "FETCHER_HOST",
        "FETCHER_PORT",
        "FETCHER_PROXY_URL",
        "FETCHER_MAX_CONCURRENCY",
        "FETCHER_MAX_ATTEMPTS",
        "FETCHER_TIMEOUT_SECONDS",
    ]:
        monkeypatch.delenv(name, raising=False)

    settings = Settings.from_env()

    assert settings.host == "127.0.0.1"
    assert settings.port == 8766
    assert settings.proxy_url is None
    assert settings.max_concurrency == 8
    assert settings.max_attempts == 5
    assert settings.timeout_seconds == 40.0


def test_reads_overrides_from_env(monkeypatch):
    monkeypatch.setenv("FETCHER_HOST", "0.0.0.0")
    monkeypatch.setenv("FETCHER_PORT", "9001")
    monkeypatch.setenv("FETCHER_PROXY_URL", "http://user:pass@dummy-proxy.example:8080")
    monkeypatch.setenv("FETCHER_MAX_CONCURRENCY", "3")
    monkeypatch.setenv("FETCHER_MAX_ATTEMPTS", "2")
    monkeypatch.setenv("FETCHER_TIMEOUT_SECONDS", "12.5")

    settings = Settings.from_env()

    assert settings.host == "0.0.0.0"
    assert settings.port == 9001
    assert settings.proxy_url == "http://user:pass@dummy-proxy.example:8080"
    assert settings.max_concurrency == 3
    assert settings.max_attempts == 2
    assert settings.timeout_seconds == 12.5
