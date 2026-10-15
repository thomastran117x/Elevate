# Configuration

Start from the tracked [.env.example](../.env.example), [backend appsettings](../backend/appsettings.json), and [Compose mappings](../docker-compose.yml). The template is a configuration inventory with development defaults, not production credentials. See [setup](SETUP.md) for preparing files.

## Loading and precedence

Compose reads the root `.env` for interpolation and optionally passes it through service `env_file`; explicit service `environment` entries take precedence over that file. Container hostnames such as `postgres`, `redis`, `kafka`, and `elasticsearch` resolve inside the Compose network.

Outside containers, backend `EnvironmentSetting` searches from `AppContext.BaseDirectory` upward for `.env` files. It loads ancestors first and nearer files afterward, while preserving nonempty process environment values that existed before loading. It ignores blank assignments. Containers skip this file loading and rely on injected values. Settings captured by this static class require a restart when changed.

ASP.NET configuration combines appsettings, environment-specific appsettings, process variables, and command-line values through its providers. Some application settings are read directly from process environment by `EnvironmentSetting` instead of exclusively through `IConfiguration`; consult the consuming options class when overriding them. Standard nested environment keys use double underscores.

The frontend generator runs in `frontend/` and uses dotenv's local `.env` plus process variables (process values win). It writes public values into `src/environments/environment.ts`. It does not load the root file automatically. Run generation before builds, type checks, or unit tests; `npm start` and `start:e2e` do it automatically.

## Addresses and public values

| Setting                     | Purpose                                                                |
| --------------------------- | ---------------------------------------------------------------------- |
| `FRONTEND_URL`              | Public frontend origin; also used in backend/email links               |
| `BACKEND_URL`               | Browser API base baked into the frontend                               |
| `CORS_ORIGIN`               | Browser origin accepted by the Compose backend CORS policy             |
| `API_PROXY_TARGET`          | Backend origin used by the dev/SSR proxy; supply to the server process |
| `FRONTEND_BACKEND_URL`      | Compose override for the frontend's browser API base                   |
| `FRONTEND_API_PROXY_TARGET` | Compose override for its proxy upstream                                |
| `BACKEND_PORT`              | Compose maps this to backend `PORT` (default 8090)                     |
| `NG_ALLOWED_HOSTS`          | SSR allowed hostnames, comma-separated without ports                   |

