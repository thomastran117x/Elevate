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
| `API_PROXY_TARGET`          | Backend origin used by the dev/SSR proxy; supply to the server process |
| `FRONTEND_BACKEND_URL`      | Compose override for the frontend's browser API base                   |
| `FRONTEND_API_PROXY_TARGET` | Compose override for its proxy upstream                                |
| `BACKEND_PORT`              | Compose maps this to backend `PORT` (default 8090)                     |
| `NG_ALLOWED_HOSTS`          | SSR allowed hostnames, comma-separated without ports                   |

Backend URL selection reads `PORT`, then `ASPNETCORE_PORT`, then 8090. The built frontend SSR server reads `PORT`, defaulting to 3090; the Angular dev server is configured at 3090. See [setup's address table](SETUP.md#full-docker-stack) for exposed infrastructure ports and the Kafka advertised-listener limitation.

Generated public values are `NODE_ENV` (production flag), `BACKEND_URL`, `FRONTEND_URL`, `GOOGLE_CLIENT_ID`, `MSAL_CLIENT_ID`, `GOOGLE_SITE_KEY`, and frontend feature flags. JWT secrets, OAuth client secrets, SMTP/Twilio credentials, storage connection strings, and payment secrets must never enter that bundle.

Compose maps `MSAL_CLIENT_ID` to backend `MS_CLIENT_ID`. A locally run backend must supply `MS_CLIENT_ID` explicitly for Microsoft OAuth.

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
| Captcha                 | `CAPTCHA_PROVIDER`, `GOOGLE_CAPTCHA_SECRET`; public `GOOGLE_SITE_KEY`                                       |
| Email                   | `SMTP_SERVER`, `SMTP_PORT`, `EMAIL_USER`, `EMAIL_PASSWORD`                                                  |
| SMS                     | `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, plus messaging service SID or sender phone                       |
| Azure blobs             | `AZURE_STORAGE_CONNECTION_STRING`, `AZURE_STORAGE_CONTAINER_NAME`                                           |
| Stripe                  | `STRIPE_API_KEY`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_SUCCESS_URL`, `STRIPE_CANCEL_URL`                        |
| Seeding                 | `RUN_SEEDERS`, `AUTH_SEED_ACCOUNT_BYPASS`, and seed account variables                                       |
| Logging                 | `LOG_LEVEL` and backend/worker logger configuration                                                         |

Default database provider is PostgreSQL. SQLite is used by OpenAPI export and selected isolated tests, not as the normal application database.

Email/SMS consumers remain idle if provider configuration is incomplete. Topic/group/DLQ defaults and failure policies are documented in [worker READMEs](../backend/README.md#workers). Keep notification topic names aligned between API publishers and consumers.

## Feature flags

The backend registry contains the following keys. Environment names are `FEATURE_` plus the uppercased key, with dots replaced by underscores. The hyphen in `storage.orphan-cleanup` is retained: its literal name is `FEATURE_STORAGE_ORPHAN-CLEANUP`; use the appsettings section or explicit environment mapping for shells that cannot assign that name normally.

| Area              | Backend keys                                                                                                                                                                 |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Auth / bloom      | `auth`, `bloom`                                                                                                                                                              |
| Clubs             | `clubs`, `clubs.discussions`, `clubs.follow`, `clubs.posts`, `clubs.reviews`, `clubs.versioning`                                                                             |
| Events            | `events`, `events.analytics`, `events.favourites`, `events.images`, `events.invitations`, `events.recurrence`, `events.registration`, `events.versioning`, `events.waitlist` |
| Payment / profile | `payment`, `profile`, `profile.admin`                                                                                                                                        |
| Search            | `search`, `search.reindex`                                                                                                                                                   |
| Storage           | `storage`, `storage.orphan-cleanup`                                                                                                                                          |

Missing flags default to true. A false parent disables its descendants. Backend parsing first reads registered process variables, then `FeatureFlags` configuration entries override those values. Unknown keys in that section are rejected; unrelated unknown process variables are not scanned/rejected. Backend boolean parsing is case-insensitive.

Frontend generation currently supports the club, auth, payment, profile, search, and event keys above **except** `events.recurrence`; it also omits `bloom` and storage keys. It accepts only literal `true`/`false` for configured booleans. These additional backend flags are absent from the root template's flag list and the shared Compose anchor; do not assume every registered backend flag has a matching frontend/build mapping.

Backend gates remove disabled MVC actions/controllers from discovery and OpenAPI and affect selected DI/hosted-service registrations. Frontend guards use `canMatch` for lazy routes; navigation should also hide disabled entry points. Configure shared flags together and rebuild the frontend when changing them.

## Security and application options

Use strong deployment-specific JWT secrets and a valid `AUTH_TOTP_ENCRYPTION_KEY` rather than development fallbacks. Production validation rejects missing/default critical values. SMS enrollment, enforcement, and step-up are controlled by `AUTH_SMS_MFA_ENROLLMENT_ENABLED`, `AUTH_SMS_MFA_ENFORCEMENT_ENABLED`, and `AUTH_SMS_MFA_STEP_UP_SMS_ENABLED`. TOTP has `AUTH_TOTP_MFA_ENROLLMENT_ENABLED` and `AUTH_TOTP_MFA_STEP_UP_ENABLED`.

Appsettings sections configure CORS, forwarded headers, request timeouts, rate limiting, profile username cooldown, version retention, recurrence, image upload limits, image processing, and orphan-blob cleanup. `Profile__UsernameChangeCooldownDays` is the normal nested override; Compose maps the template's uppercase `PROFILE__USERNAMECHANGECOOLDOWNDAYS` into it. Inspect options classes for accepted names and defaults rather than assuming all keys are flattened template variables.

### Image uploads

`ImageUpload:MaxBytes` caps an uploaded image at 5 MB by default. It is enforced when the image is attached to an event, draft, club, or recurrence series rather than when it is uploaded: presigned uploads go from the browser straight to Azure, and a SAS has no content-length field, so nothing on the request path ever sees the bytes. At attach time the server reads the stored blob's length and leading bytes, rejects anything empty, over the cap, not a supported image, or whose bytes disagree with the declared type, and deletes the blob. Re-attaching an image an event or club already holds skips the check, so a later edit costs no storage round trip.

The presigned URL grants Azure's `Create` permission without `Write`, which makes it usable exactly once: `Put Blob` accepts either permission to create a new blob but requires `Write` to overwrite one, so the bytes that pass inspection are the bytes that stay there. The stored content type is restamped from those bytes on attach, because the SAS content type overrides only reads made through the SAS, while the anonymously readable public URL is served with whatever type the uploader set on its own PUT.

Raising the cap affects only what is accepted from that point on; images already attached are unaffected. The multipart avatar upload has its own compiled-in 5 MB limit in `AvatarUploadRequest` and does not read this setting. `RateLimiter:ImageUploadPermitLimit` (30 presigned URLs per 10 minutes per account) bounds how many blobs one account can create, which the size cap does not.

### Image processing

Multipart avatar uploads are decoded to pixels and re-encoded before anything is stored, so EXIF (including phone GPS), IPTC, XMP, ICC profiles, and any non-pixel payload hidden in the file are gone. The pipeline reads the header first, rejecting oversized dimensions and animated images before any pixel buffer is allocated. It then decodes a single frame, shrinks the image to the size cap, applies the EXIF orientation (after shrinking, so the rotation never needs a second full-size buffer), strips the metadata, and encodes lossy WebP. Presigned uploads are not processed yet: the bytes go from the browser to Azure, so the server has nowhere to run this until uploads land in a quarantine container.

| Key | Default | Purpose |
| --- | --- | --- |
| `ImageProcessing:MaxDimension` | `8000` | Largest width or height accepted, read from the header alone. |
| `ImageProcessing:MaxPixels` | `50000000` | Largest total pixel count; catches a size that is within `MaxDimension` on each side but still decodes to hundreds of megabytes. |
| `ImageProcessing:AvatarMaxEdge` | `512` | Long-edge cap for avatars. Smaller images are never upscaled. |
| `ImageProcessing:GalleryMaxEdge` | `2048` | Long-edge cap for gallery images, matching the largest input Azure AI Content Safety accepts. |
| `ImageProcessing:WebpQuality` | `82` | Lossy WebP quality, 1–100. |
| `ImageProcessing:MaxAllocationMegabytes` | `256` | Largest buffer the decoder may allocate for one image; 50 MP of RGBA is about 200 MB. |
| `ImageProcessing:MaxConcurrentOperations` | `2` | Images processed at once across the process; further requests wait. |

Values are validated on startup. Processing uses [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) under the Six Labors Split License, which is free for open-source projects and for organisations under roughly $1M USD annual gross revenue; above that a commercial licence is required. The package is held on 3.x because 4.x fails Release builds without a Six Labors licence key. The CI backend audit fails on high or critical NuGet advisories, so ImageSharp patch releases need to be taken promptly.

### Bloom filters and identity probes

The `BloomFilters` section controls refresh (30 seconds), rebuild (6 hours), retired generation TTL (60 minutes), local replay (30 minutes), forced-rebuild cooldown (15 minutes), and per-target expected items (250000) / false-positive rate (0.01). Registered targets are username and email; club-name is reserved, not an authoritative availability feature.

Filters combine local and Redis bitmaps. They can avoid database reads for definitely absent values but never authorize a write. Rebuilds rotate generations and replay concurrent additions; deleted values persist until rebuilding. With `bloom=false`, callers fall back to database checks.

Anonymous email availability exposes account existence and is intentionally rate limited: `RateLimiter:EmailAvailabilityPermitLimit` defaults to 15/minute/IP; username suggestions default to 10/minute/IP through `UsernameSuggestionsPermitLimit`. The latter bounds database/candidate-generation cost.

### Email changes

`POST /api/profile/email` requires MFA step-up and the current password where applicable, then sends confirmation to the new address and a notice to the old one. `RateLimiter:EmailChangePermitLimit` defaults to 3/hour/account. Confirmation increments `AuthVersion`, removes refresh sessions, and binds pending invitations to the account. Users must sign in again. Old email bloom bits remain until rebuilding, while writes still recheck the database.
