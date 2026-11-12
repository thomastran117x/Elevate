# Key and secret rotation

How to rotate each auth-related secret, what it breaks, how to roll it back, and how to verify it without touching unrelated secrets. The security lead owns rotations (see [ownership](ownership.md)). Rotate on schedule during the [quarterly review](review-checklist.md), and at once if a secret may have leaked.

Every secret is a deployment environment variable. Generate new values on a trusted machine, store them only in the secret store, and never paste them into tickets, chat, or incident records. Record **that** a rotation happened, when, and the new key id where one exists, never the value.

## Summary

| Secret | Overlap supported | Rotation breaks | Rollback |
| --- | --- | --- | --- |
| `JWT_SECRET_ACCESS` | Yes, through `JWT_SECRET_ACCESS_PREVIOUS` | Nothing while the previous key is kept | Swap the two values |
| `AUTH_TOTP_ENCRYPTION_KEY` | Yes, through `AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS` and the re-encryption sweep | Nothing while the previous key is kept | Swap the two values; the sweep moves secrets back |
| `GOOGLE_CLIENT_SECRET` | Yes, at Google, which allows two active client secrets | Nothing | Re-enable the old secret at Google and redeploy it |
| `JWT_SECRET_VERIFICATION` | No | Codes, links, and step-up proofs in flight (minutes) | Restore the old value; anything issued under the new one is lost |
| `Auth__RecoveryCodes__Pepper` | No | Every stored recovery code | Restore the old value before users regenerate |
| `Auth__SecurityEvents__IdentifierHashKey` | No | Correlation of event hashes across the change | Restore the old value |
| SMTP, Twilio, Stripe, and Azure storage credentials | At the provider | Delivery or payments during the switch | Re-enable the old credential at the provider |

## Access-token signing key (`JWT_SECRET_ACCESS`)

