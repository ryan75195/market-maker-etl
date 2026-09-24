# Running the stack locally

This documents how to run the full scrape pipeline end to end on a workstation:
Azurite, the AIOWebScraper Functions host and worker, and MarketMakerEtl's Api
and Etl processes. It only lists configuration **setting names** — never
paste real secret values (proxy credentials, storage keys, API keys) into
source control, chat, or logs.

## Prerequisites

- .NET 10 SDK
- Node.js (for Azurite) or the `azurite` npm package installed globally
- A checkout of [AIOWebScraper](https://github.com/ryan75195/AIOWebScraper)
  alongside this repo, with its own `local.settings.json` (Functions host) and
  `ScraperWorker/appsettings.json` + `appsettings.local.json` filled in with
  real values for `residentialProxy`, `twoCaptchaApiKey`, and the Azurite
  connection strings

## 1. Start Azurite

Azurite provides local Blob, Queue, and Table storage emulation.

```
azurite --silent --location <some-data-dir> --debug <some-data-dir>/azurite-debug.log
```

Default ports: Blob `10000`, Queue `10001`, Table `10002`.

## 2. Start the AIOWebScraper Functions host

From the AIOWebScraper checkout:

```
func start --port 7071
```

This exposes `api/NewJob`, `api/GetStatus`, and `api/GetResults`, backed by
the `scrape-work` Azure Storage queue.

Required settings in `local.settings.json` (`Values`), names only:

- `AzureWebJobsStorage`
- `QueueStorageConnectionString`
- `blobStorageConnectionString`
- `tableStorageConnectionString`
- `residentialProxy`
- `twoCaptchaApiKey`

## 3. Start the ScraperWorker

From the AIOWebScraper checkout's `ScraperWorker` project:

```
dotnet run --no-build
```

Required settings in `appsettings.json` / `appsettings.local.json`, names
only:

- `queueStorageConnectionString`, `blobStorageKey`, `tableStorageConnectionString`
- `residentialProxy`
- `workerCount`
- `routing:allowedDomains` — **must include `mercari.com` and
  `www.mercari.com`** alongside any eBay domains already listed, or every
  Mercari fetch is aborted client-side by `RouteFilterService` before it ever
  reaches the network (this is a routing allowlist, not a network failure —
  it shows up as `net::ERR_FAILED` on every URL, including the bare Mercari
  homepage, and is easy to mistake for a proxy or Cloudflare problem)
- `routing:blockedResourceTypes`

### Off-screen browser requirement

AIOWebScraper's browser automation launches headed Chromium (Cloudflare
blocks fully headless browsers), which means a visible Chrome window can
briefly flash on screen during local development. There is currently no
built-in configuration to suppress this
([AIOWebScraper#149](https://github.com/ryan75195/AIOWebScraper/issues/149)
tracks a permanent fix). Until that lands, a local, uncommitted patch to
`AIOWebScraper.Unblocker/StealthBrowser.cs` that adds
`--window-position=-32000,-32000` to the launch args keeps the window
positioned off the visible desktop for every fetch except the interactive
session-capture flow. Do not commit this patch to the AIOWebScraper repo.

### Cloudflare challenges

A meaningful fraction of Mercari fetches are blocked by a Cloudflare/CAPTCHA
challenge rather than succeeding on the first attempt
([AIOWebScraper#148](https://github.com/ryan75195/AIOWebScraper/issues/148)).
The worker retries a few times per URL before giving up and dead-lettering
the job; a scheduled run may need more than one scheduler tick to produce a
successful fetch. This is expected, not a sign the stack is misconfigured.

## 4. Start MarketMakerEtl.Api

From `src/MarketMakerEtl.Api`:

```
dotnet run --no-launch-profile --urls http://localhost:5299
```

(`--no-launch-profile` avoids `launchSettings.json` silently overriding the
port.)

## 5. Start MarketMakerEtl.Etl

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
| `Scraper:BaseUrl` | AIOWebScraper Functions host base URL |
| `Scraper:ApiKey` | AIOWebScraper Functions API key, if configured |
| `Scraper:SessionReference` | Optional eBay session token; never sent for Mercari fetches |
| `ContentStore:ConnectionString` | Azure Storage connection string for scraped HTML blobs |
| `ContentStore:ContainerName` | Blob container name for scraped HTML |
| `Database:ConnectionString` | SQLite connection string for MarketMakerEtl's own database |
| `Schedule:TickMinutes` | How often `JobQueueingWorker` checks for due jobs |
| `Schedule:RefreshIntervalHours` | How often `ListingRefreshWorker` refreshes active listings |
| `Scrape:MaxPages` | Max eBay search result pages per direction |
| `Scrape:CollectSold` | Whether to also search sold listings |
| `Scrape:MaxBandsPerDirection` | Max Mercari price bands sliced per direction (active/sold) |
| `Scrape:MaxConcurrentDetailFetches` | Concurrency for item detail page fetches |
| `Scrape:MaxDetailFetchesPerRun` | Cap on item detail fetches per run |
| `Scrape:MaxDetailFetchAttempts` | Retry attempts per item detail fetch |

For a short local smoke test, set a low `Schedule:TickMinutes` (e.g. `1`) and
small `Scrape:MaxBandsPerDirection` / `Scrape:MaxDetailFetchesPerRun` values
(e.g. `6-10`) so a full run finishes in a few minutes instead of consuming a
large fetch budget.

## 6. Create a job and let the scheduler run it

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

Stop, in reverse order: MarketMakerEtl.Etl, MarketMakerEtl.Api, the
ScraperWorker, the Functions host, and Azurite. If you mapped a drive letter
to work around Windows' `MAX_PATH` limit while building AIOWebScraper from a
deeply nested path, remove it (`subst <drive>: /D`).
