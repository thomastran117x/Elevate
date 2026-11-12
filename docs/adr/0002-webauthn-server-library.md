# ADR 0002: Verify passkeys with the Fido2 server library

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-11-11 |
| Issue | [#328](https://github.com/thomastran117x/Elevate/issues/328), Phase 2 of epic [#318](https://github.com/thomastran117x/Elevate/issues/318) |
| Deciders | Repository owner, reviewing as backend, frontend, security, and deployment owner |
| Evidence date | Package versions, targets, and advisories verified 2026-11-11 |

[ADR 0001](0001-identity-provider-build-vs-buy.md) kept authentication first-party. For passkeys (#328), it requires "a maintained FIDO2 server library behind an interface". This record chooses that library and sets how the API depends on it.

## Decision

**Use [Fido2](https://www.nuget.org/packages/Fido2) (fido2-net-lib) 4.3.1, wrapped behind `IWebAuthnRelyingParty`.**

- **The library does the protocol work.** It builds the registration and authentication options. It also parses and verifies attestation and assertion responses:
  - client data type, challenge, and origin
  - the RP ID hash
  - the user-present, user-verified, and backup flags
  - the COSE public key and the signature
- **The API does the policy.** It decides:
  - the ceremony lifecycle (random, expiring, single-use, bound to one ceremony type)
  - credential ownership
  - the counter policy for synced and device-bound credentials
  - the last sign-in method rule
  - security events and notifications
- **Only the adapter references Fido2 types.** The adapter is `backend/src/main/features/auth/passkeys/WebAuthnRelyingParty.cs`. Controllers, services, and contracts pass WebAuthn JSON as opaque `JsonElement` values and use the adapter's own result records, so the library can be replaced without changing the API contract.
- **No metadata service and no attestation trust.** Registration asks for `none` attestation. Accepting a passkey therefore doesn't depend on the authenticator's maker, and the FIDO Metadata Service code path (the subject of GHSA-gw6g-q29j-3jvp) is never configured.
- **Pin an exact version that is at least 14 days old.** Dependabot proposes later versions, and they are reviewed like any other dependency.

## Context

Passwords, OAuth, and typed one-time codes can all be phished. WebAuthn binds every signature to the relying-party ID, so a lookalike site can't reuse it. The issue rules out custom cryptography and handwritten WebAuthn parsing. The decision is therefore which maintained implementation to adopt, not whether to adopt one.

## Options

| Option | Fit | Notes |
|---|---|---|
| **Fido2 (fido2-net-lib) 4.x** | Chosen | MIT licensed and actively maintained. 4.1.0 onwards targets net10.0 and net8.0. It has no dependency on ASP.NET Core Identity. Security fixes have shipped promptly: 4.3.1 fixed GHSA-gw6g-q29j-3jvp, a metadata BLOB key confusion that does not affect `none` attestation. |
| ASP.NET Core Identity passkeys (.NET 10) | Rejected | Built in, but tied to `UserManager`, `SignInManager`, and Identity stores. Using them means adopting ASP.NET Core Identity, which ADR 0001 decided against, and the account model, sessions, and AuthVersion would need adapting to it. |
| Fido2 5.0 previews | Rejected for now | Prerelease. Revisit once 5.0 is stable and has been out for two weeks. |
| A handwritten verifier | Rejected | Out of scope for #328. It would mean maintaining CBOR, COSE, and attestation parsing ourselves. |

Versions checked on 2026-11-11:
- 4.3.1 was published 2026-10-16.
- 4.3.2 (2026-10-29) changes the package signing trust, the `appid` extension, and error-code flags. None of these are security fixes.
- 4.3.3 (2026-11-11) only bumps `System.Formats.Cbor`.
- 4.3.1 is the newest release that meets the 14-day rule.

## Consequences

- WebAuthn JSON crosses the API as opaque objects. OpenAPI describes them as free-form objects rather than typed WebAuthn schemas.
- The library's counter check rejects any regression. To avoid locking out synced passkeys, which may report a zero or out-of-order counter across devices, the adapter passes a stored counter of 0 for backup-eligible credentials. The API then compares counters itself and records anomalies without rejecting. Device-bound credentials keep the library's strict check. See the [API guide](../API.md#passkeys).
- Passkeys are scoped to the configured RP ID. Changing the domain, or leaving first-party auth as ADR 0001's fallback describes, means users register their passkeys again.
- The test projects build real `none` attestations and assertions with a software authenticator. Integration tests then exercise the real library rather than a mock.

## Decision triggers

Revisit this record if any of the following happen:
1. A Fido2 advisory affects the code paths we use and is not fixed in a 4.x release.
2. 4.x stops receiving fixes after 5.0 is released.
3. ADR 0001 is superseded by a managed identity provider.
4. We need attestation trust, for example to allow only certain authenticator models. That would require the metadata service and a new review.

## References

- [Web Authentication: Level 3](https://www.w3.org/TR/webauthn-3/)
- [NIST SP 800-63B](https://pages.nist.gov/800-63-4/sp800-63b.html), syncable authenticators
- [fido2-net-lib](https://github.com/passwordless-lib/fido2-net-lib) and [GHSA-gw6g-q29j-3jvp](https://github.com/passwordless-lib/fido2-net-lib/security/advisories/GHSA-gw6g-q29j-3jvp)