Backend URL selection reads `PORT`, then `ASPNETCORE_PORT`, then 8090. The built frontend SSR server reads `PORT`, defaulting to 3090; the Angular dev server is configured at 3090. See [setup's address table](SETUP.md#full-docker-stack) for exposed infrastructure ports and the Kafka advertised-listener limitation.

Generated public values are `NODE_ENV` (production flag), `BACKEND_URL`, `FRONTEND_URL`, `GOOGLE_CLIENT_ID`, `MSAL_CLIENT_ID`, `GOOGLE_SITE_KEY`, and frontend feature flags. JWT secrets, OAuth client secrets, SMTP/Twilio credentials, storage connection strings, and payment secrets must never enter that bundle.

Compose maps `MSAL_CLIENT_ID` to backend `MS_CLIENT_ID`. A locally run backend must supply `MS_CLIENT_ID` explicitly for Microsoft OAuth.

Work and school Microsoft accounts link to an existing account by email only when the ID token includes the `xms_edov` optional claim set to true. To allow that, add `xms_edov` to the ID token's optional claims in the Entra app registration. Without the claim, those users can still sign up with a new account or sign in with a subject that is already linked. Personal Microsoft accounts need no extra configuration. See [OAuth provider sign-in](API.md#oauth-provider-sign-in).

## Infrastructure and integrations

| Area                    | Principal settings                                                                                          |
| ----------------------- | ----------------------------------------------------------------------------------------------------------- |
| Database                | `DB_CONNECTION_STRING`; app configuration also supports `Database:ConnectionString` and `Database:Provider` |
| Redis                   | `REDIS_URL` (legacy fallback `REDIS_CONNECTION`)                                                            |
| Search                  | `ELASTICSEARCH_URL`; Elasticsearch options in appsettings                                                   |
| Kafka                   | `KAFKA_BOOTSTRAP_SERVERS`; worker-specific topic/group/DLQ settings                                         |
| JWT                     | `JWT_SECRET_ACCESS`, `JWT_SECRET_VERIFICATION`; legacy access-secret fallback `JWT_SECRET_KEY`              |
| Google OAuth            | `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`                                                                  |
| Microsoft / Apple OAuth | `MS_CLIENT_ID`, `MS_TENANT_ID`, `APPLE_CLIENT_ID`                                                           |
| Captcha                 | `CAPTCHA_PROVIDER`, `GOOGLE_CAPTCHA_SECRET`, non-production-only `CAPTCHA_ALLOW_BYPASS`; public `GOOGLE_SITE_KEY` |
| Email                   | `SMTP_SERVER`, `SMTP_PORT`, `EMAIL_USER`, `EMAIL_PASSWORD`                                                  |
| SMS                     | `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, plus messaging service SID or sender phone                       |
| Azure blobs             | `AZURE_STORAGE_CONNECTION_STRING`, `AZURE_STORAGE_CONTAINER_NAME`, `AZURE_STORAGE_QUARANTINE_CONTAINER_NAME` (default `event-assets-quarantine`) |
| Stripe                  | `STRIPE_API_KEY`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_SUCCESS_URL`, `STRIPE_CANCEL_URL`                        |
| Seeding                 | `RUN_SEEDERS`, `AUTH_SEED_ACCOUNT_BYPASS`, and seed account variables                                       |
| Logging                 | `LOG_LEVEL` and backend/worker logger configuration; `Auth:SecurityEvents` for auth [security events](SECURITY_EVENTS.md) |

Default database provider is PostgreSQL. SQLite is used by OpenAPI export and selected isolated tests, not as the normal application database.

Email/SMS consumers remain idle if provider configuration is incomplete, and so does the media worker without storage settings. The media worker uses `MEDIA_VALIDATION_TOPIC`, `MEDIA_VALIDATION_GROUP_ID`, `MEDIA_VALIDATION_DLQ_TOPIC` and `MEDIA_VALIDATION_STATUS_TOPIC`. The API publishes to the first and consumes the last as `MEDIA_VALIDATION_STATUS_GROUP_ID`. Topic/group/DLQ defaults and failure policies are documented in [worker READMEs](../backend/README.md#workers). Keep notification topic names aligned between API publishers and consumers.

## Feature flags

The backend registry contains the following keys. Environment names are `FEATURE_` plus the uppercased key, with dots replaced by underscores. The hyphen in `storage.orphan-cleanup` is retained: its literal name is `FEATURE_STORAGE_ORPHAN-CLEANUP`; use the appsettings section or explicit environment mapping for shells that cannot assign that name normally.

| Area              | Backend keys                                                                                                                                                                 |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Auth / bloom      | `auth`, `bloom`                                                                                                                                                              |
| Clubs             | `clubs`, `clubs.discussions`, `clubs.follow`, `clubs.posts`, `clubs.reviews`, `clubs.versioning`                                                                             |
| Events            | `events`, `events.analytics`, `events.favourites`, `events.images`, `events.invitations`, `events.recurrence`, `events.registration`, `events.versioning`, `events.waitlist` |
| Payment / profile | `payment`, `profile`, `profile.admin`                                                                                                                                        |
| Search            | `search`, `search.reindex`                                                                                                                                                   |
| Storage           | `storage`, `storage.orphan-cleanup`, `storage.quarantine`, `storage.quarantine.inline`                                                                                       |

Missing flags default to true. A false parent disables its descendants. Backend parsing first reads registered process variables, then `FeatureFlags` configuration entries override those values. Unknown keys in that section are rejected; unrelated unknown process variables are not scanned/rejected. Backend boolean parsing is case-insensitive.

Frontend generation currently supports the club, auth, payment, profile, search, and event keys above **except** `events.recurrence`; it also omits `bloom` and storage keys. It accepts only literal `true`/`false` for configured booleans. These additional backend flags are absent from the root template's flag list and the shared Compose anchor; do not assume every registered backend flag has a matching frontend/build mapping.

Backend gates remove disabled MVC actions/controllers from discovery and OpenAPI and affect selected DI/hosted-service registrations. Frontend guards use `canMatch` for lazy routes; navigation should also hide disabled entry points. Configure shared flags together and rebuild the frontend when changing them.

## Security and application options

Use strong deployment-specific JWT secrets and a valid `AUTH_TOTP_ENCRYPTION_KEY` rather than development fallbacks. Production validation rejects missing/default critical values. SMS enrollment, enforcement, and step-up are controlled by `AUTH_SMS_MFA_ENROLLMENT_ENABLED`, `AUTH_SMS_MFA_ENFORCEMENT_ENABLED`, and `AUTH_SMS_MFA_STEP_UP_SMS_ENABLED`. TOTP has `AUTH_TOTP_MFA_ENROLLMENT_ENABLED` and `AUTH_TOTP_MFA_STEP_UP_ENABLED`.

Appsettings sections configure CORS, forwarded headers, request timeouts, rate limiting, profile username cooldown, version retention, recurrence, image upload limits, image processing, and orphan-blob cleanup. `Profile__UsernameChangeCooldownDays` is the normal nested override; Compose maps the template's uppercase `PROFILE__USERNAMECHANGECOOLDOWNDAYS` into it. Inspect options classes for accepted names and defaults rather than assuming all keys are flattened template variables.

### Image uploads

`ImageUpload:MaxBytes` caps an uploaded image at 5 MB by default. It is enforced when the image is attached to an event, draft, club, or recurrence series rather than when it is uploaded. Presigned uploads go from the browser straight to Azure, and a SAS has no content-length field, so nothing on the request path ever sees the bytes. At attach time the server reads the stored blob's length and leading bytes. It rejects anything empty, over the cap, not a supported image, or whose bytes disagree with the declared type, and deletes the blob. Re-attaching an image that an event or club already holds skips the check, so a later edit costs no storage round trip.

What happens after those checks depends on `storage.quarantine`:

- **On (the default).** The presigned URL points into the private quarantine container, and the `publicUrl` returned with it is reserved for the result. It ends in `.webp` whatever was uploaded. When the upload is attached, the server downloads at most the cap and re-encodes it (see [image processing](#image-processing)). It then writes the result to the reserved URL and deletes the quarantined original. Bytes that fail are deleted and never reach the public container. The upload's `MediaAssets` row records the outcome, and `GET /api/media/{publicId}` reports it. Nothing a client uploads is publicly readable before it has been validated.
  Where that re-encoding runs depends on `storage.quarantine.inline`. On (the default), it runs inside the attach request. Off, the API claims every image the save brings in and publishes a request for each to [media-worker](../backend/src/worker/media-worker/README.md). It then waits up to ten seconds, once, for all of their verdicts. If some do not arrive it returns 409, "still being checked", with error code `MEDIA_PROCESSING`. The event editor keeps such an image, follows `GET /api/media/{publicId}`, and attaches it once it is ready. Either way, a URL is saved onto an event, club, or series only once its asset is Ready. Compose sets the flag to false because it runs the worker.
- **Off.** The presigned URL points straight into the public container, and the checks above run against the stored blob in place, with the header restamping described below. This is exactly how uploads behaved before quarantine. Switching the flag off does not strand anything: the quarantine reaper stays on under the `storage` parent flag and drains what quarantine was holding. An upload issued before the switch can no longer be attached, and the client simply uploads again.

The presigned URL grants Azure's `Create` permission without `Write`, which makes it usable exactly once. `Put Blob` accepts either permission to create a new blob but requires `Write` to overwrite one, so the bytes that pass inspection are the bytes that stay there. Without quarantine, the stored content type is restamped from those bytes on attach. The SAS content type overrides only reads made through the SAS, while the anonymously readable public URL is served with whatever type the uploader set on its own PUT. With quarantine, the public blob is written by the server with `image/webp`, so no uploader-chosen header is ever served.

Quarantined uploads that are never attached are expired after 24 hours, and their bytes are deleted, by the hourly quarantine reaper.

Raising the cap affects only what is accepted from that point on; images already attached are unaffected. The multipart avatar upload has its own compiled-in 5 MB limit in `AvatarUploadRequest` and does not read this setting. `RateLimiter:ImageUploadPermitLimit` (30 presigned URLs per 10 minutes per account) bounds how many blobs one account can create, which the size cap does not.

### Image processing

Multipart avatar uploads are decoded to pixels and re-encoded before anything is stored. So are presigned uploads, when `storage.quarantine` is on, at the moment they are attached: in the API, or in media-worker when `storage.quarantine.inline` is off. EXIF (including phone GPS), IPTC, XMP, ICC profiles, and any non-pixel payload hidden in the file are gone.

The pipeline reads the header first, rejecting oversized dimensions and animated images (GIF, WebP and APNG) before any pixel buffer is allocated. It then decodes a single frame and shrinks the image to the size cap for its kind. Next it applies the EXIF orientation (after shrinking, so the rotation never needs a second full-size buffer), converts the colours to sRGB through any embedded ICC profile, strips the metadata, and encodes lossy WebP.

Avatars and event or club images share the processing slots below, so attaching a batch of images competes with avatar uploads for them. With media-worker validating, event and club images use the worker's own slots instead. The worker reads the same `ImageUpload` and `ImageProcessing` settings, so set any overrides on both. Animated images are refused for event and club images too, now that they are processed. Without quarantine, presigned uploads are not processed at all.

| Key | Default | Purpose |
| --- | --- | --- |
| `ImageProcessing:MaxDimension` | `8000` | Largest width or height accepted, read from the header alone. |
| `ImageProcessing:MaxPixels` | `50000000` | Largest total pixel count; catches a size that is within `MaxDimension` on each side but still decodes to hundreds of megabytes. |
| `ImageProcessing:AvatarMaxEdge` | `512` | Long-edge cap for avatars. Smaller images are never upscaled. |
| `ImageProcessing:GalleryMaxEdge` | `2048` | Long-edge cap for event and club images (galleries, covers, club icons and banners). Smaller images are never upscaled. |
| `ImageProcessing:WebpQuality` | `82` | Lossy WebP quality, 1–100. |
| `ImageProcessing:MaxConcurrentOperations` | `2` | Images processed at once across the process; further requests wait. |
| `ImageProcessing:SlotWaitTimeoutSeconds` | `10` | How long an upload waits for a free slot before the request is shed with 503. |

**Sizing a container.** Peak memory for one image is roughly its decoded pixels, at up to 4 bytes each (about 200 MB at the default 50 MP `MaxPixels`), plus the buffered upload, and `MaxConcurrentOperations` multiplies it. Sequential decoding often needs less, but size for the worst case. At the defaults, budget roughly 512 MB of headroom for image processing on top of the rest of the application, or lower `MaxPixels` to cut the per-image peak in proportion. Uploads that find both slots busy wait up to `SlotWaitTimeoutSeconds` and are then shed with 503 rather than queueing indefinitely, since the rate limiter is per account and does not bound the total.

Values are validated on startup. Processing uses [libvips](https://www.libvips.org/) through [NetVips](https://github.com/kleisauke/net-vips) (MIT). libvips itself is LGPL-2.1-or-later and is dynamically linked from the self-contained binaries in the `NetVips.Native` package, so containers need no extra system packages. ImageSharp was replaced because its advisories were fixed only in 4.x, which needs a Six Labors licence key to build. Only the JPEG, PNG, WebP and GIF loaders are ever called, chosen by the file signature, and every size check reads the header before any pixels are decoded. libvips is native code, so a parser fault on a hostile file could end the process (the API, or media-worker when it validates) instead of failing one request. Nothing here restarts it, and a message whose offset was never committed is delivered again, so that is a risk to watch rather than one the retry policy absorbs. The CI backend audit fails on high or critical NuGet advisories, so NetVips and `NetVips.Native` updates need to be taken promptly.

### Bloom filters and identity probes

The `BloomFilters` section controls refresh (30 seconds), rebuild (6 hours), retired generation TTL (60 minutes), local replay (30 minutes), forced-rebuild cooldown (15 minutes), and per-target expected items (250000) / false-positive rate (0.01). Registered targets are username and email; club-name is reserved, not an authoritative availability feature.

Filters combine local and Redis bitmaps. They can avoid database reads for definitely absent values but never authorize a write. Rebuilds rotate generations and replay concurrent additions; deleted values persist until rebuilding. With `bloom=false`, callers fall back to database checks.

Anonymous email availability exposes account existence and is intentionally protected by CAPTCHA plus independent Redis-backed source and normalized-email budgets. `Auth:AbuseProtection:EmailAvailabilityIpPermitLimit` defaults to 15/minute/IP and `EmailAvailabilityTargetPermitLimit` defaults to 5/15 minutes/email. The local `RateLimiter:EmailAvailabilityPermitLimit` remains active as a per-replica IP fallback. Username suggestions default to 10/minute/IP through `UsernameSuggestionsPermitLimit`.

### Authentication abuse protection

`Auth:AbuseProtection` is validated at startup and stores only SHA-256 hashes of type-tagged, normalized identifiers under the `auth:abuse:v1` prefix. Defaults are 10 shared source-IP attempts per five minutes, 10 login failures per normalized account per 15 minutes, 3 recovery sends per target per hour, and 5 verification, OAuth-completion, MFA-delivery, or email-availability attempts per target per 15 minutes. Login requires CAPTCHA after three failures and delays failed responses by 500 ms, 1, 2, 4, then 8 seconds (capped). A successful credential check clears the account failure state, including when MFA or device verification follows.

Redis is the cross-replica authority. If it is unavailable, shared source and target checks fail open while the existing ASP.NET in-memory IP policies continue enforcing on each replica; the transition emits one critical operational event and recovery emits an informational event. Each refused request is recorded as an `auth.throttled` [security event](SECURITY_EVENTS.md) with the hashed target, not the raw value. Configure the values directly with `Auth__AbuseProtection__...` or use the `AUTH_ABUSE_*` variables mapped by Compose.

### Authentication security events

Authentication outcomes are recorded as structured [security events](SECURITY_EVENTS.md) on the log and the `EventXperience.Auth` meter. `Auth:SecurityEvents` is validated at startup:

| Key | Default | Purpose |
| --- | --- | --- |
| `IdentifierHashKey` | development key | Base64 HMAC-SHA256 key, at least 32 bytes, for hashing usernames, emails, and client addresses in events. Required outside development and test, where a missing value or the public development key fails startup. |
| `LogSinkEnabled` | `true` | Write events to the log. |
| `MetricsSinkEnabled` | `true` | Count events on the `auth.security_events` instrument. |
| `IncludeSourceIpHash` | `true` | Add the hashed client address to events. |
| `IncludeTargetHash` | `true` | Add the hashed username or email to events that have no account id. |

Generate a key with `openssl rand -base64 32` and keep it as secret as the JWT keys. Configure it directly with `Auth__SecurityEvents__IdentifierHashKey` or through the `AUTH_SECURITY_EVENTS_HASH_KEY` variable mapped by Compose. Changing it breaks correlation between hashes made before and after the change.

### Refresh sessions

Refresh sessions live in Redis under `refresh:v2:session:{sessionId}` for 1 day, or 30 days with remember-me. Each session records the account's `AuthVersion` when it was issued or last rotated. A password reset, account disable, or email change increments `AuthVersion` and then deletes the account's sessions; that sweep removes nothing if Redis is unreachable, so `POST /api/auth/refresh` also refuses and deletes any session whose recorded version no longer matches the account. Sessions stored before the version was recorded skip that check and are stamped on their next refresh.

Every refresh rotates the refresh token. A token is `v1.{sessionId}.{generation}.{mac}`: an HMAC over the session id and generation, keyed by a random secret stored only in the session record. Rotation advances the session's generation through an atomic compare-and-set, so nothing is stored per token, and every earlier generation can still be recognised as genuine. Presenting an earlier generation is treated as reuse, the theft signal in the OAuth 2.0 refresh-token rotation guidance. The session is revoked together with its step-up proof, which kills every token derived from it, so whoever holds the newest one, whether user or attacker, has to sign in again. The replay is recorded as `auth.refresh_token.reused` and the revocation as `auth.session.revoked` with reason `token_reuse`; see [security events](SECURITY_EVENTS.md). The exception is the generation just rotated away, for 10 seconds after the rotation, and a concurrent refresh that loses the compare-and-set: those duplicates get 401 and the session is left alone, which covers tabs restoring at the same time and network retries. Writes on the refresh path only replace the exact session they read, so a revocation that lands mid-refresh is never undone. If Redis cannot advance the generation or confirm the revocation, the request fails with 503 and no new tokens are issued; a session that cannot be read is rejected with 401. A client that lost a refresh response and retries the old token later is signed out. Tokens issued before this format are random values stored under `refresh:v2:token:{hash}`. They are still accepted until they expire; the first refresh moves the session onto derived tokens and leaves one retired record behind, so a replay of the old token is detected the same way.

### Trusted devices

Completing sign-in verification trusts the device for 90 days: the `Devices` table stores a hash of the token, and the client receives it in the `trustedDevice` cookie (browsers) or the `X-Trusted-Device` header. A trusted device skips sign-in verification. Both verification paths carry the account's `AuthVersion` from the moment the password was checked: the MFA sign-in challenge and the new-device email link (`device:pending:{token}`, 15 minutes). The device row is written only if that version still matches, compared while the `Users` row is locked in the same transaction. A password reset, account disable, or email change while verification is pending therefore yields neither a session nor a trusted device, and any session already issued for it is revoked. New-device links stored before the version was recorded are refused.

### Step-up verification

Sensitive account mutations (`[RequireMfa]` routes) need a step-up proof from a TOTP, SMS, or email code verified in the same session. The proof is stored transiently in Redis under `mfa:step-up-proof:{sessionId}`. It is bound to the user, the session, and the access token's auth version. `Auth:StepUp:ProofLifetimeMinutes` (Compose: `AUTH_STEP_UP_PROOF_LIFETIME_MINUTES`) defaults to 10 and is validated at startup to be between 1 and 10. The window is measured from verification and never slides. Logout and session revocation delete the session and its proof in one atomic Redis operation; if Redis cannot confirm that, the revocation fails instead of reporting success. A proof is only written while its refresh session still exists. An auth-version change makes it unusable. Each proof also records the committed SMS/TOTP enrollment state from the database, so a completed factor change retires older proofs as part of its own commit. The change also advances the user's `mfa:step-up-generation:{userId}` counter, which retires proofs on every session and refuses any verification already in flight; if that counter cannot be updated the request returns 503. When Redis cannot return the proof and generation together, the gate treats the proof as missing.

### Password policy and hashing

`Auth:PasswordPolicy` is validated at startup and follows NIST SP 800-63B. A new password needs 15–128 characters (`MinLength`, `MaxLength`), counted as Unicode code points after NFKC normalization. There are no composition rules: spaces, Unicode, pasted and generated values are accepted. Signup, password reset, and authenticated password change also reject values that are common or breached, repetitive, sequential, or keyboard walks, and values that contain the account's username, email local part, or email domain, or a `ContextTerms` entry (defaults `eventxperience` and `event xperience`; configured terms are added to these). Every rejection uses one generic message and never says which rule or list matched. Login never applies the policy, so existing passwords keep working.

The blocklist runs entirely in process; no password or hash is sent to an outside service. The bundled list is the SecLists Xato top 100,000 (MIT), embedded from `backend/src/main/features/auth/password/blocklist/common-passwords.txt`. Set `BlocklistPath` (Compose: `AUTH_PASSWORD_BLOCKLIST_PATH`) to merge a local file with one value per line, such as an offline export from a breach corpus. Set `UseEmbeddedBlocklist` to `false` to use only that file. Entries are compared after lowercasing and removing everything except letters and digits.

New hashes use `Hashing:Algorithm` `Argon2id`, with `Hashing:Argon2:MemoryKiB` 19456, `Iterations` 2, and `Parallelism` 1 (the OWASP baseline). They are stored as PHC strings (`$argon2id$v=19$m=…,t=…,p=…$salt$hash`) in the existing `Users.Password` column. Legacy BCrypt hashes (`$2a$`/`$2b$`) still verify. A successful login whose stored hash uses another algorithm or other parameters re-hashes the password, then swaps the hash in a single conditional update without changing `AuthVersion`. If the upgrade fails, the old hash is kept and the login still succeeds. `Hashing:Algorithm=Bcrypt` with `BcryptWorkFactor` (default 12) is a rollback lever only, because BCrypt ignores input after 72 bytes. Compose maps the hashing values to `AUTH_PASSWORD_HASH_*`. Raising Argon2 memory raises the memory each concurrent login needs.

The Angular forms mirror `MinLength` and `MaxLength` as constants in `frontend/src/app/features/auth/validators/password-policy.validator.ts`, so change both together.

### Email changes

`POST /api/profile/email` requires MFA step-up and the current password where applicable, then sends confirmation to the new address and a notice to the old one. `RateLimiter:EmailChangePermitLimit` defaults to 3/hour/account. Confirmation increments `AuthVersion`, removes refresh sessions, and binds pending invitations to the account. Users must sign in again. Old email bloom bits remain until rebuilding, while writes still recheck the database.
