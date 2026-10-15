# Authentication security events

The API emits a structured stream of authentication and authorization security events, plus low-cardinality metrics, for alerting, dashboards, and the account security history. Events never contain credentials, tokens, codes, or full contact details. Recording an event never changes what a client sees, and a broken event destination cannot fail a request.

The contract lives in `backend/src/main/features/auth/events/`. A unit test compares the catalog and reason tables below with that code, so a change to one must be made in both.

## Pipeline

Auth code builds an `AuthSecurityEvent` and passes it to `IAuthSecurityEventEmitter`. The emitter adds request context (correlation id, client class, keyed hashes) and hands the finished event to every registered `IAuthSecurityEventSink`:

- **Logging sink:** one log entry per event at the catalog level, with the catalog `EventId` and every field rendered in the message and kept as structured properties.
- **Metrics sink:** increments `auth.security_events` on the `EventXperience.Auth` meter.

The emitter runs each sink in its own `try`/`catch`. A failing sink increments `auth.security_events.sink_failures` and logs the exception type (never its message); the other sinks and the request carry on. Sinks run inline, so a new sink must not block. The persisted security history (#326) should implement `IAuthSecurityEventSink` and hand events to a queue or outbox.

Two pieces of middleware cover what individual services cannot see:

- The authorization denial observer records every 403 as `authz.denied` when the response starts. That covers policy failures, `[RequireMfa]`, and `ForbiddenException` from any feature. A 403 another event already describes, such as a disabled account's failed sign-in, is not counted twice. 401 responses are not recorded, because expired access tokens produce them constantly.
- `[AuthOperation]` on auth endpoints records `auth.operation.duration`, classified from the final status code.

## Event catalog

Ids and names are append-only: never renumber, rename, or reuse one. Persist and query events by name, not by enum ordinal. The 32xxx range belongs to the abuse-protection store's operational events (`AuthAbuseRedisUnavailable` 32001, `AuthAbuseRedisRecovered` 32002, `AuthLoginFailureStateReset` 32004). 32003 was the interim throttle log line and is replaced by 33010.

| Id | Name | Level | Emitted when | Outcomes | Reasons |
| --- | --- | --- | --- | --- | --- |
| 33001 | `auth.sign_in.succeeded` | Information | A session is issued by password, OAuth, signup verification, device link, or login step-up sign-in. Refreshes are not sign-ins. | `succeeded` | `none` |
| 33002 | `auth.sign_in.failed` | Warning | A password, CAPTCHA, or provider token is refused, or a disabled account tries to sign in or refresh. | `denied` | `invalid_credentials`, `captcha_failed`, `account_disabled`, `provider_rejected` |
| 33003 | `auth.sign_in.challenged` | Information | Credentials were accepted but new-device verification or MFA step-up is required first. | `succeeded` | `device_verification_required`, `step_up_required` |
| 33010 | `auth.throttled` | Warning | An abuse budget, rate-limiter policy, or MFA delivery or attempt limit refuses a request. | `throttled` | `limit_exceeded` |
| 33020 | `auth.recovery.requested` | Information | Password or username recovery is requested. | `succeeded` | `issued`, `suppressed`, `provider_reminder` |
| 33021 | `auth.recovery.completed` | Information | A password reset completes. | `succeeded` | `none` |
| 33030 | `auth.verification.failed` | Warning | An emailed link or code for signup, password reset, email change, or a new device is refused. | `denied` | `expired`, `invalid_state`, `invalid_code`, `attempts_exhausted`, `auth_version_mismatch` |
| 33040 | `auth.step_up.succeeded` | Information | A login or in-session step-up code is verified. | `succeeded` | `none` |
| 33041 | `auth.step_up.failed` | Warning | A login or in-session step-up is refused, including a correct code whose proof could not be stored. | `denied`, `faulted` | `expired`, `invalid_state`, `invalid_code`, `attempts_exhausted`, `replayed`, `auth_version_mismatch`, `session_ended`, `none` |
| 33050 | `auth.factor.changed` | Information | SMS or TOTP MFA is enabled, disabled, or removed, or the password or email changes. | `succeeded` | `none` |
| 33051 | `auth.factor.change_failed` | Warning | A second-factor change or authenticated password change is refused or fails with a service fault. | `denied`, `faulted` | `invalid_credentials`, `account_disabled`, `invalid_code`, `invalid_state`, `invalid_request`, `rejected`, `none` |
| 33060 | `auth.refresh_token.reused` | Warning | A rotated refresh token is presented after the 10-second grace window, a theft signal. | `denied` | `replayed` |
| 33070 | `auth.session.revoked` | Information | One refresh session, or every session of an account, is ended. | `succeeded` | `logout`, `token_reuse`, `binding_missing`, `binding_mismatch`, `transport_mismatch`, `user_mismatch`, `auth_version_mismatch`, `password_changed`, `password_reset`, `email_changed`, `account_disabled`, `account_enabled`, `account_deleted` |
| 33080 | `auth.provider.linked` | Information | A Google or Microsoft identity is linked to an existing account through a verified email match. | `succeeded` | `none` |
| 33081 | `auth.provider_link.conflict` | Warning | An email match is refused because the account is linked to another identity, or Microsoft did not verify the address. | `denied` | `email_bound_to_other_provider`, `email_unverified` |
| 33090 | `authz.denied` | Warning | Any 403 response that no other event describes. | `denied` | `policy`, `mfa_required`, `resource_access` |

`auth.recovery.requested` reasons tell operators whether anything was sent. The API response deliberately hides that, so the reason must never be shown to the requester or in the user-facing history. A `throttled` factor change is recorded as `auth.throttled` with flow `mfa_enrollment`.

## Fields

| Field | Meaning |
| --- | --- |
| `EventName`, `EventId` | The catalog name and id. |
| `Outcome` | `succeeded`, `denied` (an expected refusal), `throttled`, or `faulted` (a service error). Clients never see this classification. |
| `Reason` | A code from the closed set below; never exception or request text. |
| `Method` | `password`, `google`, `microsoft`, `sms`, `totp`, `email`, or `refresh_token`. |
| `Flow` | The flow, verification purpose, or throttled budget, such as `login`, `signup`, `password_reset`, `email_change`, `device_verification`, `login_step_up`, `session_step_up`, `mfa_delivery`, or `mfa_enrollment`. |
| `Dimension` | For throttles: `source_ip`, `target`, `account`, `attempts`, or `rate_limiter`. |
| `Factor`, `Change` | For factor events: `sms`, `totp`, `password`, or `email`; `enabled`, `disabled`, `removed`, or `changed`. |
| `UserId` | The account the event is about, when known. It is stated by the code that emits the event and never inferred from a bearer token, since many auth endpoints are anonymous and act on a link, code, or name. The exception is `authz.denied`, which is about the caller. |
| `ActorUserId` | The account that caused the event when it is not `UserId`, such as an administrator disabling an account. Set only where that account authorized the action. |
| `SessionId` | The server-generated refresh session id. Absent on `auth.session.revoked` when every session of the account was revoked. |
| `TargetHash` | Keyed hash of the normalized username or email an event is aimed at when no account id is known, such as a failed sign-in. |
| `SourceIpHash` | Keyed hash of the client address, after forwarded-header processing. |
| `ClientClass`, `DeviceClass` | Coarse User-Agent classes: `browser`, `api_client`, or `unknown`; `desktop`, `mobile`, `tablet`, `api_client`, or `unknown`. The raw User-Agent is never recorded. |
| `Route` | For `authz.denied`: the HTTP method and route template, such as `DELETE api/profile`. The request path is never recorded. |
| `CorrelationId` | The request id echoed in `X-Request-Id`. A client-supplied id is kept only if it is 1–128 characters of `A-Z a-z 0-9 . _ : -`; anything else is replaced by a generated GUID. |

### Reason codes

| Reason | Meaning |
| --- | --- |
| `none` | No further classification. |
| `invalid_credentials` | Wrong username or password, or wrong current password. |
| `captcha_failed` | A required CAPTCHA was missing or rejected. |
| `account_disabled` | The account is disabled: an administrator disabled it, or a disabled account tried to sign in or refresh. |
| `provider_rejected` | Google or Microsoft refused the token or code, or its signature or claims failed validation. |
| `email_unverified` | The provider did not verify the email that would link an existing account. |
| `device_verification_required` | Sign-in needs a new-device email link. |
| `step_up_required` | Sign-in needs an MFA step-up. |
| `limit_exceeded` | A rate, attempt, or delivery limit was reached. |
| `issued` | A recovery email was sent. |
| `suppressed` | Recovery was accepted but nothing was sent (unknown, disabled, or credential-less account). |
| `provider_reminder` | A provider-only account was sent a sign-in reminder instead of a reset. |
| `invalid_code` | A link, code, or one-time password did not match. |
| `expired` | The link, code, or challenge is unknown or has expired. |
| `attempts_exhausted` | The final permitted attempt failed and the challenge was discarded. |
| `replayed` | A one-time artifact (refresh token, step-up challenge) was presented again. |
| `auth_version_mismatch` | The account's credentials changed after the token, link, or challenge was issued. |
| `invalid_state` | The artifact is malformed, for another purpose, or the flow is not in a state that allows it. |
| `invalid_request` | The request was rejected as invalid. |
| `rejected` | The request was refused for another expected reason. |
| `binding_missing` | A refresh came without its session binding token. |
| `binding_mismatch` | A refresh came with the wrong session binding token. |
| `transport_mismatch` | A refresh token issued for one transport (cookie or API) was used on the other. |
| `user_mismatch` | A legacy refresh token named a different account than its session. |
| `logout` | The user signed out. |
| `token_reuse` | The session was revoked because its refresh token was replayed. |
| `password_changed` | The user changed their password. |
| `password_reset` | The password was reset through recovery. |
| `email_changed` | The account email changed. |
| `account_enabled` | An administrator re-enabled the account. |
| `session_ended` | The refresh session ended (sign-out or revocation) before a verified step-up proof could be stored. |
| `account_deleted` | The user deleted the account. |
| `email_bound_to_other_provider` | The email belongs to an account linked to a different Google or Microsoft identity. |
| `policy` | An authorization policy or role check failed. |
| `mfa_required` | A `[RequireMfa]` route was called without a fresh step-up proof. |
| `resource_access` | A service refused access to a resource (ownership, staff role, account state). |

## Privacy and redaction

Events and their logs never contain:

- passwords, OTPs, or verification, reset, device, or step-up codes and links;
- OAuth ID tokens or authorization codes, access tokens, refresh tokens, session binding tokens, trusted-device tokens, or CAPTCHA tokens;
- usernames, email addresses, phone numbers, or client addresses in clear text;
- raw User-Agent strings, request paths, request or response bodies, or exception messages.

Every field is typed: enums from closed sets, numeric account ids, and server-generated session ids. A unit test fails if a free-form string property is added to the event, and unit and integration tests feed canary secrets through real flows and fail if any of them appears in an event, log entry, or metric tag.

Usernames, emails, and addresses that an event needs for correlation are recorded only as `HMAC-SHA256(key, "<kind>:<normalized value>")`, truncated to 128 bits. Usernames and emails are lowercased and trimmed; IP addresses are mapped to IPv6. Without the key, a hash cannot be reversed by hashing a dictionary of addresses. The key is `Auth:SecurityEvents:IdentifierHashKey` (see [configuration](CONFIGURATION.md#authentication-security-events)). Rotating it starts new hashes, so correlation across the rotation is lost; rotate it if it may have leaked.

Account ids, session ids, and hashes are still pseudonymous personal data. Restrict access to the event log as you would to other account records.

## Metrics

All instruments are on the `EventXperience.Auth` meter. Every tag value comes from a closed set. Account ids, session ids, hashes, addresses, routes, and correlation ids are never tags.

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `auth.security_events` | Counter | `{event}` | `event`, `outcome`, `reason`, `method`, `flow`, `dimension`, `factor` |
| `auth.security_events.sink_failures` | Counter | `{failure}` | `sink` |
| `auth.operation.duration` | Histogram | `ms` | `operation`, `outcome` |

`operation` is one of `sign_in`, `oauth_sign_in`, `refresh`, `step_up_verify`, `recovery_request`, `password_reset`, `verification`, or `factor_change`. Its `outcome` is `succeeded` for 2xx and 3xx, `throttled` for 429, `denied` for any other 4xx, and `faulted` for 5xx or an unhandled exception. Requests the client abandons are not recorded. Unset event tags are reported as `none`.

No exporter is configured yet; choosing one is part of #330. Until then, read the meter from a running API with [`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters):

```sh
dotnet-counters ps
dotnet-counters monitor --process-id <api-pid> --counters EventXperience.Auth
```

## Retention

| Data | Recommendation |
| --- | --- |
| Security event log entries | Keep 30 days searchable for investigations; archive up to 90 days if incident response needs it. They hold pseudonymous identifiers, so do not keep them longer than that need. |
| Metrics | Aggregates without identifiers; keep 13 months for seasonal baselines. |
| Account security history | Persisted separately for 90 days by #326 from this same contract. |

## Dashboards and alerts

Recommended panels:

| Dashboard | Source |
| --- | --- |
| Sign-in outcomes | `auth.security_events` for `auth.sign_in.*` by `outcome`, `reason`, and `method` |
| Throttling by dimension | `auth.throttled` by `flow` and `dimension` |
| Recovery and MFA delivery | `auth.recovery.requested` by `reason`; `auth.throttled` with flow `mfa_delivery` |
| Step-up and verification failures | `auth.step_up.failed` and `auth.verification.failed` by `reason` and `flow` |
| Token reuse and revocation | `auth.refresh_token.reused`; `auth.session.revoked` by `reason` |
| Provider linking | `auth.provider.linked` and `auth.provider_link.conflict` by `method` and `reason` |
| Authorization denials | `authz.denied` by `reason`; top `Route` values from the log |
| Auth latency and faults | `auth.operation.duration` p50 and p95 by `operation`; share of `outcome="faulted"` |
| Pipeline health | `auth.security_events.sink_failures` by `sink` |

Example alert conditions follow. The names are as the OpenTelemetry Prometheus exporter renders the instruments (dots become underscores and counters gain `_total`); adapt them to the backend chosen in #330. Enable alerts gradually: run each in notify-only mode for two weeks, set thresholds from the measured baseline (for example, three times the baseline p99), then page.

| Condition | Example | Why |
| --- | --- | --- |
| Credential stuffing | `sum(rate(auth_security_events_total{event="auth.sign_in.failed",reason="invalid_credentials"}[5m]))` above baseline | Many failed passwords across accounts. |
| Targeted account attack | `sum(rate(auth_security_events_total{event="auth.throttled",dimension="account"}[15m]))` above baseline | Repeated failures against individual accounts. |
| Token theft | `increase(auth_security_events_total{event="auth.refresh_token.reused"}[10m]) > 0` sustained or rising | Replayed refresh tokens. Investigate the user and session ids in the log. |
| Provider takeover attempts | `increase(auth_security_events_total{event="auth.provider_link.conflict"}[1h])` above baseline | Reassigned addresses or forged tenants. |
| Step-up brute force | `sum(rate(auth_security_events_total{event="auth.step_up.failed",reason="attempts_exhausted"}[15m]))` above baseline | Code guessing. |
| Authorization probing | `sum(rate(auth_security_events_total{event="authz.denied",reason="resource_access"}[5m]))` above baseline | One caller walking resource ids. Group the log by `UserId` and `Route`. |
| Auth faults | Faulted share of `auth_operation_duration_milliseconds_count` above 2% for 10 minutes | Service errors, as distinct from refusals. |
| Event pipeline broken | `increase(auth_security_events_sink_failures_total[5m]) > 0` | Events are being lost. |

To investigate a user report, ask for the `X-Request-Id` from the failing response and search the log for that `CorrelationId`.
