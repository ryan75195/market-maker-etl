# mmfetch

HTTP sidecar that fetches Mercari item and search data directly through Mercari's GraphQL
API (`https://www.mercari.com/v1/api`), using [`curl_cffi`](https://github.com/lexiforest/curl_cffi)
for Chrome TLS impersonation. It replaces the AIOWebScraper Azure Functions browser scraper in
the market-maker-etl pipeline. It is a standalone Python project, outside the .NET solution, and
the .NET pre-commit gate does not cover it.

The .NET ETL is the only intended caller. It POSTs a Mercari item or search URL to `/fetch` and
gets back the raw GraphQL response body as JSON text.

## Setup

Requires Python 3.10+.

```powershell
python -m venv C:\mmfv
C:\mmfv\Scripts\pip install -e fetcher[dev]
```

Run the test suite (no network access — the transport is mocked):

```powershell
C:\mmfv\Scripts\pytest fetcher
```

## Running the server

```powershell
C:\mmfv\Scripts\python -m mmfetch
```

The server listens on `FETCHER_HOST`/`FETCHER_PORT` (default `127.0.0.1:8766`).

### Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `FETCHER_HOST` | `127.0.0.1` | Bind host for `python -m mmfetch`. |
| `FETCHER_PORT` | `8766` | Bind port for `python -m mmfetch`. |
| `FETCHER_PROXY_URL` | *(empty)* | Optional `http://user:pass@host:port` proxy used for every Mercari request. Never logged, echoed in an error, or returned in a response — it is redacted (including its bare credentials) anywhere it could leak. |
| `FETCHER_MAX_CONCURRENCY` | `8` | Maximum in-flight Mercari calls at once. |
| `FETCHER_MAX_ATTEMPTS` | `5` | Maximum attempts for one `/fetch` call (each retry after a challenge or 5xx re-bootstraps the session and opens a fresh connection) and for a session bootstrap. |
| `FETCHER_TIMEOUT_SECONDS` | `40` | Per-request timeout passed to `curl_cffi`. |

## Endpoints

### `GET /health`

```json
{ "status": "ok" }
```

### `POST /fetch`

Request:

```json
{ "url": "https://www.mercari.com/us/item/m71344610988/" }
```

or

```json
{ "url": "https://www.mercari.com/search/?keyword=iphone&itemStatuses=1&minPrice=10000&maxPrice=20000" }
```

Success response (`200`):

```json
{ "kind": "item", "body": "{\"data\":{\"item\":{...}}}" }
```

`kind` is `"item"` for an item URL (`https://www.mercari.com/us/item/{id}/`) or `"search"` for a
search URL (`https://www.mercari.com/search/?...`). `body` is the raw JSON text of the upstream
GraphQL response — not re-serialized or reshaped.

**Search URL to criteria mapping:**

| URL query param | Criteria field | Notes |
|---|---|---|
| `keyword` | `query` | |
| `itemStatuses=1` | `itemStatuses: [1]` | active |
| `itemStatuses=2` | `itemStatuses: [2, 3]` | sold — matches what the browser scraper sent |
| `brandIds`, `categoryIds`, `itemConditions` | same name, `[int]` | |
| `minPrice`, `maxPrice` | same name, `int` | cents, passed through unchanged |
| `offset` | `offset` | defaults to `0` |
| *(none)* | `length: 100` | always `100`, not read from the URL |

**Error responses:**

| Status | Body | Cause |
|---|---|---|
| `400` | `{"error":"unsupported_url"}` | The URL is not a Mercari item or search URL. |
| `404` | `{"error":"not_found"}` | The item doesn't exist, or is gone/removed (Mercari returns this as a GraphQL error rather than a bare null in current testing — see "Known gaps" below). |
| `502` | `{"error":"proxy_unavailable"}` | A proxy CONNECT/tunnel failure or HTTP 407 from the proxy. At most 2 attempts — no retry storm. |
| `502` | `{"error":"upstream_error","detail":"<first GraphQL error message>"}` | The GraphQL response has `errors` and no usable `data`, and it isn't a recognized not-found/gone shape. |
| `503` | `{"error":"upstream_blocked"}` | Still challenged (403) or 5xx after `FETCHER_MAX_ATTEMPTS`, each retry on a fresh connection with a fresh session bootstrap. |

## Session management

`mmfetch.session.MercariSession` bootstraps by GETting a search page shell (to obtain the
`__cf_bm` cookie) and then `GET /v1/initialize`, caching the returned `accessToken`, `csrf` and
cookies (held by the underlying `curl_cffi` session). It re-bootstraps when the token is within
one hour of its JWT `exp` claim, on an HTTP 401, or when a request comes back challenged (403) or
5xx. An `asyncio.Lock` ensures only one bootstrap runs at a time; concurrent callers wait for it
rather than racing their own.

## Known gaps

- **`data.item: null` for a nonexistent id.** Live testing (2026-09-27) shows Mercari's
  `itemDetail` query never actually returns a bare `data.item: null` for a well-formed but
  nonexistent or removed id — it returns a GraphQL `errors` entry (`RecordNotFoundException` /
  404, or `ItemNotAvailableException` / 410 for a "gone" item) with `data: null` instead. `mmfetch`
  treats both that error shape and a literal `data.item: null` as `404 not_found`; anything else
  in `errors` with no usable `data` is `502 upstream_error`.
- **`seller.sellerId`.** The ad-hoc search schema's `itemsList.seller` is a `User` type that only
  exposes a numeric `id`, not a `sellerId` field. `SEARCH_QUERY` aliases it
  (`seller { sellerId: id }`) so the JSON key matches what `MercariSearchPayloadParser` reads.
- **`color` is an object, not a string.** The ad-hoc schema's `itemsList.color` is a
  `MasterItemColor` object requiring a subfield selection (`color { name }`), unlike the plain
  string `MercariSearchPayloadParser.ReadString(item, "color")` expects from the old
  browser-intercepted `searchFacetQuery`. The field is selected so the data is present in the
  raw JSON for the ETL to adapt to; the existing parser will read it as absent until updated.
- **`shippingPayer` is consistently null in search results.** It is selected
  (`shippingPayer { code }`) and does populate on the `itemDetail` query, but across every live
  search tried during development (multiple keywords, categories, active and sold, a price band —
  see `scripts/live_smoke.py` output) it was always null. This looks like a search-index
  limitation rather than a query mistake.
- **`color`, `itemSize` and `customFacetsList`** are populated only for listings that actually
  have that attribute set, so a given run of `scripts/live_smoke.py` may or may not observe them —
  the script reports these as non-blocking (`WARN`, not `FAIL`) for that reason.

## Live verification

`scripts/live_smoke.py` exercises the real Mercari API end to end (bootstrap, a live search, 3
item lookups by id taken from that search, a sold search, a price-banded search, and an
`offset=100` page) using the production `mmfetch` code directly. It must be run from a normal
home IP with **no** `FETCHER_PROXY_URL` set, and makes under a dozen requests:

```powershell
C:\mmfv\Scripts\python fetcher\scripts\live_smoke.py
```

It prints one `PASS`/`WARN`/`FAIL` line per check and exits non-zero if any required check fails.
