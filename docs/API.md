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

## Human-authored request text

JSON body fields that persist human-authored text are plain text. Before the normal required and
length rules run, the API trims outer whitespace and changes CRLF or CR line endings to LF. It keeps
interior whitespace, ordinary Unicode, accents, and valid emoji. Fields such as names, titles,
locations, cities, tags, addresses, and image alt text are single-line; descriptions, posts,
comments, replies, reviews, registration notes, and dietary needs may contain LF line breaks.

The API rejects HTML angle brackets, unsupported control or text-direction characters, and a
conservative server-side list of English prohibited words and phrases. Matching is case-insensitive
and uses complete words or phrases rather than substrings, so a harmless longer word is not refused.
Failures return `400` with code `VALIDATION_ERROR` and the affected field in `error.details`; the
response does not repeat the submitted text or reveal the prohibited-term list.

Passwords, email addresses, phone numbers, URLs, upload metadata, OAuth data, tokens, codes,
CAPTCHA values, enum/date/time values, invitation identifiers, and search filters keep their
specialized validation and are not transformed by this policy. In particular, passwords and tokens
remain byte-for-byte identical. Username moderation runs only when a username is created or
changed; login and lookup still accept historical usernames. Existing stored rows and version
snapshots are not rewritten. Output remains plain text and must continue to be rendered with the
frontend's normal escaping rather than as HTML.

## Authentication, CSRF, and MFA

Protected HTTP endpoints use JWT bearer access tokens: `Authorization: Bearer <access-token>`. Refresh sessions use cookies; browser clients must retain cookies and send credentials on the relevant requests. Use the existing frontend interceptors and session flow rather than inventing an alternative token-storage contract.

Fetch `GET /api/auth/csrf` and send its request token in `X-CSRF-TOKEN`, with the antiforgery cookie retained. The antiforgery cookie is named `XSRF-TOKEN`. CSRF validation applies to the configured auth POST paths and state-changing profile requests; it is not a blanket rule for every API action. The endpoint schema and `CsrfConfiguration` define the protected requests.

Some operations require MFA step-up as well as a signed-in session. Username, password, and email changes, account deletion, and every SMS or TOTP enrollment, enable, disable, or remove call need a step-up proof. The proof comes from `POST /api/auth/mfa/step-up/start` and `/verify` (TOTP, SMS, or the always-available email fallback). It is bound to the session and its auth version and expires a fixed 10 minutes after verification (`Auth:StepUp:ProofLifetimeMinutes`). Without it, these routes return `403` with error code `MFA_REQUIRED`. The step-up `options`, `start`, and `verify` routes never require a proof. Logout, credential rotation, and any completed MFA factor change discard the proof. New sensitive mutations, including future provider, session, device, or recovery-code changes, must use `[RequireMfa]` and be added to the registry in `backend.tests.Unit/Application/Security/SensitiveEndpointStepUpTests.cs`. SMS and TOTP enrollment/step-up have separate configuration switches. Role and resource ownership checks remain authoritative on the backend. Email changes additionally verify the current password when applicable, require confirmation at the new address, and invalidate sessions on completion.

### Passwords

