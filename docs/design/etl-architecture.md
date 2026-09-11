# MarketMakerEtl — ETL architecture and migration decisions

Rebuild of the AIOMarketMaker data-collection path (`ryan75195/aio-market-maker`) as a
standalone harness-scaffolded service. This document records the architecture we are
deliberately building, and where it departs from the code being migrated.

## Scope

In: eBay search discovery, listing HTML fetch via the AIOWebScraper service, parsing, and
persistence of listings plus run status/history.

Out: taxonomy, variant matching, pricing/predictions, embeddings/vector search, chat, and
the Electron desktop client. Those stay in the old repository for now.

## Source to target mapping

| Source (AIOMarketMaker) | Target (MarketMakerEtl) | Treatment |
| --- | --- | --- |
| `Core/Parsers/EbaySearchParser.cs`, `EbayListingParser.cs` | `Core.Parsers` | Port, then split to satisfy method/class limits |
| `Core/Utils/DescriptionCleaner.cs`, `StringParsing.cs` | `Core.Utils` | Port |
| `Core/Models/Ebay/*` | `Core.Models` | Port as records |
| `Core/Services/WebscraperClient.cs` (+ old HTTP client in history) | `Core` scrape client | Rebuild as standalone HTTP contract |
| `Core/Services/Pipeline/*`, `ScrapeJobProcessor.cs` | `Core` commands + `Etl` worker | Re-architect |
| `Core/Data/*`, `Data/Migrations/SqlServer/*` | `Core.Data` + EF Core migrations | Re-model, replace migration runner |
| `Api/Endpoints/{Scrape,Job,History,BatchHistory,Category}` | `Api` endpoints | Port, thin |
| `Api/Services/{NightlyScrapeService,StartupRecoveryService}` | `Etl` worker | Re-architect |
| parser/contract tests | `Tests.Unit` / `Tests.Integration` | Port |

## Decisions

### 1. Standalone scraper contract (no sibling assembly)

The old client referenced `AIOWebScraper.Storage.Azure` for `JobEntity`, `JobItemEntity`,
`JobStatusType` and the blob-path convention. The new repository owns its wire contract:
its own request/response records and enum, and a blob-path helper. The scraper is reached
over HTTP only (`api/NewJob`, `api/GetStatus`, `api/GetResults`), exactly as the original
client did before the package extraction. The seam is an interface so the transport stays
swappable and testable without a live scraper.

Encoding of the blob name must match the scraper's convention; a contract test pins it.

### 2. Runtime split: Api enqueues and reads; Etl executes

The API never runs a scrape. It creates work and serves read models. The ETL host executes
runs. The two are independent entry points over a shared Core; the architecture tests
already forbid Api→Etl and Etl→Api references, which enforces the split.

This removes the old design's in-request pipeline execution and fire-and-forget work.

### 3. A validated run state machine

Replace the old four free-form status strings with one governed state type and an explicit
transition table owned by a single component. The terminal states are exhaustive:
completed, completed-with-errors, failed. No status literal may appear outside the state
definition; a custom architecture test enforces this.

### 4. One unit of work per run, short-lived scopes

Each run is processed as a unit with a freshly resolved scope and context. No long-lived
tracked context, no change-tracker clearing. Persistence is idempotent upsert keyed on the
natural listing id, so re-running a scrape is safe.

### 5. Failure, deadline and cancellation as first-class

Every scrape fetch takes a cancellation token and an overall run has a deadline. A failure
is terminal and persisted with its cause; it is never swallowed. An empty search is a
success, not a failure. A run whose analysis-like post-step fails completes with errors
rather than reporting clean success.

### 6. Design to the harness limits

Methods are at most 40 effective lines and classes at most 200. This mandates the
decomposition the old code lacked: the parser types split into focused extractors, and the
scrape orchestration splits into small commands. Splits are by responsibility, not just to
satisfy the counter.

### 7. EF Core migrations against a new database

The old hand-written SQL migrations and the regex transpiling runner are not carried over.
The new service uses EF Core migrations and its own database.

### 8. Configuration

Scraper base URL and API key, scrape concurrency, and the run deadline. No analysis
configuration — analysis is out of scope for this repository.

### 9. Observability

A correlation id per run is threaded through the fetch calls and logs.

## Custom architecture tests to add

Beyond the scaffold's defaults, the following pin the decisions above so they cannot decay:

- no change-tracker clearing anywhere in Core or Etl;
- no status string literals outside the state definition;
- every scrape-fetch interface method declares a cancellation token;
- the Api assembly references no persistence types;
- the scrape client is only constructed behind its interface;
- run state transitions reject an illegal transition.

## Open questions

- Reuse the existing database rows, or start empty? Default assumes empty.
- Keep the 40/200 limits as-is (default applies).
