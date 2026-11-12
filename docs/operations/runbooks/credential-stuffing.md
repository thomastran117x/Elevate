# Credential stuffing and targeted account attacks

| Alert | Severity | Owner |
| --- | --- | --- |
| [`AuthCredentialStuffing`](#authcredentialstuffing) | page | Auth on-call |
| [`AuthTargetedAccountThrottling`](#authtargetedaccountthrottling) | ticket | Auth on-call |

Owners and escalation are defined in [ownership](../ownership.md). Open an [incident record](../incident-record.md) when you acknowledge a page.

## AuthCredentialStuffing

**Meaning:** failed sign-ins with `invalid_credentials` are above both 3x their 14-day p99 and 0.5/s for 10 minutes. Leaked username and password pairs are being tried across many accounts.

### Triage

1. Open the [Auth security dashboard](../monitoring.md#dashboard), rows **Sign-in outcomes** and **Account and IP throttles**. Note when the rise started and whether `auth.sign_in.succeeded` rose with it. Successes rising alongside failures mean some guesses are working.
2. Check how concentrated the sources are. The event log has `source` (keyed hash of the client address) and `target` (keyed hash of the username or email):

   ```sh
   docker compose logs --since 30m backend \
     | grep 'Auth security event auth.sign_in.failed' \
     | grep -o 'source=[^ ]*' | sort | uniq -c | sort -rn | head
   ```

   A few sources with high counts are a botnet's exit nodes or a single scraper. Thousands of sources with one or two attempts each are a distributed campaign. In that case the per-source budget will not bite, and the per-account budget is what protects users.
3. Find accounts that were broken into. Compare `user=` values on `auth.sign_in.succeeded` in the window against accounts that also had `auth.sign_in.failed`. A sign-in from a new device produces `auth.sign_in.challenged` with `reason=device_verification_required` first. That is the control working, and those users are not yet compromised.

   ```sh
   docker compose logs --since 30m backend | grep 'auth.sign_in.succeeded' | grep -o 'user=[0-9]*' | sort -u
   ```

### Contain

Use the lightest lever that stops the rise. Every setting below is an environment variable on the API (Compose names in brackets; see [configuration](../../CONFIGURATION.md#authentication-abuse-protection)) and takes effect on restart.

- **Lower the per-source budget:** `Auth__AbuseProtection__SharedIpPermitLimit` (`AUTH_ABUSE_SHARED_IP_LIMIT`, default 10 per 5 minutes). Effective against concentrated sources; useless against a distributed one.
- **Require CAPTCHA sooner:** `Auth__AbuseProtection__CaptchaFailureThreshold` (`AUTH_ABUSE_CAPTCHA_THRESHOLD`, default 3). `1` challenges after the first failure for each account.
- **Lower the per-account budget:** `Auth__AbuseProtection__LoginAccountFailureLimit` (`AUTH_ABUSE_LOGIN_ACCOUNT_LIMIT`, default 10 per 15 minutes). This also locks real users out sooner, so note it in the incident record.
- **Block at the edge:** if the ingress or WAF supports it, rate-limit or block the offending networks there. The API never sees raw addresses in its event log, so take addresses from the ingress logs.
- **For a confirmed compromised account:** disable it with `PATCH /api/admin/users/{id}/status` and `{"isDisabled": true, "reason": "<incident id>"}`. This revokes every session (`auth.session.revoked` with reason `account_disabled`). Re-enable it once the owner has reset their password through recovery.

### Verify and close

The alert resolves on its own once the rate drops below the threshold. Before closing, record in the incident record:

- the start and end times, and the peak rate from the dashboard;
- how many distinct `source` and `target` hashes were involved;
- the accounts with successful sign-ins in the window and what was done for each;
- every setting changed, and when it was restored;
- whether the threshold was right (see [rollout](../monitoring.md#rollout)).

## AuthTargetedAccountThrottling

**Meaning:** the per-account abuse budget (`dimension=account`) is refusing more than 0.05/s for 15 minutes. Specific accounts are being attacked from many sources, so the per-source limit does not trip.

### Triage

1. In the **Account and IP throttles** row, confirm that `account` dominates the dimension panel and see which `flow` is throttled.
2. List the targeted accounts by their hashed target:

   ```sh
   docker compose logs --since 1h backend \
     | grep 'Auth security event auth.throttled' | grep 'dimension=account' \
     | grep -o 'target=[^ ]*' | sort | uniq -c | sort -rn | head
   ```

   A handful of targets are deliberate attacks on high-value accounts, such as administrators or club owners. Many targets are a stuffing campaign that is tripping the account budget; handle those under [`AuthCredentialStuffing`](#authcredentialstuffing).
3. To map a hash to an account, compute it for a suspected username or email with the identifier hash key. The algorithm is in [security events](../../SECURITY_EVENTS.md#privacy-and-redaction). Never paste the key or the plaintext into the incident record.

### Contain

The account budget is already refusing the attempts, so the account is protected unless the attacker has the password. For a targeted administrator or owner account:

- Ask the owner, through a channel you already trust, to enroll a passkey or TOTP if they have not.
- If you suspect the password is known, disable the account until the owner resets it (see above).

### Verify and close

Record the targeted hashes (never the plaintext identities), the duration, and the outcome for each targeted account.

## Rehearse

- **Alert logic:** the promtool cases `credential stuffing ...` and `account throttles ...` in `docker/monitoring/prometheus/tests/auth-alerts.test.yml` inject these signals. See [testing the rules](../monitoring.md#testing-the-rules).
- **End to end:** `auth-drill --scenario telemetry` sends a burst of wrong passwords until the source is throttled, then checks that the counters moved. See [drills](../drills.md). Run it with the alert's floor temporarily lowered to watch it go pending in observe mode.