Signup (`POST /api/auth/signup`), reset (`POST /api/auth/reset-password` and `/api/auth/change-password`), and authenticated change (`POST /api/profile/change-password`) accept 15–128 characters with no composition rules. A length violation fails model validation. A common, breached, or account-derived value is rejected by the service. Both return `400` with code `VALIDATION_ERROR` and `details` keyed by the request field (`Password` or `NewPassword`), holding a single message. A rejected reset password leaves the emailed link or code usable for another attempt. A successful reset or change bumps `AuthVersion` and revokes every refresh session. Login (`POST /api/auth/login`) never applies the policy. See [password policy and hashing](CONFIGURATION.md#password-policy-and-hashing).

### OAuth provider sign-in

Google (`POST /api/auth/google`, `/api/auth/google/code`) and Microsoft (`POST /api/auth/microsoft`) sign-in first look up the account linked to the provider subject. A linked subject signs in whatever address the provider now reports.

Otherwise the provider's email is matched against existing accounts:

- **No match.** The response asks for role selection, and `POST /api/auth/oauth/complete` creates the account.
- **The account is linked to a different Google or Microsoft identity.** The request answers `409`, and nothing is linked.
- **The account has no provider link.** The subject is linked to it, and the caller is signed in. This happens only when the provider proves the caller controls the address.

Google requires `email_verified`. For Microsoft:

- The email comes only from the ID token's `email` claim, never from `preferred_username`, which a tenant administrator sets.
- The email counts as verified when the token is from the personal-account (consumer) tenant `9188040d-6c67-4c5b-b112-36a304b66dad`, or when the `xms_edov` claim is true. See [configuration](CONFIGURATION.md#addresses-and-public-values).
- An unverified address that matches an existing account answers `409` with error code `OAUTH_EMAIL_UNVERIFIED`. The account is neither linked nor signed in.
- An unverified address with no matching account can still create a new account.
- A token with no `email` claim signs in only with a subject that is already linked. Otherwise it answers `401`.

Completing a pending signup re-runs these checks.

## Authorization

Every endpoint requires an authenticated bearer user unless it explicitly opts out. `AddJwtAuth` sets both the default and the fallback authorization policy to "authenticated Bearer user", so an action, hub, or minimal endpoint without an authorization attribute fails closed rather than open.

- **Public endpoints** carry `[AllowAnonymous]` on the action (or `.AllowAnonymous()` on a minimal endpoint). Put it on the action, not the controller: a class-level `[AllowAnonymous]` silently overrides every `[Authorize]` beneath it. The intended public surface covers:
  - sign-in, sign-up, verification, OAuth, session refresh and logout, and recovery;
  - invitation resolution;
  - public profiles;
  - event and club reads;
  - the Stripe webhook;
  - the OpenAPI document routes;
  - the club realtime hub, whose methods each re-run the REST read gate.
- **Role-restricted endpoints** use the named policies in `AuthorizationPolicies` (`AdminOnly`, `OrganizerOnly`) instead of string literals.
- **Resource rules** are enforced in the services and are not expressed as policies: club ownership, manager and volunteer staff roles, event registration, and invitations.
- **Responses:**
  - A missing, invalid, or expired token answers `401` with error code `UNAUTHORIZED`.
  - A failed policy answers `403` with error code `FORBIDDEN` and the message "You do not have permission to access this resource". A service-level ownership denial uses the same code with its own message.
  - A route that matched nothing, including one removed by a disabled feature flag, still answers the JSON `404` to anonymous callers.
- **OpenAPI:**
  - Protected operations carry the `bearerAuth` security requirement and `401`/`403` responses.
  - Every operation carries an `x-authorization` extension: `anonymous`, `authenticated`, or the required policy name.
- **Audit:** `backend.tests.Unit/Application/Security/EndpointAuthorizationAuditTests.cs` fails when:
  - an action or hub method is unclassified;
  - an `[Authorize]` action sits under a class-level `[AllowAnonymous]`;
  - the anonymous surface differs from its reviewed `ExpectedAnonymousEndpoints` list.

  Add a new public endpoint to that list in the same change.

Captcha and external provider credentials are needed for their corresponding auth flows. Login omits CAPTCHA until the generic 401 envelope reports `meta.captchaRequired: true`; the next submission must include a fresh token. Login failures and sensitive delivery/verification operations use independent Redis-backed source-IP and hashed-target limits, with per-replica IP limiting retained during Redis outages.

Email availability uses `POST /api/auth/email/availability` with CSRF and `{ "email": "...", "captcha": "..." }`. The response envelope is unchanged. `GET /api/auth/email/availability?email=...` remains deprecated for one compatibility release, requires the CAPTCHA token in `X-Captcha-Token`, and returns `Deprecation` plus successor `Link` headers; CAPTCHA tokens are never accepted in the query string. Availability and suggestion endpoints are rate limited; a throttled or failed availability probe is advisory and must not be interpreted as either available or taken.

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

A save that attaches uploads can answer 409 with error code `MEDIA_PROCESSING`, "This image is still being checked". This happens when `storage.quarantine.inline` is off and media-worker has not given its verdict within the wait. It is not a refusal: validation continues. Follow the asset with this endpoint, then repeat the attach once it is ready. Any other 409 or 400 on an attach is a refusal.

It exists for an editor waiting on an image it just attached. It uses the global per-user rate limit rather than the image-upload policy, so polling does not use up upload allowance. The frontend polls every 1.5 seconds for the first 10 seconds, then every 4 seconds up to a minute. After that it shows "Still processing" and polls every 15 seconds, for up to ten minutes. Attach currently completes validation before it returns, so the first read after a successful attach already reports ready.

When quarantine is off, `mediaAssetId` is null, `GET /api/media/{publicId}` does not exist, and the uploaded bytes are what `publicUrl` serves. Those bytes are checked only for size and file signature when attached, and they are not re-encoded.

## Feature flags and realtime

Disabled MVC features are removed during discovery, return the shared JSON 404, and disappear from OpenAPI. Frontend flags hide entry points and prevent disabled lazy routes from loading; frontend hiding does not replace backend authorization. See [configuration](CONFIGURATION.md).

Club realtime uses SignalR at `/api/hubs/clubs` for comments, presence, and typing. The hub is mapped separately from MVC and opts out of the global request timeout. Proxies must support WebSocket upgrades and long polling. Use the existing SignalR client and JWT configuration for connection authentication; hub methods are not REST actions listed by the controller reference.
