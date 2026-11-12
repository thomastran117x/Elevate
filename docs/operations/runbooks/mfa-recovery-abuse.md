# MFA, step-up, and recovery abuse

| Alert | Severity | Owner |
| --- | --- | --- |
| [`AuthStepUpBruteForce`](#authstepupbruteforce) | page | Auth on-call |
| [`AuthMfaRecoveryAbuse`](#authmfarecoveryabuse) | ticket | Auth on-call |
| [`AuthMfaDeliveryFailures`](#authmfadeliveryfailures) | ticket | Auth on-call |

Owners and escalation are defined in [ownership](../ownership.md). Open an [incident record](../incident-record.md) when you acknowledge a page.

## AuthStepUpBruteForce

**Meaning:** step-up challenges are being discarded after their final wrong code (`auth.step_up.failed` with `attempts_exhausted`) at more than 0.01/s for 15 minutes. Someone who already has valid passwords is guessing second-factor codes. Each challenge allows only a few attempts, so guessing a 6-digit code is unlikely, but the passwords themselves are compromised.

### Triage

1. In the **Step-up and verification failures** row, check the `flow`. `login_step_up` is someone signing in with a stolen password. `session_step_up` is someone already holding a session, which is more serious.
2. List the accounts:

   ```sh
   docker compose logs --since 1h backend \
     | grep 'Auth security event auth.step_up.failed' | grep 'reason=attempts_exhausted' \
     | grep -oE 'method=[^ ]* flow=[^ ]* .* user=[0-9]* .* source=[^ ]*' | sort | uniq -c | sort -rn | head
   ```

### Contain

- Every account listed has a compromised password. Disable each one (`PATCH /api/admin/users/{id}/status`) until its owner resets the password through recovery, or contact the owner through a trusted channel.
- For `session_step_up`, the attacker holds a live session. Disabling the account revokes it.
- If the attempts come from few sources, block them at the ingress.

### Verify and close

Record the affected accounts, the method mix (`totp`, `sms`, `email`, `recovery_code`), and confirm each owner has reset their password.

## AuthMfaRecoveryAbuse

**Meaning:** MFA code delivery is being throttled (`auth.throttled` with `flow=mfa_delivery`), or password and username recovery is being requested, well above baseline for 15 minutes. Typical causes:
- SMS pumping, which runs up the SMS bill;
- mail bombing a victim;
- enumerating which addresses have accounts.

Delivery limits and recovery suppression already contain the damage.

### Triage

1. In the **Recovery and MFA delivery** row:
   - Recovery requests by `reason`. `suppressed` means no account, or a disabled or credential-less account, so it was sent nothing, and a surge of it is enumeration. `issued` means mail really is going out.
   - `mfa delivery throttled` by `dimension`. `target` means one recipient is being flooded. `source_ip` means one source is sending to many.
2. Check whether the attack targets a few hashes or many:

   ```sh
   docker compose logs --since 1h backend \
     | grep -E 'Auth security event (auth.recovery.requested|auth.throttled .*flow=mfa_delivery)' \
     | grep -o 'target=[^ ]*' | sort | uniq -c | sort -rn | head
   ```

3. For SMS, check the provider console for spend and destination countries.

### Contain

- **Tighten per-target limits:**
  - `Auth__AbuseProtection__MfaDeliveryTargetPermitLimit` (`AUTH_ABUSE_MFA_DELIVERY_TARGET_LIMIT`, default 5 per 15 minutes);
  - `Auth__AbuseProtection__RecoveryTargetPermitLimit` (`AUTH_ABUSE_RECOVERY_TARGET_LIMIT`, default 3 per hour).
- **Stop SMS pumping:** set `AUTH_SMS_MFA_STEP_UP_SMS_ENABLED=false`. SMS step-up then stops and users fall back to their other factors. If SMS enrollment is being abused, also set `AUTH_SMS_MFA_ENROLLMENT_ENABLED=false`. Both are on the API and the SMS worker; restart both.
- **Block at the edge:** block the sources at the ingress.

### Verify and close

Record which signal fired, the target hashes, the provider spend impact, and every setting changed and when it was restored.

## AuthMfaDeliveryFailures

**Meaning:** more than 5% of security-relevant deliveries failed over 30 minutes, with at least 10 deliveries. That covers verification, reset, new-device, MFA, username and provider reminders, email-change verification, and security alerts. Users cannot finish signing in or recovering. This is an availability problem, not an attack, but it blocks recovery during one.

### Triage

1. In the **Recovery and MFA delivery** row, see whether email, SMS, or both are failing, and which `message_type`.
2. Read the worker logs for the provider error. They log the failure; the metrics never carry it.

   ```sh
   docker compose logs --since 1h email-worker sms-worker | grep -i 'delivery failed'
   ```

3. Check the provider credentials (`SMTP_SERVER`, `EMAIL_USER`, `EMAIL_PASSWORD`, and the Twilio settings), provider status pages, and the DLQ topics (`EMAIL_DLQ_TOPIC`, `SMS_DLQ_TOPIC`).

### Contain

Fix or rotate the provider credentials (see [key rotation](../key-rotation.md#provider-and-integration-secrets)). Failed messages are in the DLQ. Codes and links expire quickly, so tell affected users to request a new one rather than replaying the DLQ.

### Verify and close

The failure share returns to baseline. Record the cause, the window, and how many messages failed.

## Rehearse

- **Alert logic:** the promtool cases `exhausted step-up ...`, `throttled MFA delivery ...`, `a recovery request surge ...`, and `failing MFA emails ...` / `failing SMS codes ...` in `docker/monitoring/prometheus/tests/auth-alerts.test.yml`.
- **End to end:** `auth-drill --scenario recovery` requests a password reset and follows the emailed link, which counts a `reset_password` delivery. `--scenario session-revocation` uses email step-up codes, which count `mfa_code` deliveries. To rehearse delivery failure, point `email-worker` at an unreachable `SMTP_SERVER` in a non-production stack, run the drill, and watch the failure share rise. The drill itself fails, as users would. See [drills](../drills.md).
