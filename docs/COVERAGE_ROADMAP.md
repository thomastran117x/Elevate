# Backend Coverage Policy

This document tracks the current backend unit coverage policy and the measurement scope behind the CI gate.

## Current State

- Enforced target: `90.00%` filtered backend unit line coverage
- Measurement command:

```powershell
.\bin\backend-unit-coverage.ps1
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
