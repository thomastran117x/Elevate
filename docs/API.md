# API guide

The default local API base is `http://localhost:8090/api`. MVC controllers receive the `/api` prefix through the route convention. The frontend can use its same-origin `/api` proxy; OpenAPI document routes are outside that prefix.

## API reference

The generated [OpenAPI JSON](../backend/openapi.json) and [OpenAPI YAML](../backend/openapi.yaml) describe endpoint parameters, bodies, responses, and security requirements. The running API exposes `/openapi.json`, `/openapi.yaml`, `/openapi/v1.json`, and `/openapi/v1.yaml`. There is no configured Swagger UI.

Regenerate both committed formats from the repository root after API contract changes:

```powershell
dotnet run --project tools/Event.DevTasks/Event.DevTasks.csproj -- export-openapi --port 8091
```

The task starts a temporary Development host, suppresses startup migrations/seeders/hosted services, uses SQLite and a no-op cache, writes both artifacts, and stops the host. The example uses an alternate port to avoid an API already listening on 8090. Export reflects the configured feature flags; review the generated diff before committing. See [development tasks](../tools/Event.DevTasks/README.md).

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

## Image uploads

`POST /api/profile/avatar` decodes the uploaded image and re-encodes it before storing it. The stored avatar is always WebP, and its URL ends in `.webp` whatever the uploaded file was called or what format it was in. By default the long edge is reduced to at most 512 pixels (smaller images are not enlarged), the EXIF orientation is applied, and EXIF (including GPS), IPTC, XMP, and ICC metadata are removed.

JPEG, PNG, WebP, and GIF are accepted. A static GIF is converted to WebP. **Animated images (GIF, WebP, or APNG) are rejected with 400** and the message "Animated images are not supported. Upload a single-frame image." Converting them would keep only the first frame. Images wider or taller than 8000 pixels, or larger than 50 megapixels, are also rejected with 400. That check reads only the file header, so an oversized image is never decoded. Every size limit here is a default that a deployment can change; the settings are listed under [image processing](CONFIGURATION.md#image-processing).

Processing runs on a small number of slots shared by the whole process. An upload that waits longer than the configured slot timeout is rejected with 503 and can be retried. Uploads racing each other for one account can end in 409 when each keeps replacing the other; that is retryable as well. A request that outlives the server's request-timeout policy gets the usual 504. A client that disconnects part-way is recorded as 499 for the access log; nothing is sent, because the connection is already gone.

Event, club, and series images use a presigned upload instead:

1. `POST /api/events/images/presigned-url` returns a write-once `uploadUrl`, the `publicUrl` to attach, and a `mediaAssetId`.
2. The browser PUTs the file to `uploadUrl` with an `x-ms-blob-type: BlockBlob` header.
3. The client attaches `publicUrl`, through `POST /api/events/{eventId}/images` or in an event, draft, series, or club payload.

When uploads are quarantined (`storage.quarantine`, on by default), the bytes land in a private container and nothing exists at `publicUrl` until the image has been attached. Attaching it validates the bytes and re-encodes them exactly as for avatars, but with a 2048-pixel long edge by default. The result is published at `publicUrl`, which therefore always ends in `.webp`. A refused image makes the attach request fail with 400 and the reason, for example "Animated images are not supported. Upload a single-frame image." The editors refuse an animated file as soon as it is picked, before anything is uploaded. Two other attach failures are retryable: 503 means no processing slot came free in time, and 409 means another request is checking the same upload. An upload that is never attached is expired after 24 hours. Clients should keep showing the file they uploaded until the image is live, rather than loading `publicUrl` early.

`GET /api/media/{publicId}` reports where an upload is, by its `mediaAssetId`, as `{ id, status, url, rejectionReason }`. `status` is a number, decoded by position: 0 pending upload, 1 uploaded, 2 processing, 3 ready, 4 rejected, 5 needs review. New values are only ever appended. `url` is set only when the image is ready, and `rejectionReason` only when it is rejected. The endpoint answers the uploader and managers of the club the upload was issued for; anyone else gets 404, whether or not the id exists.

It exists for an editor waiting on an image it just attached. It uses the global per-user rate limit rather than the image-upload policy, so polling does not use up upload allowance. The frontend polls every 1.5 seconds for the first 10 seconds, then every 4 seconds up to a minute. After that it shows "Still processing" and polls every 15 seconds, for up to ten minutes. Attach currently completes validation before it returns, so the first read after a successful attach already reports ready.

When quarantine is off, `mediaAssetId` is null, `GET /api/media/{publicId}` does not exist, and the uploaded bytes are what `publicUrl` serves. Those bytes are checked only for size and file signature when attached, and they are not re-encoded.

## Feature flags and realtime

Disabled MVC features are removed during discovery, return the shared JSON 404, and disappear from OpenAPI. Frontend flags hide entry points and prevent disabled lazy routes from loading; frontend hiding does not replace backend authorization. See [configuration](CONFIGURATION.md).

Club realtime uses SignalR at `/api/hubs/clubs` for comments, presence, and typing. The hub is mapped separately from MVC and opts out of the global request timeout. Proxies must support WebSocket upgrades and long polling. Use the existing SignalR client and JWT configuration for connection authentication; hub methods are not REST actions listed by the controller reference.
