# ADR 0001: Retain first-party identity rather than adopt a managed provider

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-10-14 |
| Issue | [#322](https://github.com/thomastran117x/Elevate/issues/322), Phase 0 of epic [#318](https://github.com/thomastran117x/Elevate/issues/318) |
| Deciders | Repository owner, reviewing as backend, frontend, security, and deployment owner |
| Evidence date | Provider documentation and prices verified 2026-10-14 |

EventXperience has a substantial first-party identity stack. This record compares it with Microsoft Entra External ID, Amazon Cognito, and Auth0 using the weights agreed in #322. It decides how Phases 2 and 3 of #318 proceed. The [API guide](../API.md#authentication-csrf-and-mfa) and [configuration guide](../CONFIGURATION.md#security-and-application-options) describe the current contracts. This record explains why they stay first-party and when that should be revisited.

## Decision

**Retain first-party authentication.** It continues to be implemented in the ASP.NET Core API. Phases 2 and 3 of #318 proceed as first-party work, with the portability guardrails in [Impact on Phases 2 and 3](#impact-on-phases-2-and-3).

- **Fallback:** Auth0 is the designated managed provider if a [decision trigger](#decision-triggers) fires. It has the most complete security feature set and the best migration continuity. It needs an Enterprise quote above 20,000 MAU.
- **Rejected:** Entra External ID and Amazon Cognito, for the reasons in [Rejected alternatives](#rejected-alternatives).

The first-party option scores highest (4.00 against Auth0's 3.75) only because migration and application compatibility carry 45% of the weight. On security capability alone it scores below Auth0. The margin is narrow, and [sensitivity](#sensitivity) shows it reverses under plausible changes. This decision is therefore conditional: it commits the project to closing the gaps in [Known gaps](#known-gaps) and to the triggers below.

## Context and decision drivers

Phase 1 of #318 hardened the stack: authentication by default (#337), session-bound 10-minute step-up (#333), Argon2id hash agility (#338), OAuth link conflict rejection (#340), and AuthVersion binding of refresh sessions and trusted devices (#341, #342).

Phases 2 and 3 would add:
- session and device management (#325)
- security history (#326)
- recovery codes (#327)
- passkeys (#328)
- provider linking (#329)
- resource authorization (#331)

Several of these are provider-native features. If a managed provider is adopted later, rebuilding them first-party is wasted work, so this decision gates both phases.

The drivers are:
- the security capability the project can sustain
- continuity for existing users and their passwords, OAuth links, and MFA enrollments
- fit with an application-owned authorization model (global roles plus club ownership and staff)
- operational load on a small team hosting on Azure
- published cost at 10k, 100k, and 1M monthly active users (MAU)

## Current state

Every component is custom code. There is no ASP.NET Core Identity, OpenIddict, Duende, or external identity provider.

| Area | Current implementation | Evidence |
|---|---|---|
| Local sign-in | Username and password. New hashes are Argon2id PHC strings (m=19456 KiB, t=2, p=1) over the NFKC-normalized password. Legacy BCrypt (cost 12) hashes verify unnormalized and upgrade on successful login. A dummy hash equalizes unknown-user timing. | [PasswordHasher.cs](../../backend/src/main/features/auth/password/PasswordHasher.cs), [Argon2idPasswordFormat.cs](../../backend/src/main/features/auth/password/Argon2idPasswordFormat.cs), [PasswordText.cs](../../backend/src/main/features/auth/password/PasswordText.cs) |
| Password policy | 15–128 characters, no composition rules, and context checks. An offline blocklist of the 100,000 most common passwords applies at signup, reset, and change. There is no online breached-password service. | [PasswordPolicy.cs](../../backend/src/main/features/auth/password/PasswordPolicy.cs), [common-passwords.txt](../../backend/resources/passwords/common-passwords.txt) |
| Abuse controls | Redis-backed per-source and per-identifier budgets, CAPTCHA after repeated failures, and capped progressive delay. There is no hard lockout. | [abuse/](../../backend/src/main/features/auth/abuse/), [configuration](../CONFIGURATION.md#authentication-abuse-protection) |
| Federation | Google: server-side code exchange with `email_verified` required. Microsoft: browser PKCE exchange, with the `id_token` validated by the API against the multi-tenant `common` metadata. Apple: verification code exists but nothing calls it. Provider subjects are the `GoogleID`/`MicrosoftID` columns on `Users`. | [OAuthService.cs](../../backend/src/main/features/auth/oauth/OAuthService.cs), [AuthService.cs](../../backend/src/main/features/auth/AuthService.cs) |
| Access tokens | HS256 JWT signed with one static secret, valid for 15 minutes. Claims are the integer user id, one role, `auth_version`, and `sid`. Every request reloads the user and rejects disabled accounts or AuthVersion mismatches. | [TokenService.cs](../../backend/src/main/features/auth/token/TokenService.cs), [JwtConfiguration.cs](../../backend/src/main/application/security/JwtConfiguration.cs) |
| Refresh sessions | Stored only in Redis. Rotation issues a refresh token plus a session-binding token, both in HttpOnly SameSite=Strict cookies scoped to `/api/auth`. Sessions are bound to AuthVersion. The TTL is 1 day, or 30 days with remember-me. ASP.NET antiforgery protects the cookie routes. | [TokenService.cs](../../backend/src/main/features/auth/token/TokenService.cs), [CsrfConfiguration.cs](../../backend/src/main/application/security/CsrfConfiguration.cs), [configuration](../CONFIGURATION.md#refresh-sessions) |
| Trusted devices | `Devices` table keyed by a token hash. A new device is verified by email or MFA. Trust is written only if AuthVersion is unchanged. | [DeviceTrustService.cs](../../backend/src/main/features/auth/device/DeviceTrustService.cs), [configuration](../CONFIGURATION.md#trusted-devices) |
| MFA and step-up | TOTP (secrets encrypted with AES-256-GCM), SMS through Twilio, and email OTP. Login step-up applies to untrusted devices and enrolled users. `[RequireMfa]` demands a 10-minute, session-bound proof for sensitive mutations. | [totp/](../../backend/src/main/features/auth/mfa/totp/), [session/](../../backend/src/main/features/auth/mfa/session/), [LoginStepUpChallengeService.cs](../../backend/src/main/features/auth/stepup/LoginStepUpChallengeService.cs), [RequireMfaAttribute.cs](../../backend/src/main/application/security/RequireMfaAttribute.cs) |
| Recovery | Password reset by link or OTP, username recovery, and email change with confirmation. Each bumps AuthVersion and revokes sessions. | [AuthService.cs](../../backend/src/main/features/auth/AuthService.cs), [AuthNotificationService.cs](../../backend/src/main/features/auth/notifications/AuthNotificationService.cs) |
| Authorization | One global role (`Users.Usertype`: Admin, Organizer, Participant, Volunteer) and an authenticated fallback policy. Club ownership and `ClubStaff` manager/volunteer checks live in services, alongside feature gates. | [AuthRoles.cs](../../backend/src/main/application/security/AuthRoles.cs), [AuthorizationPolicies.cs](../../backend/src/main/application/security/AuthorizationPolicies.cs), [ClubService.cs](../../backend/src/main/features/clubs/ClubService.cs), [API authorization](../API.md#authorization) |
| Audit | Serilog lines only (`[AuthMfaAudit]`, `[SessionMfaAudit]`, abuse EventIds). There is no audit table, user-facing history, or export. | #323, #326 |
| Identifiers and data | `int` user ids referenced by every feature table. Email and username are citext and unique. Sessions, pending verifications, and step-up proofs live only in Redis. | [User.cs](../../backend/src/main/features/profile/User.cs), [AppDatabaseContext.cs](../../backend/src/main/infrastructure/database/core/AppDatabaseContext.cs) |
| Frontend | Access token held in memory (NgRx and `AuthTokenService`). Access, refresh, and CSRF interceptors are in place. OAuth callbacks post codes or tokens to the API, so the browser never holds a refresh token. | [auth-token.service.ts](../../frontend/src/app/core/api/services/auth-token.service.ts), [interceptors/](../../frontend/src/app/core/api/interceptors/) |
| Tests | Testcontainers integration suite with `FakeOAuthService`, `FakeCaptchaService`, and fast Argon2 parameters. Endpoint authorization and step-up registries are enforced by unit tests. | [TestWebApplicationFactory.cs](../../backend.tests.Integration/Infrastructure/TestWebApplicationFactory.cs), [SensitiveEndpointStepUpTests.cs](../../backend.tests.Unit/Application/Security/SensitiveEndpointStepUpTests.cs), [testing](../TESTING.md#authorization-audit) |

### Ownership costs

First-party identity has no per-MAU fee. Its costs are ownership:
- maintaining the code above and its tests
- Redis availability, because sessions are Redis-only
- SMTP and Twilio delivery, through the [email](../../backend/src/worker/email-worker/README.md) and [SMS](../../backend/src/worker/sms-worker/README.md) workers
- secret rotation for the JWT, TOTP-encryption, and OAuth credentials (#330)
- monitoring (#323, #330) and incident response for identity incidents

**Engineering estimate:** about 2–4 engineer-days a month for upkeep at the current scale, plus the Phase 2 and 3 build. This is an assumption, not a measurement; record actual effort to inform the [triggers](#decision-triggers).

### Known gaps

Most gaps are already tracked by #318. Re-reading the code for this decision found three that were not; each now has its own issue.

| Gap | Evidence | Tracked by |
|---|---|---|
| Microsoft sign-in takes the email from `preferred_username` (falling back to `email`). Neither is a verified-email claim, and any Entra tenant is accepted. That address drives email-fallback linking onto an account with no provider link. | [OAuthService.cs:200](../../backend/src/main/features/auth/oauth/OAuthService.cs#L200), [AuthService.cs:981](../../backend/src/main/features/auth/AuthService.cs#L981) | #346 |
| A replayed, already-rotated refresh token is rejected without revoking its session. The rotated token's key is deleted, so lookup fails before reuse detection runs. | [TokenService.cs:237](../../backend/src/main/features/auth/token/TokenService.cs#L237), [TokenService.cs:275](../../backend/src/main/features/auth/token/TokenService.cs#L275) | #347 |
| Admin account enable/disable does not require a fresh step-up proof. | [UserAdminController.cs:29](../../backend/src/main/features/profile/admin/UserAdminController.cs#L29) | #348 |
| No server-side trusted-device expiry. No list or revoke for sessions and devices. | [DeviceTrustService.cs](../../backend/src/main/features/auth/device/DeviceTrustService.cs) | #325 |
| No recovery codes. Email fallback satisfies every step-up. | — | #327 |
| No phishing-resistant authenticator (passkeys). | — | #328 |
| Implicit provider linking, with no link/unlink UI. Apple is unused. | — | #329 |
| No structured security events, user-facing history, or export. | — | #323, #326 |
| Single static HS256 signing key with no `kid`, so rotation can't overlap keys. | [TokenService.cs](../../backend/src/main/features/auth/token/TokenService.cs) | #330 |
| Club authorization is spread across services. | [ClubService.cs](../../backend/src/main/features/clubs/ClubService.cs) | #331 |

## Requirements

**Mandatory requirements** must be met by the chosen option, natively or with bounded custom work:

| # | Requirement |
|---|---|
| M1 | Existing local users keep signing in with their current username or email and password. There is no forced reset for the whole population at cutover. |
| M2 | Google and Microsoft sign-in continue for linked accounts, and links are keyed by provider subject, not email. |
| M3 | MFA with at least one factor stronger than email, plus a path to phishing-resistant passkeys. |
| M4 | Refresh rotation with server-side revocation of one session, all sessions, and all sessions on credential change. |
| M5 | Fresh, session-bound step-up of 10 minutes or less for sensitive mutations. |
| M6 | Global roles, club ownership, and staff checks remain enforced by the backend, against the existing `int` user id. |
| M7 | CI integration tests run without a live tenant or network dependency. |
| M8 | Security events can be retained beyond 30 days and exported. |
| M9 | A documented exit path that does not strand users or their credentials. |

**Disqualifying gaps** are the requirements that rule an option out outright:

- **M1 and M6:** an option that can't meet either is disqualified, because failing them breaks every existing user or every authorization check.
- **M7:** an option that needs a live tenant for every CI run is disqualified.

No option is disqualified. Each meets M1 through a migration path, M6 through a `sub`-to-`int` mapping table, and M7 by validating provider-shaped JWTs signed with a test key. The other requirements affect scores rather than eligibility.

## Options and capability evidence

Evidence is from official documentation fetched on 2026-10-14. ✓ means native. ◐ means partial, plan-gated, or preview. ✗ means absent or custom-built only.

| Capability | First-party (today) | Entra External ID | Amazon Cognito (Essentials/Plus) | Auth0 |
|---|---|---|---|---|
| Local username/password | ✓ | ✓ email + password; username sign-in identifier ([alias][entra-alias]); username at sign-up is preview | ✓ username or alias ([features][cog-plans]) | ✓ |
| Google / Microsoft / Apple | ✓ / ✓ / ✗ (Apple unused) | ✓ / ◐ personal accounts via custom OIDC, work accounts via Entra federation / ✓ ([methods][entra-features]) | ✓ / ◐ Microsoft as OIDC IdP, billed per federated MAU / ✓ | ✓ / ✓ / ✓ |
| MFA factors | TOTP, SMS, email | Email OTP, SMS, passkey. **No authenticator-app TOTP** for customers ([features][entra-features]) | TOTP, SMS, email (email needs Essentials). One preferred factor per user ([MFA][cog-mfa]) | TOTP, SMS/voice, email, push, WebAuthn, recovery codes; factors plan-gated ([factors][a0-factors]) |
| Passkeys | ✗ (#328) | ✓ sign-in and MFA; registration needs an existing password account ([passkeys][entra-passkey]) | ✓ Essentials. Not a second factor after a password unless `MULTI_FACTOR_WITH_USER_VERIFICATION` ([MFA][cog-mfa]) | ✓ all plans ([pricing][a0-pricing]) |
| Breached passwords / risk | Offline top-100k list only | Global banned-password list and smart lockout. **ID Protection not available** in external tenants | Plus: compromised-credential and adaptive risk checks ([features][cog-plans]) | Breached-password detection and enhanced attack protection on Professional; adaptive MFA on Enterprise ([pricing][a0-pricing]) |
| Recovery | Link or OTP reset, username recovery; no recovery codes | SSPR by email OTP or SMS | Forgot-password by email or SMS; must not use the MFA channel ([MFA][cog-mfa]) | Reset flows and MFA recovery codes |
| Refresh rotation / revocation | ✓ rotation + binding token; reuse detection gap above | Revoke all of a user's sessions via Graph `revokeSignInSessions` ([features][entra-features]) | ✓ optional rotation, 60 s grace, revoke and global sign-out ([refresh][cog-refresh]) | ✓ rotation with family-wide reuse detection ([rotation][a0-rotation]) |
| Step-up | ✓ session-bound 10-minute proof | ◐ Conditional Access authentication context and sign-in frequency; no risk conditions ([features][entra-features]) | ◐ no native step-up; adaptive MFA on Plus only | ✓ Actions-driven MFA checked via ID-token claims or scopes ([step-up][a0-stepup]) |
| Account linking | ◐ implicit by email (#329) | ◐ identities collection per user | ◐ `AdminLinkProviderForUser` | ✓ account-linking APIs |
| Roles / custom claims | ✓ application-owned | ◐ app roles being phased into external tenants; custom claims provider extension ([features][entra-features]) | ◐ groups; access-token customization on Essentials ([features][cog-plans]) | ✓ RBAC (Essentials+); Actions add claims |
| Audit export | ✗ (#323, #326) | ◐ audit and sign-in logs kept **7 days**; Azure Monitor export is preview ([features][entra-features]) | ◐ activity log export on Plus ([features][cog-plans]); CloudTrail for admin APIs | ◐ retention 5/10/30 days by plan; log streaming ([retention][a0-logs]) |
| Data residency | Wherever we deploy Postgres and Redis | Tenant geography; Go-Local add-on (Australia, Japan) ([pricing doc][entra-pricing-doc]) | Any AWS Region per user pool | Public cloud US, EU, AU, JP, UK, CA; Private Cloud on AWS or Azure ([endpoints][a0-regions]) |
| Tenant isolation | Single database | Dedicated external tenant | Dedicated user pool | Dedicated tenant; Private Cloud on Enterprise |
| Local/test environments | ✓ fully offline in CI | ✗ no emulator; separate dev tenant | ✗ no official emulator; separate pool | ✗ no emulator; separate dev tenant |
| Incident response | Team-owned end to end | Microsoft-operated service; tenant admin acts on users | AWS-operated; customer acts via APIs | Okta-operated; tenant logs and support |
| Portability / exit | ✓ we hold the hashes | ✗ no hash export documented; exit needs JIT harvest or reset | ✗ passwords stored hashed and not retrievable ([passwords][cog-export]); exit needs JIT harvest or reset | ◐ hash and MFA-secret export by support case, subject to eligibility review and executive sign-off ([export][a0-export]) |

## Migration continuity

Each option was assessed against the six continuity items in #322.

| Item | Entra External ID | Amazon Cognito | Auth0 |
|---|---|---|---|
| **Existing passwords** | No hash import. Pre-create users through Graph with random passwords and a `toBeMigrated` flag ([migrate users][entra-migrate]). The GA `OnPasswordSubmit` extension sends the encrypted password to our endpoint, which verifies it with the existing hasher; Entra then stores it ([JIT][entra-jit]). | No hash import. The migrate-user Lambda trigger runs on sign-in or forgot-password. The app client must enable `USER_PASSWORD_AUTH` during migration ([Lambda migration][cog-lambda]). CSV import forces resets. | Bulk import accepts `argon2` and `bcrypt` hashes as UTF-8 PHC/MCF strings ([schema][a0-schema]). **Caveat:** our Argon2id hashes are over the NFKC-normalized password, so imported hashes fail for non-ASCII passwords whose NFKC form differs. Route those through a custom-database lazy migration (Professional+) or a reset. The 500 KB file limit means batching ([bulk import][a0-bulk]). |
| **OAuth links** | Google subjects are stable across clients, so federated users can be pre-created with Google identities. We store the Microsoft `sub`, which is [pairwise per application][ms-claims]. It survives only if the provider's Microsoft federation reuses our app registration; otherwise users re-link at first sign-in. | Same constraint. Pre-link Google with `AdminLinkProviderForUser`; Microsoft (configured as an OIDC IdP) re-links unless our app registration is reused. | Same constraint. Google identities can be pre-linked by subject; Microsoft re-links unless the connection reuses our app registration. |
| **Roles** | Keep in our database (recommended) or map to app roles. | Keep in our database, or use groups. | Keep in our database, or use RBAC. Add an Action to put the `int` id and role in tokens. |
| **Sessions** | None transfer. All users sign in again at cutover. | Same. | Same. |
| **MFA enrollments** | **TOTP can't move** (unsupported for customers). SMS numbers could probably be pre-registered through the Graph phone-methods API, but this is not verified for external tenants. TOTP users fall back to SMS, email, or a new passkey. | TOTP secrets can't be imported; users re-enroll. SMS and email carry over as attributes. | TOTP, SMS, and email enrollments import with the user ([schema][a0-schema]). |
| **Account identifiers** | New object id (GUID). Map `oid`→`int` in a new link table. | New `sub` (UUID). Map `sub`→`int`. | Import accepts our id as `user_id` (prefixed by the connection strategy) ([schema][a0-schema]). Map it back to `int`. |

Every provider uses the same staged pattern:
1. Introduce the identity-link table.
2. Run the backend-for-frontend code exchange in parallel behind a feature flag.
3. Migrate credentials just in time for a time-boxed window, monitoring the migrated percentage.
4. Send the long tail through password reset.
5. Retire the first-party endpoints.

## Scoring

Scores run from 1 (poor) to 5 (strong) and are weighted as #322 specifies. Cognito is scored at the Plus tier where security depends on it, and at Essentials for cost. Auth0 is scored on the Professional/Enterprise features that 100k MAU would require.

| Criterion (weight) | First-party | Entra External ID | Cognito | Auth0 |
|---|---|---|---|---|
| Security capabilities (30%) | **3**: strong token and session design and Argon2id; no passkeys, recovery codes, risk detection, or audit store yet | **3**: passkeys, banned passwords, smart lockout; no TOTP, no ID Protection, 7-day logs | **3.5**: TOTP/SMS/email, passkeys, rotation; Plus adds compromised-credential and adaptive checks; no native step-up | **4.5**: every factor including recovery codes and WebAuthn, attack protection, reuse-detecting rotation, step-up |
| Migration and user continuity (25%) | **5**: no migration | **2.5**: GA JIT passwords; TOTP lost; re-link Microsoft; new ids | **2.5**: Lambda JIT; TOTP re-enrollment; new ids | **4**: imports hashes and MFA; NFKC caveat; new ids |
| Application and authorization compatibility (20%) | **5**: roles, club checks, AuthVersion, and step-up are native | **2.5**: app roles still being phased in; step-up via Conditional Access context only; mapping and AuthVersion re-implemented | **2.5**: claim customization on Essentials; no step-up; mapping and AuthVersion re-implemented | **3**: Actions and claims fit; step-up supported; mapping and AuthVersion re-implemented |
| Operations and reliability (15%), Azure-hosted | **3**: we own Redis HA, keys, delivery, and response; offline tests | **4**: managed and Azure-aligned; no emulator; 7-day logs | **3**: managed, but adds a second cloud to an Azure deployment | **4**: managed with log streaming; Private Cloud on Azure available |
| Cost (10%) | **4**: no fee; engineering time is the cost | **3**: see below | **4**: lowest managed price at 1M | **2**: self-serve ends at 20k MAU; Enterprise quote required |
| **Weighted total** | **4.00** | **2.93** | **3.03** | **3.75** |

### Sensitivity

- **Auth0 overtakes first-party if** the first-party security score drops from 3 to 2 (3.70 against 3.75). That could happen through an incident or by failing to deliver #325, #327, or #328.
- **It also overtakes if** security weighs 45% and migration 10% (3.83 against 3.70).
- **Entra and Cognito stay last** under every re-weighting tried. Their migration and compatibility scores cap them, and Entra's missing TOTP and 7-day log retention hold back its security score.

The decision is robust between the two managed laggards and fragile against Auth0. The [decision triggers](#decision-triggers) monitor that fragility.

## Cost estimates

Published list prices in USD per month, as of 2026-10-14. MAU means unique users who authenticate in a calendar month. SMS, email, and engineering time are excluded unless stated.

| Option | 10k MAU | 100k MAU | 1M MAU | Basis |
|---|---|---|---|---|
| First-party | $0 | $0 | $0 | No license. Redis and PostgreSQL are already required by the application. |
| Entra External ID | $0 | $1,500 | $28,500 | First 50,000 MAU free, then $0.03 per MAU (Azure Retail Prices API meter "Basic Monthly Active Users", effective 2025-07-01; [pricing][entra-price]). The API also lists an older "Core" meter at $0.01625 effective 2025-05-01. Confirm which applies in the Azure pricing calculator before budgeting. |
| Cognito Essentials | $0 | $1,350 | $14,850 | 10,000 MAU free, then $0.015 ([pricing][cog-price]) |
| Cognito Plus | $200 | $2,000 | $20,000 | $0.02 per MAU, no free tier ([pricing][cog-price]) |
| Auth0 B2C Essentials | $700 | Quote | Quote | Self-serve ends at 20,000 MAU ($1,400) ([pricing][a0-pricing]) |
| Auth0 B2C Professional | $1,600 | Quote | Quote | Self-serve ends at 20,000 MAU ($3,200); larger volumes are Enterprise contracts ([pricing][a0-pricing]) |

Separately priced security features and add-ons:

- **Entra External ID:**
  - SMS phone authentication: $0.03, $0.07, $0.15, or $0.35 per authentication by country tier.
  - Go-Local data residency: $0.02 per MAU (Australia and Japan only).
  - M2M tokens: $0.001 each.
  - ID Protection is not offered for external tenants at any price.
- **Cognito:**
  - Threat protection (compromised credentials, adaptive authentication, log export) requires Plus.
  - Microsoft or other OIDC/SAML federation adds $0.015 per federated MAU above 50.
  - SMS and email are billed through Amazon SNS and SES.
  - Multi-Region replication is an add-on.
- **Auth0:**
  - Breached-password detection, enhanced attack protection, and custom database connections require Professional.
  - Adaptive MFA requires Enterprise.
  - Enterprise MFA is a $100/month add-on to Essentials.
  - Log retention is 5, 10, or 30 days by plan.
  - Production SMS may need a customer-configured provider such as Twilio ([factors][a0-factors]).
- **First-party:** Twilio SMS and SMTP delivery at provider rates, plus the [ownership costs](#ownership-costs).

## Rejected alternatives

- **Microsoft Entra External ID:**
  - **Weaknesses:**
    - It doesn't support authenticator-app TOTP for customers, so existing TOTP users lose a factor.
    - Identity Protection is unavailable in external tenants.
    - Logs are kept for seven days, and export is still preview.
    - App-role support is being phased in.
    - At 1M MAU it is the most expensive of the published prices.
  - **Strengths:** Azure alignment and GA just-in-time password migration.
  - Revisit if Microsoft ships TOTP and longer log retention for external tenants, and the project standardizes on Azure-native identity.
- **Amazon Cognito:**
  - **Weaknesses:**
    - No native step-up.
    - Only one preferred MFA factor per user.
    - Passkeys can't follow a password as a second factor without user-verification configuration.
    - TOTP users must re-enroll.
    - Hashes and MFA secrets are not exportable.
    - It adds AWS to an Azure deployment.
  - **Strength:** the lowest managed cost at scale. It is not enough to offset continuity and compatibility losses.
  - No AWS identity pool is warranted: browsers don't need direct AWS credentials.
- **Auth0 now:** the best managed fit, but rejected for now:
  - Migration still discards all sessions.
  - The `int` id, AuthVersion, and club authorization still need first-party code.
  - The NFKC hash caveat needs a lazy path.
  - Pricing above 20,000 MAU is unpublished.

  It remains the [fallback](#decision).

## Fallback migration outline (Auth0)

1. **Prepare:**
   - Add an identity-link table (`provider`, `subject` → `UserId int`) and a feature flag.
   - Build the #329 provider-link model so it generalizes to an Auth0 `user_id`.
2. **Integrate (backend-for-frontend):**
   - The Angular app redirects to Universal Login with PKCE, and the API exchanges the code.
   - The API keeps issuing the existing HttpOnly refresh cookies, or replaces them with server-held Auth0 refresh tokens.
   - Access, refresh, and CSRF interceptors, the NgRx session, and SSR choices stay unchanged.
   - Callback routes render client-side.
3. **Import:**
   - Bulk-import users with Argon2id PHC / BCrypt hashes (already in the formats Auth0 requires), `user_id` set to our id, and TOTP/SMS/email enrollments, in batches under 500 KB.
   - Users whose NFKC-normalized password differs from the raw input use a custom-database login script against a hardened verification endpoint until they sign in once.
4. **Cut over:**
   - Enable the flag per cohort and watch sign-in failure rates.
   - Pre-announce a forced sign-in, since sessions don't transfer.
   - Keep the first-party login reachable for rollback through the flag.
5. **Decommission:**
   - After a fixed window, reset the remaining unmigrated users.
   - Remove the password, TOTP, and session code paths.
   - Keep `int` ids and application authorization.

## Risks of retaining first-party auth

| Risk | Mitigation |
|---|---|
| Security parity drifts as threats evolve (credential stuffing, phishing). | Deliver #325, #327, and #328 before new identity features. Follow NIST SP 800-63B and OWASP guidance. Add a dependency on maintained FIDO2 and crypto libraries rather than custom crypto. |
| Undetected compromise because audit data is thin. | #323 and #326 before Phase 3. #330 dashboards and alerts. |
| Single-maintainer knowledge risk. | Keep the API and configuration guides current. Integration tests for every flow. The identity-link abstraction keeps exit cost bounded. |
| Redis outage affects sessions. | Fail-closed revocation already exists. #325 adds authoritative session metadata. |
| Signing-key compromise or rotation outage. | #330 rotation procedures. Move to keyed (`kid`) signing so old and new keys overlap. |

## Decision triggers

Reopen this decision, starting with Auth0, if any of these occurs:

1. **Security incident:** a confirmed account-takeover incident caused by a first-party authentication defect.
2. **Deferred hardening:** #325, #327, or #328 not delivered within two quarters of this decision (by 2027-04-14).
3. **Maintenance load:** more than 6 engineer-days a month on identity upkeep, excluding planned feature work, sustained over a quarter.
4. **New requirement:** a product requirement for enterprise SSO (SAML or organization federation), B2B organizations, or a compliance attestation (SOC 2, ISO 27001) whose identity controls we can't evidence first-party.
5. **Data-residency demand:** a jurisdiction we can't serve from our own deployment.
6. **Scale:** sustained growth past 100,000 MAU. Obtain an Auth0 Enterprise quote at that point.
7. **Provider change:** a provider closes a rejection reason, for example Entra adding customer TOTP and retention beyond 30 days, or Auth0 publishing pricing above 20,000 MAU.

## Impact on Phases 2 and 3

All Phase 2 and 3 issues proceed as first-party work, with these constraints so a later move to a provider stays cheap.

| Issue | Proceed | Constraint from this decision |
|---|---|---|
| #325 sessions and devices | Yes | Make sessions authoritative in PostgreSQL keyed by `sid`. Keep the `sid` claim, which maps onto a provider session id. |
| #326 security history | Yes | Build on #323 events in a provider-neutral schema (actor, event type, method, outcome), so Auth0 log streams could feed the same table. |
| #327 recovery codes | Yes | Hash codes like passwords (Argon2id). Define how email fallback is narrowed once codes exist. |
| #328 passkeys | Yes | Use a maintained FIDO2 server library behind an interface. Store standard WebAuthn credential fields. Passkeys are scoped to the relying-party ID (our domain), so plan on users re-registering after any provider migration. |
| #329 provider linking | Yes, and first | Introduce the `(provider, subject) → UserId` link table. This is also the mapping every managed provider would need. Fix #346 first. |
| #331 resource authorization | Yes | Unaffected by the provider choice. Authorization stays application-owned under every option. |

## Review record

The repository owner reviewed this record as the backend, frontend, security, and deployment owner on 2026-10-14 and accepted it. Later reviewers should record dissent or amendments as a superseding ADR rather than editing the decision.

## References

- [NIST SP 800-63B, Digital Identity Guidelines: Authentication and Authenticator Management](https://pages.nist.gov/800-63-4/sp800-63b.html)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- Microsoft Entra External ID: [overview](https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam), [external tenant features][entra-features], [pricing and billing][entra-pricing-doc], [price list][entra-price], [migrate users][entra-migrate], [JIT password migration][entra-jit], [sign in with alias][entra-alias], [passkeys][entra-passkey]
- Amazon Cognito: [authentication](https://docs.aws.amazon.com/cognito/latest/developerguide/authentication.html), [feature plans][cog-plans], [pricing][cog-price], [MFA][cog-mfa], [user migration Lambda][cog-lambda], [refresh tokens][cog-refresh], [password recovery][cog-export]
- Auth0: [documentation](https://auth0.com/docs), [pricing][a0-pricing], [bulk import][a0-bulk], [import schema][a0-schema], [MFA factors][a0-factors], [refresh token rotation][a0-rotation], [step-up][a0-stepup], [log retention][a0-logs], [regions][a0-regions], [hash export][a0-export]

[entra-features]: https://learn.microsoft.com/en-us/entra/external-id/customers/concept-supported-features-customers
[entra-pricing-doc]: https://learn.microsoft.com/en-us/entra/external-id/external-identities-pricing
[entra-price]: https://azure.microsoft.com/en-us/pricing/details/microsoft-entra-external-id/
[entra-migrate]: https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-migrate-users
[entra-jit]: https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-migrate-passwords-just-in-time
[entra-alias]: https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-sign-in-alias
[entra-passkey]: https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-sign-in-with-passkey
[ms-claims]: https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference
[cog-plans]: https://docs.aws.amazon.com/cognito/latest/developerguide/cognito-sign-in-feature-plans.html
[cog-price]: https://aws.amazon.com/cognito/pricing/
[cog-mfa]: https://docs.aws.amazon.com/cognito/latest/developerguide/user-pool-settings-mfa.html
[cog-lambda]: https://docs.aws.amazon.com/cognito/latest/developerguide/cognito-user-pools-import-using-lambda.html
[cog-refresh]: https://docs.aws.amazon.com/cognito/latest/developerguide/amazon-cognito-user-pools-using-the-refresh-token.html
[cog-export]: https://docs.aws.amazon.com/cognito/latest/developerguide/managing-users-passwords.html
[a0-pricing]: https://auth0.com/pricing
[a0-bulk]: https://auth0.com/docs/manage-users/user-migration/bulk-user-imports
[a0-schema]: https://auth0.com/docs/manage-users/user-migration/bulk-user-import-database-schema-and-examples
[a0-factors]: https://auth0.com/docs/secure/multi-factor-authentication/multi-factor-authentication-factors
[a0-rotation]: https://auth0.com/docs/secure/tokens/refresh-tokens/refresh-token-rotation
[a0-stepup]: https://auth0.com/docs/secure/multi-factor-authentication/step-up-authentication
[a0-logs]: https://auth0.com/docs/deploy-monitor/logs/log-data-retention
[a0-regions]: https://auth0.com/docs/troubleshoot/customer-support/operational-policies/public-cloud-service-endpoints
[a0-export]: https://auth0.com/docs/manage-users/user-migration/export-password-hashes-and-mfa-secrets
