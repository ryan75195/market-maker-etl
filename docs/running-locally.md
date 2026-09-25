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
- `routing:allowedDomains` — **leave this empty (domain filtering off) when
  scraping Mercari.** A non-empty list is a strict allowlist:
  `RouteFilterService.ShouldBlock` aborts *every* request — not just the
  top-level page navigation — to a host that isn't on the list. An empty
  list means "leave the bare Mercari domains on it" is not enough: Mercari's
  page load pulls in the Cloudflare beacon, Mercari's own asset CDN, a
  consent widget, and analytics hosts, and if any of those get aborted the
  Cloudflare JS challenge on the page fails, producing a genuine
  `Blocked: Captcha` verdict that looks exactly like a proxy or bot-detection
  problem but is actually self-inflicted. (A too-narrow allowlist, e.g. just
  the bare `mercari.com`/`www.mercari.com` hosts, also produces
  `net::ERR_FAILED` on every URL before a fetch even starts — same
  root cause, different symptom.) See
  [AIOWebScraper#150](https://github.com/ryan75195/AIOWebScraper/issues/150)
  for the evidence and a curated-allowlist alternative if you need domain
  filtering for other marketplaces at the same time as Mercari.

  **Setting `allowedDomains: []` in `appsettings.local.json` does not clear
  a populated list from an earlier-loaded config file.** ScraperWorker loads
  `appsettings.routing.json` (tracked in git, and may hold an allowlist for
  other marketplaces such as eBay) before `appsettings.local.json`. .NET's
  configuration system merges array-shaped settings **by index**, not by
  replacement: an empty array in a later-loaded file contributes no indexed
  keys at all, so it leaves every entry from the earlier file's array in
  place rather than overriding it. The only way to actually disable the
  allowlist for a local run is to empty the array in
  `appsettings.routing.json` itself, as a local, uncommitted edit (the same
  way the off-screen `StealthBrowser.cs` patch below is local and
  uncommitted) — an empty `appsettings.local.json` array cannot do it.
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

A meaningful fraction of Mercari fetches can be blocked by a
Cloudflare/CAPTCHA challenge rather than succeeding on the first attempt
([AIOWebScraper#148](https://github.com/ryan75195/AIOWebScraper/issues/148)).
The worker retries a few times per URL before giving up and dead-lettering
the job; a scheduled run may need more than one scheduler tick to produce a
successful fetch. Before assuming this is a proxy or bot-detection problem,
check `routing:allowedDomains` first — see the gotcha above. A non-empty
allowlist that omits Cloudflare's beacon or Mercari's asset CDN reproduces a
Cloudflare block on nearly every fetch and is easy to mistake for a genuine
anti-bot problem.

### Mercari search fetches sometimes land on a Cloudflare challenge page

A meaningful fraction of Mercari search fetches (roughly a third, in a
captured live run) come back as a Cloudflare "Just a moment..." challenge
page — a ~28KB HTML document with a `challenge-platform` script and no item
cards or JSON payload — rather than a genuine search response. The scraper
reports this fetch as a plain 200 success, so nothing at the HTTP layer
distinguishes it from a real page.

A genuine Mercari search result always arrives either as a JSON payload
containing `data.search` (with `count` and `itemsList`, `count: 0` and an
empty `itemsList` being a real empty result) or as rendered HTML containing
at least one `data-testid="ItemContainer"` card. `MercariSearchParser.Parse`
only accepts those two shapes; anything else — a challenge page or any other
unrecognised page — throws `UnrecognisedSearchPageException` instead of
silently returning an empty `SearchPageResult`. `SearchPageFetcher` retries
that exception like any other transient failure: up to
`Scrape:SearchPageMaxAttempts` attempts (default 5) with an increasing delay
between attempts (roughly 5s/15s/30s/60s at the default
`Scrape:SearchPageRetryBaseDelaySeconds` of 5), since challenge pages tend to
cluster in time and a short wait is often enough for the next attempt to get
a real page. If every attempt for a band comes back as a challenge (or other
unrecognised) page, the band is recorded as a `SearchPageFailed` issue whose
message is "Cloudflare challenge page" — distinguishing it from a genuinely
empty search, which is never retried and never recorded as an issue — while
every other band's results are kept.

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
| `ContentStore:ContainerName` | Blob container name for scraped HTML — **must be exactly `html`**. AIOWebScraper's `AzureJobRepository` hardcodes `blobs.GetBlobContainerClient("html")` when it writes scraped content; any other value here makes `BlobScrapeContentStore.BuildBlobName` throw `Blob uri '...' does not point into the '<configured>' container` even though the underlying fetch succeeded |
| `Database:ConnectionString` | SQLite connection string for MarketMakerEtl's own database |
| `Schedule:TickMinutes` | How often `JobQueueingWorker` checks for due jobs |
| `Schedule:RefreshIntervalHours` | How often `ListingRefreshWorker` refreshes active listings |
| `Scrape:MaxPages` | Max eBay search result pages per direction |
| `Scrape:CollectSold` | Whether to also search sold listings |
| `Scrape:MaxBandsPerDirection` | Max Mercari price bands sliced per direction (active/sold) |
| `Scrape:MaxConcurrentDetailFetches` | Concurrency for item detail page fetches |
| `Scrape:MaxDetailFetchesPerRun` | Cap on item detail fetches per run |
| `Scrape:MaxDetailFetchAttempts` | Retry attempts per item detail fetch |
| `Scrape:SearchPageMaxAttempts` | Max attempts per Mercari/eBay search-page fetch before recording a `SearchPageFailed` issue (Cloudflare challenge pages are retried like any other unrecognised-page failure) |
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
