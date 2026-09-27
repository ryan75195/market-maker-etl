from __future__ import annotations

import asyncio

import pytest

from mmfetch.session import BootstrapFailedError, MercariSession, decode_jwt_exp


def test_decode_jwt_exp_reads_exp_claim(make_jwt):
    token = make_jwt(exp=1791108728, iat=1790503928)
    assert decode_jwt_exp(token) == 1791108728


def test_bootstrap_parses_token_csrf_and_exp(fake_transport, bootstrap_script):
    exp = 2_000_000_000
    transport = fake_transport(bootstrap_script(exp, csrf="the-csrf"))
    session = MercariSession(transport=transport, max_attempts=3, clock=lambda: 0.0)

    token = asyncio.run(session.ensure_ready())

    assert token.csrf == "the-csrf"
    assert token.exp == exp
    assert len(transport.calls) == 2


def test_ensure_ready_does_not_rebootstrap_when_token_fresh(fake_transport, bootstrap_script):
    exp = 2_000_000_000
    transport = fake_transport(bootstrap_script(exp))
    session = MercariSession(transport=transport, max_attempts=3, clock=lambda: 0.0)

    asyncio.run(session.ensure_ready())
    second = asyncio.run(session.ensure_ready())

    assert second.exp == exp
    assert len(transport.calls) == 2


def test_ensure_ready_rebootstraps_when_token_within_expiry_margin(
    fake_transport, shell_ok, initialize_ok, make_jwt
):
    now = 1_000_000.0
    almost_expired_exp = int(now + 1800)
    fresh_exp = int(now + 10_000)

    script = [
        shell_ok(),
        initialize_ok(make_jwt(almost_expired_exp)),
        shell_ok(),
        initialize_ok(make_jwt(fresh_exp)),
    ]
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=3, clock=lambda: now)

    first = asyncio.run(session.ensure_ready())
    assert first.exp == almost_expired_exp

    second = asyncio.run(session.ensure_ready())
    assert second.exp == fresh_exp
    assert len(transport.calls) == 4


def test_bootstrap_retries_on_challenge_then_succeeds(
    fake_transport, shell_ok, initialize_challenged, bootstrap_script
):
    exp = 2_000_000_000
    script = [shell_ok(), initialize_challenged(), *bootstrap_script(exp)]
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=3, clock=lambda: 0.0)

    token = asyncio.run(session.ensure_ready())

    assert token.exp == exp
    assert transport.reset_count == 1


def test_bootstrap_gives_up_after_max_attempts(fake_transport, shell_ok, initialize_challenged):
    script = [shell_ok(), initialize_challenged()] * 2
    transport = fake_transport(script)
    session = MercariSession(transport=transport, max_attempts=2, clock=lambda: 0.0)

    with pytest.raises(BootstrapFailedError):
        asyncio.run(session.ensure_ready())
