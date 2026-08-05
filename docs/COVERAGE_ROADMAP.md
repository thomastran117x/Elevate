# Coverage Policy

This document tracks the coverage policy for both suites: an enforced gate on the backend, and a report-only baseline on the frontend.

# Backend

## Current State

- Enforced target: `90.00%` filtered backend unit line coverage
- Measurement command:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- backend-unit-coverage
```

- Latest pre-gate baseline measured during rollout: `89.85%` line coverage (`5714 / 6359`)

## Coverage Scope

The backend unit coverage gate uses `backend.coverage.runsettings` and counts filtered application code such as:

- services
- controllers
- repositories
- utilities and shared helpers
- DTOs and contracts when they contain behavior
- middleware, handlers, consumers, writers, publishers, and bootstrap code with runtime behavior

The filtered metric excludes:

- EF Core migrations
- migration designer files
- model snapshots
- `src/main/seeders/**`
- generated code marked by compiler or generated-code attributes

## Working Guidance

- prefer unit tests when the goal is moving the backend coverage number
- keep new tests branch-dense and behavior-focused
- use the coverage script locally before opening a PR when backend logic changes
- treat coverage regressions as test gaps to fix in the same change whenever practical

# Frontend

## Current State

- Enforced target: **none yet** — coverage is measured and reported, not gated
- Measurement command:

```powershell
cd frontend
npm run generate:env
npm test -- --watch=false --browsers=ChromeHeadlessCI --code-coverage
```

- Baseline measured when reporting was introduced (520 specs):

| Metric | Baseline |
| --- | --- |
| Statements | `78.44%` (2257 / 2877) |
| Branches | `77.01%` (1749 / 2271) |
| Functions | `81.55%` (774 / 949) |
| Lines | `78.67%` (2154 / 2738) |

CI runs the same command and uploads `frontend/coverage/` as the `frontend-coverage` artifact; the `text-summary` reporter also prints these numbers into the job log.

## Coverage Scope

Karma instruments only the files reachable from a spec, so the denominator grows as new areas gain their first test. A percentage that dips after adding specs for a previously untested area is expected and is not a regression — compare absolute covered lines as well as the ratio.

`src/testing/**` is excluded via `codeCoverageExclude` in `angular.json`.

## Ratchet Plan

`frontend/karma.conf.js` already carries a `coverageReporter.check.global` block with every threshold at `0`. Raising those values is the single change needed to turn reporting into a gate.

1. Report-only (current) — establish a stable baseline across a few PRs.
2. Set a floor comfortably below the baseline (around `70%` statements/lines) so the gate catches removals rather than blocking ordinary work.
3. Raise the floor as untested areas gain specs, matching how the backend reached its `90.00%` gate.

## Working Guidance

- new services, guards, interceptors and normalizers ship with a spec (see `docs/TESTING.md`)
- prefer specs on pure normalizers and core infrastructure — highest coverage per line and the likeliest silent regressions
- large presentational components are deliberately low priority; `should create` stubs inflate the number without catching anything
