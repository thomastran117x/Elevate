# Test Suite Layout

This test project is organized by application boundary so auth coverage can grow without turning the root folder into a grab bag.

## Folders

- `Controllers/Auth`
  - HTTP contract tests for auth endpoints.
  - Focus on request/response mapping, transport selection, cookie behavior, and auth-specific controller branching.
- `Services/Auth`
  - Unit tests for auth domain services such as `AuthService`, `TokenService`, and `DeviceService`.
  - Focus on auth rules, token/session state, device verification, and OAuth signup completion.
- `Services/Captcha`
  - Unit tests for captcha providers and captcha policy behavior.
  - Focus on provider success/failure handling, bypass rules, and request composition.
- `Seeders`
  - Startup seeding policy tests and seeder-specific coverage.
  - Focus on when seeders run, env-driven behavior, and idempotent seed construction rules.
- `Utilities`
  - Low-level auth utility tests such as cookie/header handling helpers.
- `TestSupport`
  - Shared test doubles and in-memory helpers used across the auth suite.

## Coverage Goals

The auth test suite should keep these areas covered:

- Controller behavior for local auth, OAuth auth, verification, refresh, logout, and OAuth signup completion.
- Auth service behavior for password login, verification flows, OAuth first-login branching, and role assignment.
- Token service behavior for access tokens, refresh rotation, binding-token enforcement, revocation, and verification artifacts.
- Device service behavior for trusted-device recognition, pending verification issuance, and device-confirmed login.
- Captcha behavior for Google reCAPTCHA, Cloudflare Turnstile, bypass policy, malformed responses, and provider/network failures.

## Adding Tests

- Prefer placing new tests by boundary rather than by helper type.
- Reuse helpers from `TestSupport` before introducing per-file mock factories.
- Keep unit tests focused on one branch or invariant each; favor small setup helpers over broad integration-style fixtures.
