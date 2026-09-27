# Running the stack locally

This documents how to run the full scrape pipeline end to end on a workstation:
the `mmfetch` local sidecar and MarketMakerEtl's Api and Etl processes. It
only lists configuration **setting names** — never paste real secret values
(proxy credentials, API keys) into source control, chat, or logs.

## Quick start: `scripts/run-local.ps1`

For everything except the scraper stack (classifier sidecar, Api, and Etl),
`scripts/run-local.ps1` replaces the manual steps below with a single
repeatable script:

```powershell
Copy-Item scripts/run-config.example.json .local/run-config.json
notepad .local/run-config.json   # fill in databasePath, modelsDir, pythonExe, etc.

./scripts/run-local.ps1 -Start    # builds Api + Etl, starts classifier/Api/Etl hidden, waits for health
./scripts/run-local.ps1 -Status   # PIDs, alive/dead, and health responses
./scripts/run-local.ps1 -Stop     # kills the recorded process trees
```

`.local/run-config.json` is gitignored — it is the only place machine-specific
paths (model directory, venv `python.exe`, DB path) need to live. The example
config's `scraperStartScript` field can point at a local script that starts
the scraper stack described below; leave it `null` to manage the scraper
stack manually with the steps in this document. Logs land under
`.local/logs/<name>-<timestamp>.log`, PID records under `.local/pids/`.

The classifier needs a GPU at runtime; for a smoke test, set
`classifierPreload` to `[]` in the config so no model is loaded eagerly.

## Manual steps

## Prerequisites

- .NET 10 SDK
- A running `mmfetch` sidecar (see its own repo/README for setup) listening
  on `Fetcher:BaseUrl` (default `http://127.0.0.1:8766`). It is a local
  HTTP service that fetches Mercari URLs and returns their JSON payload; the
  ETL no longer talks to a browser-automation scraper or blob storage.

## 1. Start the mmfetch sidecar

Start it however its own documentation describes (it is a separate
repo/process, not part of this solution). Confirm it is up before starting
the ETL:

```
curl http://127.0.0.1:8766/health
```

which should return `{"status":"ok"}`.

### Sidecar error semantics the ETL relies on

`FetcherScrapeClient` maps the sidecar's responses as follows:

- `404 {"error":"not_found"}` — the item/listing genuinely no longer exists;
  mapped to `ListingNotFoundException`, which the ETL treats as a terminal,
  non-retried outcome (see below), not a transient failure.
- `502 {"error":"proxy_unavailable"}`, `503 {"error":"upstream_blocked"}`,
  connection-refused, or a request timeout — mapped to
  `FetchInfrastructureUnavailableException`, a transient condition retried
  like any other fetch failure.
- `502 {"error":"upstream_error", ...}`, `400 {"error":"unsupported_url"}`,
  or any other unexpected status/malformed body — mapped to
  `FetchFailedException`.

## 2. Start MarketMakerEtl.Api

From `src/MarketMakerEtl.Api`:

```
dotnet run --no-launch-profile --urls http://localhost:5299
```

(`--no-launch-profile` avoids `launchSettings.json` silently overriding the
port.)

## 3. Start MarketMakerEtl.Etl

From `src/MarketMakerEtl.Etl`:

```
dotnet run
```

This process hosts three background workers: `JobQueueingWorker` (queues due
jobs on a schedule), `ScrapeWorker` (claims and runs queued scrape runs), and
`ListingRefreshWorker` (refreshes active listings on a schedule).

## Configuration settings (names only)

Both the Api and Etl processes read the same options, bound from environment
variables using the `Section__Key` convention (or `appsettings.json` /
`ASPNETCORE_ENVIRONMENT`-specific files, which do not exist in this repo — it
is environment-variable only):

| Setting | Purpose |
|---|---|
| `Fetcher:BaseUrl` | Base URL of the local `mmfetch` sidecar; default `http://127.0.0.1:8766` |
| `Fetcher:TimeoutSeconds` | Per-request timeout for sidecar fetches; default `30` |
| `Database:ConnectionString` | SQLite connection string for MarketMakerEtl's own database |
| `Database:BusyTimeoutMs` | `PRAGMA busy_timeout` (milliseconds) applied to every connection so a writer waits for a lock instead of failing immediately with `SQLITE_BUSY`; default `10000` |
| `Schedule:TickMinutes` | How often `JobQueueingWorker` checks for due jobs |
| `Schedule:RefreshIntervalHours` | How often `ListingRefreshWorker` refreshes active listings |
| `Scrape:MaxPages` | Max eBay search result pages per direction |
| `Scrape:CollectSold` | Whether to also search sold listings |
| `Scrape:MaxBandsPerDirection` | Max Mercari price bands sliced per direction (active/sold) |
| `Scrape:MaxConcurrentDetailFetches` | Concurrency for item detail page fetches |
| `Scrape:MaxDetailFetchesPerRun` | Cap on item detail fetches per run |
| `Scrape:MaxDetailFetchAttempts` | Retry attempts per item detail fetch |
| `Scrape:SearchPageMaxAttempts` | Max attempts per Mercari/eBay search-page fetch before recording a `SearchPageFailed` issue (sidecar fetch failures and unrecognised payloads are retried the same way) |
| `Scrape:SearchPageRetryBaseDelaySeconds` | Base delay between search-page fetch retries; the actual delay grows with each attempt (about 5s/15s/30s/60s at the default) |
| `Scrape:SearchConcurrency` | Max Mercari search-band (and sold-backfill item-page) fetches in flight at once per direction (default 3) |

For a short local smoke test, set a low `Schedule:TickMinutes` (e.g. `1`) and
small `Scrape:MaxBandsPerDirection` / `Scrape:MaxDetailFetchesPerRun` values
(e.g. `6-10`) so a full run finishes in a few minutes instead of consuming a
large fetch budget. Be aware that a small `Scrape:MaxBandsPerDirection` value
means the price-band binary split can only explore a small slice of the
price range before the cap is hit for any search term with more than a
couple hundred results — a low listing count from a smoke-test run reflects
the fetch budget, not a defect, and shows up as a `PriceBandCapHit` issue on
the run.

## 4. Create a job and let the scheduler run it

Always create jobs through the API and let the scheduler queue them — do not
call `/api/jobs/{jobId}/run` for anything that is meant to exercise the real
scheduled path:

```
curl -X POST http://localhost:5299/api/jobs \
  -H "Content-Type: application/json" \
  -d '{"searchTerm":"enamel pin","intervalHours":24}'
```

`JobQueueingWorker` queues the job on its next tick (a brand-new job with no
`lastQueuedUtc` is immediately due). Watch progress with:

```
curl http://localhost:5299/api/jobs/{jobId}/runs
```

which reports each run's `triggerType`, per-direction listing counts, price
band and detail-fetch counters, and any recorded issues (including
`PriceBandCapHit` and per-listing detail-fetch failures).

## Cleanup

Stop, in reverse order: MarketMakerEtl.Etl, MarketMakerEtl.Api, and the
`mmfetch` sidecar.
