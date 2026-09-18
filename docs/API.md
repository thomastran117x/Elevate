# API guide

The default local API base is `http://localhost:8090/api`. MVC controllers receive the `/api` prefix through the route convention. The frontend can use its same-origin `/api` proxy; OpenAPI document routes are outside that prefix.

## API reference

The generated [OpenAPI JSON](../backend/openapi.json) and [OpenAPI YAML](../backend/openapi.yaml) describe endpoint parameters, bodies, responses, and security requirements. The running API exposes `/openapi.json`, `/openapi.yaml`, `/openapi/v1.json`, and `/openapi/v1.yaml`. There is no configured Swagger UI.

Regenerate both committed formats from the repository root after API contract changes:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- export-openapi --port 8091
```

The task starts a temporary Development host, suppresses startup migrations/seeders/hosted services, uses SQLite and a no-op cache, writes both artifacts, and stops the host. The example uses an alternate port to avoid an API already listening on 8090. Export reflects the configured feature flags; review the generated diff before committing. See [development tasks](../tools/Event.DevTasks/).

## Responses and pagination

API response conventions use this envelope:

```json
{
  "success": true,
  "message": "Request completed.",
  "data": {},
  "error": null,
  "meta": null
}
```

On failure, `success` is false, `data` is null, and `error` contains a machine-readable `code` and optional `details`. HTTP status still indicates the result. Validation, authentication, authorization, and not-found handling use shared error conventions. Optional metadata can identify a response source. Binary responses, hub traffic, and proxy-generated failures are not the normal controller JSON contract.

Paginated responses can carry `items`, `totalCount`, `page`, `pageSize`, and `totalPages` inside `data`; pages are 1-based. Consult the endpoint schema for its query parameters and limits. Frontend normalizers intentionally accept camelCase and legacy PascalCase payload shapes; preserve that compatibility when touching them.

## Authentication, CSRF, and MFA

Protected HTTP endpoints use JWT bearer access tokens: `Authorization: Bearer <access-token>`. Refresh sessions use cookies; browser clients must retain cookies and send credentials on the relevant requests. Use the existing frontend interceptors and session flow rather than inventing an alternative token-storage contract.

Fetch `GET /api/auth/csrf` and send its request token in `X-CSRF-TOKEN`, with the antiforgery cookie retained. The antiforgery cookie is named `XSRF-TOKEN`. CSRF validation applies to the configured auth POST paths and state-changing profile requests; it is not a blanket rule for every API action. The endpoint schema and `CsrfConfiguration` define the protected requests.

Some operations require MFA step-up as well as a signed-in session. SMS and TOTP enrollment/step-up have separate configuration switches. Role and resource ownership checks remain authoritative on the backend. Email changes additionally verify the current password when applicable, require confirmation at the new address, and invalidate sessions on completion.

Captcha and external provider credentials are needed for their corresponding auth flows. Anonymous availability and suggestion endpoints are rate limited; integration tests suppress the normal request limiter, so their success does not verify production rate budgets.

## Feature flags and realtime

Disabled MVC features are removed during discovery, return the shared JSON 404, and disappear from OpenAPI. Frontend flags hide entry points and prevent disabled lazy routes from loading; frontend hiding does not replace backend authorization. See [configuration](CONFIGURATION.md).

Club realtime uses SignalR at `/api/hubs/clubs` for comments, presence, and typing. The hub is mapped separately from MVC and opts out of the global request timeout. Proxies must support WebSocket upgrades and long polling. Use the existing SignalR client and JWT configuration for connection authentication; hub methods are not REST actions listed by the controller reference.
