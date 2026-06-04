# Backend Coverage Roadmap

This document tracks the practical path from the current backend unit coverage baseline to a healthier target.

## Goal

- Near-term target: `80%` filtered backend unit line coverage
- Long-term target: `90%` filtered backend unit line coverage
- Current filtered baseline: `24.48%` line coverage, `18.11%` branch coverage

The current baseline is measured with:

```powershell
dotnet test backend.tests.Unit\backend.tests.Unit.csproj --settings backend.coverage.runsettings --collect:"XPlat Code Coverage" --results-directory TestResultsCoverage
```

## Coverage Policy

The filtered backend unit coverage metric should:

- include services
- include controllers
- include repositories
- include utilities and shared helpers
- include DTOs and request/response contracts when validation or mapping behavior exists
- include middleware, handlers, consumers, writers, publishers, and bootstrapping logic when they contain runtime behavior

The filtered metric should exclude:

- EF Core migrations
- migration designer files
- model snapshots
- `src/main/seeders/**`
- generated code marked by compiler or generated-code attributes

## Why We Are Far From 80%

The largest remaining unit-coverage gaps are not controllers or repositories. They are concentrated in the service layer and a few runtime support classes.

High-impact uncovered files include:

- `features/events/EventsService.cs`
- `features/events/search/EventSearchService.cs`
- `features/clubs/ClubService.cs`
- `features/events/invitations/EventInvitationService.cs`
- `features/clubs/search/ClubSearchService.cs`
- `consumers/ElasticsearchIndexMessageValidator.cs`
- `features/cache/CacheService.cs`
- `application/handlers/WebHandler.cs`

Controllers already have some coverage. Repositories and DTOs still matter, but service tests will move the percentage much faster.

## Roadmap

### Phase 1: Highest-Leverage Service Tests

Focus on the biggest files with dense branching and business rules.

Priority order:

1. `EventsService`
2. `ClubService`
3. `EventSearchService`
4. `ClubSearchService`
5. `EventInvitationService`

Expected test themes:

- authorization and visibility rules
- lifecycle transitions
- validation and failure paths
- cache invalidation
- search fallback behavior
- outbox and side-effect coordination
- batch operations
- private/public access rules

Primary output:

- new direct unit test classes under `backend.tests.Unit/Features/**`
- reusable harness/builders for mocking common collaborators

### Phase 2: Runtime Support and Infrastructure Behavior

Focus on medium-sized files that are branch-heavy and isolated enough for fast unit coverage gains.

Priority order:

1. `ElasticsearchIndexMessageValidator`
2. `CacheService`
3. `WebHandler`
4. `FileUploadService`
5. `Logger`
6. `OpenApiYamlSerializer`

Expected test themes:

- malformed input handling
- retry/fallback behavior
- serialization behavior
- request/response formatting
- error-path and boundary handling

### Phase 3: DTO and Contract Validation Coverage

DTOs should not be skipped when they enforce meaningful rules.

Focus areas:

- request DTOs with validation attributes
- contract objects with derived behavior or mapping expectations
- serialization-sensitive response models

Expected test themes:

- invalid length or missing field validation
- invalid enum and role handling
- malformed payload rejection
- mapping invariants for API responses

Preferred approach:

- use `Validator.TryValidateObject` for attribute-driven validation
- keep these tests fast and table-driven where possible

### Phase 4: Repository Backfill

Repository tests are still useful, but they are not the fastest way to climb from the current baseline.

Focus areas:

- repositories with query branching
- repositories with paging, ordering, and filtering logic
- repositories with soft-delete or status-aware behavior

Expected test themes:

- `AsNoTracking` query expectations where behavior matters
- paging and page-size clamping
- status filtering
- ownership and visibility filtering

## Suggested Work Batches

Use small batches that can land safely and measurably:

### Batch A

- `EventsService` unit tests
- `ClubService` unit tests

### Batch B

- `EventSearchService` unit tests
- `ClubSearchService` unit tests
- `ElasticsearchIndexMessageValidator` unit tests

### Batch C

- `EventInvitationService` unit tests
- `CacheService` unit tests
- `WebHandler` unit tests

### Batch D

- DTO validation suites for auth, events, clubs, and payment requests
- repository backfill for the most branch-heavy repositories

## Definition Of Done For Each Area

Before moving on from a target file or subsystem:

- cover at least the main success path
- cover the main authorization failure path
- cover the main validation failure path
- cover meaningful edge cases and branch conditions
- verify side effects such as cache removal, publish/write calls, or status changes

## Working Rules

- prefer unit tests when the goal is moving the unit coverage number
- use integration tests only when mocking would hide important behavior
- avoid writing thin tests that only prove a method can be called
- prefer branch-dense tests over broad but shallow happy-path-only tests
- track coverage after each batch instead of waiting for a large push

## Next Recommended Implementation Order

If we want the fastest practical climb from `24.48%`, start here:

1. `EventsService`
2. `ClubService`
3. `EventSearchService`
4. `ClubSearchService`
5. `EventInvitationService`

This sequence gives the best chance of moving the filtered unit metric materially before spending time on smaller backfill items.