Access tokens are HS256 JWTs that live 15 minutes. Each carries a `kid` naming its signing key. Refresh tokens, sessions, and verification proofs do not depend on this key, so rotating it leaves every session intact. See [access-token signing keys](../CONFIGURATION.md#access-token-signing-keys).

**Rotate:**

1. Generate a new key of at least 32 bytes, for example with `openssl rand -base64 48`.
2. Deploy with `JWT_SECRET_ACCESS=<new>` and `JWT_SECRET_ACCESS_PREVIOUS=<old>`, on every API replica. Replicas still on the old configuration keep validating new tokens only once they restart, so roll every replica in one deployment.
3. Verify:
   - A token issued before the deploy still works on `GET /api/auth/me`.
   - A new sign-in's access token has the new `kid` in its header. The kid is base64url of the first 8 bytes of SHA-256 of the key; compare it with the token header, never the key.
   - `AuthFaultRate` and the **Sign-in outcomes** row stay flat.
4. After at least 16 minutes (token lifetime plus clock skew), deploy again with `JWT_SECRET_ACCESS_PREVIOUS` removed.

**Roll back:** deploy with the two values swapped: `JWT_SECRET_ACCESS=<old>` and `JWT_SECRET_ACCESS_PREVIOUS=<new>`. Tokens signed by either key keep working. Remove the previous key later, as in step 4.

**Suspected leak:** skip the overlap. Deploy the new key **without** a previous key. Every access token is refused at once, and clients refresh transparently, because refresh does not depend on this key; API clients see one 401. Then rotate `JWT_SECRET_VERIFICATION` if it was exposed too.

## TOTP encryption key (`AUTH_TOTP_ENCRYPTION_KEY`)

TOTP shared secrets are encrypted at rest with AES-256-GCM. Each stored secret names the key that encrypted it (`v2:{kid}:...`); secrets from before key ids are `v1:...`. See [TOTP secret encryption](../CONFIGURATION.md#totp-secret-encryption).

**Rotate:**

1. Generate a new key: `openssl rand -base64 32`, which must decode to exactly 32 bytes.
2. Deploy with `AUTH_TOTP_ENCRYPTION_KEY=<new>` and `AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS=<old>`. New enrollments use the new key, and existing secrets still decrypt with the old one.
3. At startup, and then hourly, the API re-encrypts stored secrets under the new key, in batches, with conditional updates. Watch the log for `[TotpKeyRotation]`. It reports how many secrets were re-encrypted, changed concurrently, could not be decrypted, and remain under a previous key, and never logs a secret.
4. Verify:
   - A user with TOTP, such as a test account, can still sign in with a TOTP code.
   - In the database, `SELECT count(*) FROM "TotpMfaEnrollments" WHERE "EncryptedSecret" NOT LIKE 'v2:<new kid>:%'` reaches 0. The new kid is the base64url of the first 8 bytes of SHA-256 of the decoded key bytes; the `[TotpKeyRotation]` log line prints it.
5. Only once the log reports **0 remaining and 0 undecryptable**, deploy with `AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS` removed. Removing it earlier makes the remaining users' TOTP fail closed: they cannot verify TOTP until the key is restored.

**Roll back:** deploy with the values swapped: `AUTH_TOTP_ENCRYPTION_KEY=<old>` and `AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS=<new>`. Every secret still decrypts, and the sweep moves them back under the old key.

**Undecryptable secrets** are secrets that no configured key opens, for example because an older key was removed too early. Restore that key as a previous key; the sweep then re-encrypts them. If the key is lost, those users must remove TOTP and enroll again, using a recovery code or another factor to sign in.

## Verification secret (`JWT_SECRET_VERIFICATION`)

This is an HMAC key, not a JWT key. It protects emailed verification codes, MFA enrollment proofs, and session step-up code hashes, all of which live for minutes. There is no overlap. After a rotation, codes and links issued before it fail, and users request new ones; stored step-up proofs stop matching, so users step up again. Rotate it in a quiet period. Roll back by restoring the old value, which fails anything issued in between in the same way.

## Recovery-code pepper (`Auth__RecoveryCodes__Pepper`)

The pepper is part of every recovery code's Argon2id hash and selector. Changing it invalidates **every** stored recovery code. Rotate it only if it may have leaked. Then tell every user with recovery codes to regenerate them from **Account > Security**, because their stored codes stop working even though the page still counts them. Keep the old value in the secret store's history, so you can roll back before users regenerate.

## Security-event identifier hash key (`Auth__SecurityEvents__IdentifierHashKey`)

The key behind `TargetHash` and `SourceIpHash` in the event log. Rotating it breaks correlation between hashes made before and after the change, and nothing else. It takes effect on restart. Rotate it if it may have leaked, and note the time in the event log's retention record, so investigations know where correlation breaks.

## Provider and integration secrets

- **Google client secret (`GOOGLE_CLIENT_SECRET`):** in Google Cloud Console, add a second client secret for the OAuth client and deploy it. Confirm Google sign-in works (the **Sign-in outcomes** row by method `google`), then disable the old secret. To roll back, re-enable the old secret and deploy it. Microsoft and Apple sign-in validate signed tokens against published keys and hold no secret here.
- **SMTP (`EMAIL_USER`, `EMAIL_PASSWORD`) and Twilio (`TWILIO_*`):** create the new credential at the provider, deploy it to the email or SMS worker, and confirm the **Recovery and MFA delivery** row shows `sent` deliveries. Then revoke the old one. A failed switch shows up as `AuthMfaDeliveryFailures`.
- **Stripe webhook secret (`STRIPE_WEBHOOK_SECRET`):** Stripe lets an endpoint secret be rolled with an overlap period. Deploy the new secret within it.
- **Azure storage connection string:** rotate the account key that is not in use, deploy the connection string for it, then rotate the other.

## Rehearsal

Rehearse each quarter in a non-production stack, and before any planned production rotation. Record it in the [drill log](drill-log.md).

1. Sign in, and keep the access token.
2. Rotate `JWT_SECRET_ACCESS` with the old key as previous, and restart the API. The kept token still works on `GET /api/auth/me`, and a new sign-in's token carries the new `kid`.
3. Roll back by swapping the values, and restart. Both tokens work.
4. Remove the previous key, and restart. The token signed by the removed key is refused with 401; the session's refresh still works.
5. Enroll TOTP on a test account. Rotate `AUTH_TOTP_ENCRYPTION_KEY` with the old key as previous, and restart. The `[TotpKeyRotation]` log reports the secret re-encrypted, and TOTP step-up still accepts a code.
6. Roll back by swapping, and restart. The secret moves back, and TOTP still works.
7. Confirm nothing else changed: existing sessions and refresh tokens, recovery codes, and the event hashes for a known test username are the same as before.
