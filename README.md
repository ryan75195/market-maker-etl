# MarketMakerEtl

ETL + API solution scaffolded from the [dotnet-agent-harness](https://github.com/ryan75195/dotnet-agent-harness) `etl-api` template.

## First-time setup

After scaffolding (`dotnet new etl-api -n MarketMakerEtl`), run once:

```powershell
.\setup.ps1
```

This initializes a git repo, activates `.githooks/` for the project lifecycle, and creates the initial commit. Codex hook config is included under `.codex/` for edit-time branch guards.

## Build and test

```powershell
dotnet restore
dotnet build
dotnet test
```

## Running locally

`scripts/run-local.ps1` starts, stops, and reports the status of the local
stack (classifier sidecar, Api, Etl, and optionally a scraper start script).
Copy `scripts/run-config.example.json` to `.local/run-config.json` (gitignored)
and fill in machine-specific paths, then:

```powershell
./scripts/run-local.ps1 -Start
./scripts/run-local.ps1 -Status
./scripts/run-local.ps1 -Stop
```

See [docs/running-locally.md](./docs/running-locally.md) for configuration
details and the full manual pipeline (including the external scraper repo).

## Development lifecycle

See [CLAUDE.md](./CLAUDE.md) for the full lifecycle (issue → branch → commit → PR).

Quick summary:
1. `gh issue create --title "..."` (every change starts with an issue)
2. `git checkout -b feat/<issue-num>-<slug>` (`reference-transaction` hook verifies the issue exists)
3. Edit + commit (pre-commit hook runs build, format, tests)
4. `gh pr create` and squash-merge

Direct edits and commits to `main` are blocked. Edits to already-merged branches are blocked.
