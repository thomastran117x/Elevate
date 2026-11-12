# Token theft, refresh-token reuse, and failed revocation

| Alert | Severity | Owner |
| --- | --- | --- |
| [`AuthRefreshTokenReuse`](#authrefreshtokenreuse) | page | Security lead |
| [`AuthSessionRevocationFaulted`](#authsessionrevocationfaulted) | page | Auth on-call |

Owners and escalation are defined in [ownership](../ownership.md). Open an [incident record](../incident-record.md) when you acknowledge a page.

## AuthRefreshTokenReuse

**Meaning:** more than 3 rotated refresh tokens per hour, and more than 3x the hourly p99, were presented after the 10-second grace window. Each replay already revoked its session (`auth.session.revoked` with reason `token_reuse`), so whoever held the newest token, user or attacker, was signed out. Several replays together suggest tokens are being stolen, for example by malware or a compromised browser extension, or leaked from a log or proxy.

### Triage

1. In the **Token reuse and session revocation** row, check whether the replays are spread over time or concentrated.
2. List the affected accounts and sessions:

   ```sh
   docker compose logs --since 2h backend \
     | grep 'Auth security event auth.refresh_token.reused' \
     | grep -oE 'user=[0-9]* .* session=[^ ]* .* source=[^ ]* client=[^ ]* device=[^ ]*'
   ```

   Look for any of these:
   - the same `source` hash replaying tokens for many users, which is one attacker holding many tokens;
   - API-client (`client=api_client`) replays of browser sessions;
   - one user replaying repeatedly, which is usually a broken client retrying, not theft.
3. Check whether anything logs or forwards tokens. Look at recent ingress, proxy, or APM changes, because refresh tokens must never be captured.

### Contain

- The replayed sessions are already dead. For each affected user who may still have a live attacker session, revoke the rest: the owner can sign out every session from **Account > Security**. An operator can disable and then re-enable the account (`PATCH /api/admin/users/{id}/status`), which revokes every session.
- If one `source` hash replays tokens for many users, block it at the ingress, and treat its access tokens (15-minute lifetime) as compromised until they expire.
- If the access-token signing key itself may have leaked, rotate it now. Follow [key rotation](../key-rotation.md#access-token-signing-key-jwt_secret_access) and **do not** keep the old key as a previous key.

### Verify and close

Record:

- the affected user ids and session counts;
- the source hashes;
- the suspected theft vector, and whether it was confirmed;
- the revocations and key rotations done;
- for each affected user, confirmation that `GET /api/auth/sessions` lists no session they do not recognise. Ask the user, or check `AuthSessions` rows.

## AuthSessionRevocationFaulted

**Meaning:** an account-wide revocation, such as for a password reset, an account disable, an email change, or a provider unlink, could end no sessions, because neither the session registry (PostgreSQL) nor Redis answered. The user believes their other sessions were ended, but they may still be alive.

### Triage

1. Find the event and its account:

   ```sh
   docker compose logs --since 1h backend | grep 'Auth security event auth.session.revoked outcome=faulted'
   ```

2. Check PostgreSQL and Redis health. Look for connection errors around the event time in `docker compose logs backend postgres redis`.

### Contain

Once the database and Redis are healthy, repeat the revocation for each affected `user=`. Disable and re-enable the account with `PATCH /api/admin/users/{id}/status`. That bumps `AuthVersion`, which invalidates every access token, and revokes every session. Then confirm `GET /api/auth/sessions`, made as that user or read from `AuthSessions`, shows only the session you expect.

### Verify and close

Record the affected accounts, the root cause of the database or Redis failure, and confirmation that each account's sessions were revoked afterwards.

## Rehearse

- **Alert logic:** the promtool cases `refresh token reuse ...` and `a faulted account-wide revocation ...` in `docker/monitoring/prometheus/tests/auth-alerts.test.yml`.
- **End to end:** `auth-drill --scenario token-reuse` replays a rotated refresh token past the grace window. It checks that the session is revoked, that the account history records `refresh_token_reuse_detected`, and, with `--prometheus-url`, that the counter moved. `auth-drill --scenario session-revocation` exercises sign-out of other sessions. See [drills](../drills.md).
