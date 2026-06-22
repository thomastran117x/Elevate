# Configuration

## Feature flags

EventXperience uses a code-owned feature flag registry to control whether backend endpoints, backend services, hosted services, and frontend routes are exposed.

### Canonical keys

The supported keys are:

- `auth`
- `clubs`
- `clubs.follow`
- `clubs.posts`
- `clubs.reviews`
- `clubs.versioning`
- `events`
- `events.analytics`
- `events.images`
- `events.invitations`
- `events.registration`
- `events.versioning`
- `payment`
- `profile.admin`
- `search`
- `search.reindex`

Unknown keys are intentionally rejected by backend parsing and tests so configuration drift fails fast.

### Inheritance and defaults

Flags use parent-child inheritance.

- Missing flags default to `true`.
- Setting a parent flag to `false` disables every descendant.
- Setting a child flag to `false` disables only that subfeature.

Examples:

- `events=false` disables all event routes, services, and frontend event routes.
- `events.invitations=false` keeps the rest of events enabled while removing invitation endpoints and UI.
- `search=true` with `search.reindex=false` keeps search online while hiding manual reindex operations.

### Environment variables

Deployments should use the flat environment variables defined in `.env.example`:

- `FEATURE_AUTH`
- `FEATURE_CLUBS`
- `FEATURE_CLUBS_FOLLOW`
- `FEATURE_CLUBS_POSTS`
- `FEATURE_CLUBS_REVIEWS`
- `FEATURE_CLUBS_VERSIONING`
- `FEATURE_EVENTS`
- `FEATURE_EVENTS_ANALYTICS`
- `FEATURE_EVENTS_IMAGES`
- `FEATURE_EVENTS_INVITATIONS`
- `FEATURE_EVENTS_REGISTRATION`
- `FEATURE_EVENTS_VERSIONING`
- `FEATURE_PAYMENT`
- `FEATURE_PROFILE_ADMIN`
- `FEATURE_SEARCH`
- `FEATURE_SEARCH_REINDEX`

The backend also supports a `FeatureFlags` configuration section, but the flat env vars are the canonical deploy-time format because they are shared with the frontend environment generation step.

### Backend behavior

Backend feature flags affect three layers:

- MVC discovery: `[FeatureGate("...")]` removes disabled controllers and actions before endpoint mapping, so disabled endpoints return the standard JSON 404 payload and disappear from OpenAPI.
- Dependency injection: feature-specific services are registered only when their feature is enabled, with disabled fallbacks where needed.
- Hosted services: background workers such as search initialization, club version cleanup, and invitation status consumption are registered only when their feature is enabled.

### Frontend behavior

Frontend flags are generated at build time into `src/environments/environment.ts` by `frontend/scripts/generate-env.mjs`.

- Route trees use `canMatch` guards so disabled lazy features are never loaded.
- Subfeature routes use the same inheritance rules as the backend.
- Hidden features should also have their UI entry points removed from navigation and landing pages.
- Disabled URLs should fall through to the client-side not-found route.

### Deployment rule

Backend and frontend flags must be configured together for each deploy.

If the backend disables a feature but the frontend build still exposes it, users will see broken entry points. If the frontend disables a feature but the backend leaves it enabled, the feature may still be reachable directly. Treat the env vars as a single deploy-time contract across both applications.
