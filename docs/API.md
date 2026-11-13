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

Fetch `GET /api/auth/csrf` and send its request token in `X-CSRF-TOKEN`, with the antiforgery cookie retained. The antiforgery cookie is named `XSRF-TOKEN`. CSRF validation applies to the configured auth POST paths and to state-changing profile, `/api/auth/providers`, `/api/auth/sessions`, `/api/auth/devices`, and `/api/auth/passkeys` requests; it is not a blanket rule for every API action. The endpoint schema and `CsrfConfiguration` define the protected requests.

Some operations require MFA step-up as well as a signed-in session. Username, password, and email changes, account deletion, every SMS or TOTP enrollment, enable, disable, or remove call, recovery-code generation, every provider link or unlink, every passkey registration, rename, or removal, every session revocation or trusted-device removal, and an administrator enabling or disabling another account (`PATCH /api/admin/users/{id}/status`) need a step-up proof. For the admin route the proof belongs to the acting admin's session; there is no admin UI yet, so API clients complete step-up first, and a future admin UI should reuse `<app-mfa-gate>`. The other admin routes do not require step-up: the `reindex` routes only rebuild derived search indexes from the database, and the rest only read. The proof comes from `POST /api/auth/mfa/step-up/start` and `/verify` (TOTP, SMS, a recovery code, or the always-available email fallback), or from a passkey through `POST /api/auth/passkeys/step-up/options` and `/api/auth/passkeys/step-up`; see [passkeys](#passkeys). It is bound to the session and its auth version and expires a fixed 10 minutes after verification (`Auth:StepUp:ProofLifetimeMinutes`). Without it, these routes return `403` with error code `MFA_REQUIRED`. The step-up `options`, `start`, and `verify` routes, and the passkey step-up routes, never require a proof. Logout, credential rotation, and any completed MFA factor change discard the proof. New sensitive mutations must use `[RequireMfa]` and be added to the registry in `backend.tests.Unit/Application/Security/SensitiveEndpointStepUpTests.cs`. SMS and TOTP enrollment/step-up have separate configuration switches. Role and resource ownership checks remain authoritative on the backend. Email changes additionally verify the current password when applicable, require confirmation at the new address, and invalidate sessions on completion.

### Recovery codes

Recovery codes are a backup way to verify when a user's authenticator app or phone is unavailable.

- `POST /api/auth/mfa/recovery-codes/regenerate` needs a fresh step-up proof. It returns ten single-use codes once, with `Cache-Control: no-store`. They cannot be fetched again. Each call replaces the whole set, so every earlier code stops working. Like other factor changes, it discards the step-up proof on every session and emails the owner. The email never contains a code.
- `GET /api/auth/mfa/recovery-codes/status` returns `remainingCount`, `totalCount` (10), and `generatedAtUtc` (null before the first set). It never returns codes and needs no step-up.
- Codes are 28 characters from the Crockford base32 alphabet (140 random bits), shown as seven hyphen-separated groups of four. Input ignores case, spaces, and hyphens, and reads `O` as `0` and `I` or `L` as `1`. Anything else that cannot be a code is refused with `400` before it is checked.
- While any code remains unused, `recovery_code` appears in the step-up `options` and in a sign-in challenge's `availableMethods`. In-session step-up sends it to `POST /api/auth/mfa/step-up/verify` with `method: "recovery_code"`. Sign-in step-up sends the challenge and code to `POST /api/auth/mfa/verify/recovery-code`. Neither method needs a `start` call, but `start` accepts it.
- A code works once. A wrong, used, or replaced code answers `401`, and failed attempts are capped like TOTP.
- At sign-in, the code is spent only after the account and challenge are confirmed valid, so a disabled account or stale challenge never uses one up. A sign-in completed with a recovery code does not trust the device.
- Each use is recorded in the security history as `recovery_code_used` and emailed to the owner as a security alert.

