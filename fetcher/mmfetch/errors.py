from __future__ import annotations


class MercariFetchError(Exception):
    status_code: int = 500
    error_code: str = "error"


class UnsupportedUrlError(MercariFetchError):
    status_code = 400
    error_code = "unsupported_url"


class NotFoundError(MercariFetchError):
    status_code = 404
    error_code = "not_found"


class UpstreamErrorError(MercariFetchError):
    status_code = 502
    error_code = "upstream_error"

    def __init__(self, detail: str):
        super().__init__(detail)
        self.detail = detail


class ProxyUnavailableError(MercariFetchError):
    status_code = 502
    error_code = "proxy_unavailable"


class UpstreamBlockedError(MercariFetchError):
    status_code = 503
    error_code = "upstream_blocked"
