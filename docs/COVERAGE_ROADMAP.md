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

- Enforced target: `90%` statements, lines, branches and functions
- Enforced by `coverageReporter.check.global` in `frontend/karma.conf.js`; the build exits non-zero when any metric drops below its floor
- Measurement command:

```powershell
cd frontend
npm run generate:env
npm test -- --watch=false --browsers=ChromeHeadlessCI --code-coverage
```

- Current state (819 specs):

| Metric | Covered | Floor |
| --- | --- | --- |
| Statements | `93.04%` (2677 / 2877) | `90%` |
| Branches | `91.67%` (2082 / 2271) | `90%` |
| Functions | `92.83%` (881 / 949) | `90%` |
| Lines | `92.98%` (2546 / 2738) | `90%` |

For reference, the baseline when coverage reporting was first introduced was `78.67%` lines / `77.01%` branches across 520 specs.

CI runs the same command and uploads `frontend/coverage/` as the `frontend-coverage` artifact; the `text-summary` reporter also prints these numbers into the job log.

## Coverage Scope

Karma instruments only the files reachable from a spec, so **the denominator grows as new areas gain their first test**. Adding a spec for a previously untested area pulls that whole file into the count, which can push the ratio down even though absolute coverage went up. When a percentage drops, compare covered counts as well as the ratio before treating it as a regression.

`src/testing/**` is excluded via `codeCoverageExclude` in `angular.json`.

## Working Guidance

- new services, guards, interceptors and normalizers ship with a spec (see `docs/TESTING.md`)
- prefer specs on pure normalizers and core infrastructure — highest coverage per line and the likeliest silent regressions
- for anything reading the dual camelCase/PascalCase contract, cover both casings plus the empty payload; those `??` chains are most of the branch count
- large presentational components are deliberately low priority; `should create` stubs inflate the number without catching anything
- raise the floors when the margin above them grows comfortable, the same ratchet the backend used to reach `90.00%`
