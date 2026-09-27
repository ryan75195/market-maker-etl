from __future__ import annotations

from typing import Optional
from urllib.parse import urlsplit

MASK = "***"


def redaction_targets(proxy_url: Optional[str]) -> list[str]:
    if not proxy_url:
        return []

    targets = {proxy_url}
    parsed = urlsplit(proxy_url)

    if parsed.password:
        targets.add(parsed.password)
        if parsed.username:
            targets.add(f"{parsed.username}:{parsed.password}")
    elif parsed.username:
        targets.add(parsed.username)

    return sorted((target for target in targets if target), key=len, reverse=True)


def redact(text: str, proxy_url: Optional[str]) -> str:
    if not text:
        return text

    redacted = text
    for target in redaction_targets(proxy_url):
        redacted = redacted.replace(target, MASK)
    return redacted