Codes are stored as Argon2id hashes keyed by `Auth:RecoveryCodes:Pepper`; see [configuration](CONFIGURATION.md#recovery-codes). The plaintext is never stored, logged, or cached.

**Email fallback.** ADR 0001 asked for this policy to be defined. Recovery codes are offered alongside email, not instead of it. Email stays the universal step-up fallback for every account, including accounts with recovery codes. Removing email as a step-up method once an account has codes is deferred to a follow-up decision.

### Passwords

Signup (`POST /api/auth/signup`), reset (`POST /api/auth/reset-password` and `/api/auth/change-password`), and authenticated change (`POST /api/profile/change-password`) accept 15–128 characters with no composition rules. A length violation fails model validation. A common, breached, or account-derived value is rejected by the service. Both return `400` with code `VALIDATION_ERROR` and `details` keyed by the request field (`Password` or `NewPassword`), holding a single message. A rejected reset password leaves the emailed link or code usable for another attempt. A successful reset or change bumps `AuthVersion` and revokes every refresh session. Login (`POST /api/auth/login`) never applies the policy. See [password policy and hashing](CONFIGURATION.md#password-policy-and-hashing).

### OAuth provider sign-in

Google (`POST /api/auth/google`, `/api/auth/google/code`) and Microsoft (`POST /api/auth/microsoft`) sign-in first look up the account linked to the provider subject. A linked subject signs in whatever address the provider now reports.

Otherwise the provider's email is matched against existing accounts:

- **No match.** The response asks for role selection, and `POST /api/auth/oauth/complete` creates the account.
- **The account is linked to a different Google or Microsoft identity.** The request answers `409`, and nothing is linked.
- **The account has no provider link.** Nothing is linked and no one is signed in. The response is `200` with `Type` `requires_link_confirmation` and a `LinkConfirmation` holding `linkToken`, `provider`, `maskedEmail`, and `expiresAtUtc` (10 minutes). The owner signs in to the account as usual, passes step-up, and confirms with `POST /api/auth/providers/link/confirm`; see [provider linking](#provider-linking). This is offered only when the provider proves the caller controls the address.

Google requires `email_verified`. For Microsoft:

- The email comes only from the ID token's `email` claim, never from `preferred_username`, which a tenant administrator sets.
- The email counts as verified when the token is from the personal-account (consumer) tenant `9188040d-6c67-4c5b-b112-36a304b66dad`, or when the `xms_edov` claim is true. See [configuration](CONFIGURATION.md#addresses-and-public-values).
- An unverified address that matches an existing account answers `409` with error code `OAUTH_EMAIL_UNVERIFIED`. The account is neither linked nor signed in.
- An unverified address with no matching account can still create a new account.
- A token with no `email` claim signs in only with a subject that is already linked. Otherwise it answers `401`.

Completing a pending signup re-runs these checks. If an account took the address in the meantime, completion answers `409` and links nothing; the owner links the provider after signing in.

### Provider linking

The signed-in owner manages Google and Microsoft links under `/api/auth/providers`. A provider subject maps to at most one account, and an account holds at most one link per provider. Every change needs a fresh step-up proof (`403` `MFA_REQUIRED` without one) and a CSRF token.

| Request | Purpose |
| --- | --- |
| `GET /api/auth/providers` | `hasPassword`, and for each provider: `available` (configured here), `link` (`email`, `displayName`, `linkedAtUtc`, `lastUsedAtUtc`, or null), and `canUnlink`. The provider subject is never returned. No step-up needed. |
| `POST /api/auth/providers/{provider}/link/start` | Issues a `state`, `nonce`, and PKCE verifier bound to the caller's account, session, and auth version, held on the server for 10 minutes. Returns `authorizationUrl`, `state`, `redirectUri`, and, for Microsoft only, `codeVerifier`. The redirect URI is the server's own (`{FRONTEND_URL}/auth/{provider}`), never the client's; a browser that redeems the code sends back exactly the returned `redirectUri`. `409` `PROVIDER_ALREADY_CONNECTED` if the provider is already linked. |
| `POST /api/auth/providers/{provider}/link/complete` | `{ state, code }` for Google, whose code the server redeems with the verifier it kept. `{ state, idToken }` for Microsoft, whose public SPA client redeems the code in the browser. The state is spent on first use. The ID token's signature, issuer, audience, lifetime, and the stored nonce are all checked. |
| `POST /api/auth/providers/link/confirm` | `{ linkToken }` from a `requires_link_confirmation` sign-in. Only the account the email matched can confirm it. |
| `DELETE /api/auth/providers/{provider}` | Removes the link unless it is the account's last way to sign in (no password, no other link, and no passkey). Concurrent removals are serialized on the account row. The removal bumps `AuthVersion` in the same commit and ends every session of the account, the caller's included, so nothing the removed provider signed in keeps access. |

Errors a client tells apart by code:

- `410` `PROVIDER_LINK_EXPIRED`: the state or link token expired, was already used, or was revoked. Start again.
- `403` `PROVIDER_LINK_MISMATCH`: the state or link token belongs to another account or session, or the auth version changed. It is spent anyway.
- `409` `PROVIDER_ALREADY_LINKED`: the provider identity is linked to a different account. Email never overrides this.
- `409` `PROVIDER_ALREADY_CONNECTED`: the account already links a different identity from that provider.
- `409` `LAST_SIGN_IN_METHOD`: the link is the only way left to sign in.
- `400`: the provider refused the code or token, or the nonce did not match. This is never `401`, so it cannot be mistaken for an expired session.

Linking or unlinking revokes every link flow and email-link proposal the account still has outstanding. It is recorded as `auth.provider.linked` or `auth.provider.unlinked`, appears in the security history as `provider_linked` or `provider_unlinked`, and emails the owner a security alert. Unlinking also signs out every session, recorded as `sessions_revoked` with reason `provider_unlinked`.

A link's `lastUsedAtUtc` changes only when a sign-in through it completes: the account is enabled, any login step-up has been passed, and a session has been issued. A refused or abandoned sign-in does not count as a use.

Apple is a reserved provider name (`apple`). It is not offered until its flow exists, and adding it does not change these routes or shapes.

### Passkeys

Passkeys are WebAuthn credentials. Enrollment is optional, and passwords, OAuth, and email recovery are unchanged. The server verifies every ceremony with the Fido2 library ([ADR 0002](adr/0002-webauthn-server-library.md)). It stores only the credential id, the public key, the user handle, the signature counter, the backup flags, a name, and the creation and last-used times. The authenticator keeps the private key.

Every ceremony has two calls:

1. Options: the response holds `ceremonyId`, `publicKey` (WebAuthn options JSON for `PublicKeyCredential.parseCreationOptionsFromJSON()` or `parseRequestOptionsFromJSON()`), and `expiresAtUtc`.
2. Completion: send `{ ceremonyId, credential }`, where `credential` is the browser's `PublicKeyCredential` as JSON (its `toJSON()` form).

A ceremony is random, expires after `Auth:Passkeys:CeremonyTimeoutSeconds` (300 by default), works once whatever its outcome, and works only for the ceremony type that created it. Every option requires user verification. Registration also asks for a discoverable credential and no attestation.

| Request | Purpose |
| --- | --- |
| `GET /api/auth/passkeys` | The caller's passkeys: `id`, `name`, `syncable` (the passkey can sync between devices), `backedUp`, `canRemove`, `createdAt`, and `lastUsedAt`, plus the per-account `limit`. The credential id, public key, and user handle are never returned. No step-up needed. |
| `POST /api/auth/passkeys/registration/options` | `{ name? }`. Starts a registration bound to the caller's account, session, and auth version. The options exclude the caller's existing passkeys. `409` `PASSKEY_LIMIT_REACHED` once the account holds `Auth:Passkeys:MaxCredentialsPerUser` (10). |
| `POST /api/auth/passkeys/registration` | Stores the new passkey. Any refusal answers `401`. This covers a spent or expired ceremony, one from another session, an auth version that changed meanwhile, a wrong origin, RP ID, or challenge, missing user verification, and a credential id already registered to this or any other account. Success is a factor change: it discards step-up proofs on every session and emails the owner. |
| `PATCH /api/auth/passkeys/{id}` | `{ name }` (1–64 characters). Renaming does not discard step-up proofs. |
| `DELETE /api/auth/passkeys/{id}` | Removes the passkey unless it is the account's last way to sign in (no password, no provider link, and no other passkey): `409` `LAST_SIGN_IN_METHOD`, the same code as a provider unlink. Concurrent removals and unlinks are serialized on the account row. Removal is a factor change. It cancels the account's ceremonies in flight but does not end sessions; the owner can do that under [sessions](#sessions-and-trusted-devices). |
| `POST /api/auth/passkeys/sign-in/options` | Anonymous, with CSRF. Asks for any discoverable passkey for this RP ID; no username is sent. |
| `POST /api/auth/passkeys/sign-in` | Anonymous, with CSRF. `{ ceremonyId, credential, rememberMe?, transport? }`. On success the response has the same shape as `POST /api/auth/login` with `type` `authenticated`, and browser refresh tokens arrive as cookies. A user-verified passkey is phishing-resistant multi-factor authentication on its own, so no login step-up or new-device email follows. The device is not trusted, though, so a later password sign-in from it is still challenged. A disabled account answers `403`, and only after the passkey is proven. |
| `POST /api/auth/passkeys/step-up/options` | Offers only the caller's own passkeys. `400` if the account has none. |
| `POST /api/auth/passkeys/step-up` | Records the same 10-minute session-bound proof as `/api/auth/mfa/step-up/verify`. The step-up `options` list `passkey` first while the account has one and `auth.passkeys` is on. |

Every failed sign-in or step-up answers `401` with code `AUTHENTICATION_FAILED` and the same message. That covers an unknown credential, a bad signature, a replayed or expired ceremony, a malformed response, a wrong origin or RP ID, another account's passkey, and a user handle that does not match the credential. The reason is recorded only in the security events. Sign-in and options are rate-limited like password sign-in, and step-up shares the step-up verification budgets.

**Counters.** A device-bound passkey's signature counter must increase. A regression refuses that assertion and records `auth.passkey.counter_anomaly`, which alerts the owner. A backup-eligible (synced) passkey has copies on several devices, so its counter may stay at zero or arrive out of order (WebAuthn Level 3, §6.1.1). A regression there is recorded and alerted on, but does not refuse the sign-in. Neither case disables the passkey. A backup-eligible flag that differs from the one registered is refused.

**Recovery.** Passkeys are an additional sign-in method, not a replacement. Password reset by email, provider sign-in, recovery codes, and the email step-up fallback all work as before. Removing the final sign-in method is refused, so an account always keeps a way back in.

Passkeys are scoped to the RP ID in `Auth:Passkeys`; see [configuration](CONFIGURATION.md#passkeys). Turning `auth.passkeys` off removes these routes and stops offering `passkey` for step-up. Registered passkeys are kept and work again when the flag is turned back on.

### Sessions and trusted devices

The signed-in owner reviews and ends their sessions under `/api/auth/sessions`, and their trusted devices under `/api/auth/devices`. Listing needs no step-up. Every change needs a fresh step-up proof (`403` `MFA_REQUIRED` without one) and a CSRF token. An id that is not one of the caller's own live sessions or devices answers `404`, whether it is unknown, already ended, or another account's, so the response reveals nothing about other accounts.

| Request | Purpose |
| --- | --- |
| `GET /api/auth/sessions` | The caller's live sessions, most recently used first: `id`, `current` (the session making the request), `transport` (`browser` or `api`), `client`, `deviceClass`, `network`, `rememberMe`, `createdAt`, `lastSeenAt`, and `expiresAt`. |
| `DELETE /api/auth/sessions/{sessionId}` | Ends one session. Its refresh token and its unexpired access tokens stop working at once. If it is the current session, the response also clears the refresh cookies. |
| `DELETE /api/auth/sessions/others` | Ends every session except the current one, which keeps its step-up proof. |
| `DELETE /api/auth/sessions` | Ends every session, the current one included, and clears the refresh cookies. |
| `GET /api/auth/devices` | The caller's unexpired trusted devices, most recently used first: `id`, `current` (the device whose token the request presented), `client`, `deviceClass`, `network`, `createdAt`, `lastSeenAt`, and `expiresAt`. |
| `DELETE /api/auth/devices/{deviceId}` | Forgets one device: its next sign-in asks for verification again. If it is the current device, the response clears its cookie. Sessions are not ended. |
| `DELETE /api/auth/devices` | Forgets every device and clears the current device's cookie. Sessions are not ended. |

Revocations return `revokedCount` and `currentSessionRevoked`; device removals return `revokedCount` and `currentDeviceRevoked`. A client that ends its own session should drop its local session state and sign in again. Classifications use the same snake_case names and masked network prefix as the [security history](#security-history); no response carries a token, a token hash, or a full address.

Each change is recorded as `auth.session.revoked` (reasons `user_revoked_session`, `user_revoked_other_sessions`, `user_revoked_all_sessions`) or `auth.trusted_device.revoked`, and appears in the security history as `session_removed`, `other_sessions_revoked`, `sessions_revoked`, `trusted_device_removed`, or `trusted_devices_removed`. See [refresh sessions](CONFIGURATION.md#refresh-sessions) and [trusted devices](CONFIGURATION.md#trusted-devices) for how revocation and expiry are enforced.

### Security history

`GET /api/auth/security-events?page=1&pageSize=20` returns the signed-in caller's own security history, newest first, as a standard paged envelope. Entries are kept for up to 90 days (`SECURITY_HISTORY_RETENTION_DAYS` can shorten that, never extend it). There is no parameter that names an account; the owner is always the token subject. Reading the history needs a signed-in session but no step-up proof. `page` accepts 1–500 and `pageSize` 1–50, and out-of-range values fail model validation with `400`.

Each entry has:

- a `kind` such as `signed_in`, `password_changed`, `mfa_enabled`, `recovery_code_used`, `provider_linked`, `provider_unlinked`, or `refresh_token_reuse_detected`;
- `occurredAt`;
- optional `method`, `factor`, and `reason` names;
- coarse `client` and `deviceClass` classes;
- a `network` prefix (`203.0.113.0/24` or a `/48` for IPv6), never the full address;
- `initiatedByAdmin`;
- a `reference`, the id of the request that produced it.

Every classification is a snake_case name, never an ordinal, unlike the enums elsewhere in the API that are sent as integers. Entries are recorded asynchronously by the security-history worker, so a change can take a moment to appear. Failed sign-ins and recovery requests are deliberately not listed. See [security events](SECURITY_EVENTS.md#account-security-history).

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
- **Resource rules** are catalog permissions such as `club.manage`, `club.own` and `event.view`. An endpoint declares them with `[RequirePermission]`, a filter checks them before the action, and the service re-checks them. They cover club ownership, manager and volunteer staff roles, membership, event visibility, registrations, payments and media. Admin holds the club owner's powers explicitly. The [authorization guide](AUTHORIZATION.md) is the approved permission matrix: who each permission allows, how every endpoint is checked, and the known deviations.
- **Responses:**
  - A missing, invalid, or expired token answers `401` with error code `UNAUTHORIZED`.
  - A failed policy answers `403` with error code `FORBIDDEN` and the message "You do not have permission to access this resource". A resource-permission refusal uses the same code with the endpoint's own message.
  - Existence-sensitive resources (`event.view`, `media.view`) answer a refusal with the same `404` as a missing resource. Anonymous readers of a private club get `401` with the endpoint's message. See [refusals](AUTHORIZATION.md#refusals).
  - A route that matched nothing, including one removed by a disabled feature flag, still answers the JSON `404` to anonymous callers.
- **OpenAPI:**
  - Protected operations carry the `bearerAuth` security requirement and `401`/`403` responses.
  - Every operation carries an `x-authorization` extension: `anonymous`, `authenticated`, or the required policy name.
- **Audit:** the CI Auth Contract Gate (see [testing](TESTING.md#authorization-audit)) fails when:
  - an action, hub method, or minimal endpoint is unclassified;
  - an `[Authorize]` action sits under a class-level `[AllowAnonymous]`;
  - the anonymous surface differs from the reviewed list in `backend.tests.Unit/Application/Security/ReviewedPublicSurface.cs`;
  - the regenerated OpenAPI document differs from the committed `backend/openapi.json` or `backend/openapi.yaml`;
  - an endpoint is missing from the approved permission matrix, or its `[RequirePermission]` declarations differ from it.

  Add a new public endpoint to that list, classify every new endpoint in the matrix, and regenerate the OpenAPI documents, in the same change.

Captcha and external provider credentials are needed for their corresponding auth flows. Login omits CAPTCHA until the generic 401 envelope reports `meta.captchaRequired: true`; the next submission must include a fresh token. Login failures and sensitive delivery/verification operations use independent Redis-backed source-IP and hashed-target limits, with per-replica IP limiting retained during Redis outages.

Email availability uses `POST /api/auth/email/availability` with CSRF and `{ "email": "...", "captcha": "..." }`. The response envelope is unchanged. `GET /api/auth/email/availability?email=...` remains deprecated for one compatibility release, requires the CAPTCHA token in `X-Captcha-Token`, and returns `Deprecation` plus successor `Link` headers; CAPTCHA tokens are never accepted in the query string. Availability and suggestion endpoints are rate limited; a throttled or failed availability probe is advisory and must not be interpreted as either available or taken.

## Club moderation

Club staff rank runs owner, then manager, then volunteer. The [authorization guide](AUTHORIZATION.md#moderation) has the full rules.

- **Posts.**
  - Any staff member can create a post.
  - Authors edit or delete their own post while they are still staff.
  - Managers, the owner and admins can edit or delete any post.
  - Pinning or unpinning a post, including creating a pinned one, is limited to managers, the owner and admins. A volunteer gets `403`.
- **Discussions, replies, comments and reviews.**
  - Authors edit and delete their own.
  - Staff who outrank the author in that club, and admins, can remove them but not edit them.
  - Discussion edits and deletes need club membership (`club.contribute`), so a member who left or was removed gets `403`.
- **Reasons.**
  - Removing someone else's content requires a `reason` query parameter, for example `DELETE /api/clubs/{clubId}/posts/{id}?reason=Spam`.
  - When it is missing or blank, the API answers `400`.
  - `PUT /api/clubs/{clubId}/posts/{id}` accepts an optional `moderationReason` in the body.
  - Reasons are plain text of up to 500 characters and follow the [human-authored text](#human-authored-request-text) rules.

`GET /api/clubs/{clubId}/moderation-log?page=&pageSize=` lists a club's moderation actions, newest first, as a paged response. It is open to the club's managers, its owner, and admins (`club.manage`). Each item has these fields:

- `action`: `remove`, `edit`, `pin` or `unpin`;
- `contentType` (`Post`, `Discussion`, `Reply`, `Comment` or `Review`) and `contentId`;
- `contentSnapshot`: the title or opening text, up to 200 characters, as it was before the action;
- `reason`;
- `actorClubRole`: `Owner`, `Manager`, `Volunteer` or `Admin`;
- `actor` and `author`, as `{ id, name, username, usernameDisplay, avatar }`;
- `createdAt`.

An entry is written in the same transaction as the change it records.

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
