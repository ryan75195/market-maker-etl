from __future__ import annotations

from mmfetch.redact import redact

DUMMY_PROXY = "http://dummyuser:dummypass@dummy-proxy.example:8080"


def test_redact_masks_full_proxy_url():
    message = f"Failed to connect to proxy at {DUMMY_PROXY}"
    assert DUMMY_PROXY not in redact(message, DUMMY_PROXY)


def test_redact_masks_bare_password():
    message = "auth failed with password dummypass for proxy user"
    assert "dummypass" not in redact(message, DUMMY_PROXY)


def test_redact_masks_credential_pair():
    message = "could not authenticate as dummyuser:dummypass"
    assert "dummyuser:dummypass" not in redact(message, DUMMY_PROXY)


def test_redact_is_noop_without_proxy_configured():
    message = "some error with no secret in it"
    assert redact(message, None) == message


def test_redact_leaves_unrelated_text_untouched():
    message = "connection timed out after 40s"
    assert redact(message, DUMMY_PROXY) == message
