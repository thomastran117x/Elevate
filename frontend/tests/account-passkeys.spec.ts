import { generateKeyPairSync, randomBytes } from 'node:crypto';

import { expect, test, type CDPSession, type Page, type Route } from 'playwright/test';

/**
 * Passkeys (#328) in the browser, against a mocked API: adding one, signing in with one,
 * stepping up with one, and removing one.
 *
 * The ceremonies are real. Chromium's virtual authenticator (over the DevTools protocol) answers
 * `navigator.credentials`, so these tests cover the option parsing, the browser prompt, and the
 * credential JSON the page sends back. The API is mocked, so the server's verification is
 * covered by the backend integration tests instead.
 */

const ORIGIN = 'http://localhost:3101';
const RP_ID = 'localhost';

const USER = {
  id: 42,
  email: 'owner@example.com',
  username: 'owner',
  usernameDisplay: 'owner',
  name: 'Owner',
  avatar: null,
  usertype: 'participant',
};

const MFA_SETTINGS = {
  email: { maskedEmail: 'o***@example.com', isEnabled: true },
  sms: {
    enrollmentAvailable: false,
    isConfigured: false,
    isEnabled: false,
    canEnroll: false,
    canEnable: false,
    canDisable: false,
    canRemove: false,
  },
  totp: {
    enrollmentAvailable: true,
    isConfigured: false,
    isEnabled: false,
    canEnroll: true,
    canEnable: false,
    canDisable: false,
    canRemove: false,
  },
};

const PASSKEY = {
  id: 7,
  name: 'Work laptop',
  syncable: false,
  backedUp: false,
  canRemove: true,
  createdAt: '2026-11-10T09:00:00Z',
  lastUsedAt: null,
};

function envelope(status: number, data: unknown, message = 'ok', code?: string) {
  return {
    status,
    contentType: 'application/json',
    body: JSON.stringify({
      success: status < 400,
      message,
      data,
      error: code ? { code, details: null } : null,
      meta: null,
    }),
  };
}

const base64Url = (bytes: Buffer): string => bytes.toString('base64url');

function challenge(): string {
  return base64Url(randomBytes(16));
}

function clientData(credential: Record<string, unknown>): { type: string; origin: string } {
  const response = credential['response'] as Record<string, string>;
  return JSON.parse(Buffer.from(response['clientDataJSON'], 'base64url').toString('utf8'));
}

/** A virtual platform authenticator that verifies the user, as Touch ID or Windows Hello would. */
async function addAuthenticator(page: Page): Promise<{ cdp: CDPSession; authenticatorId: string }> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
  return { cdp, authenticatorId };
}

/** Puts a passkey for this site on the virtual authenticator, as if registered earlier. */
async function seedPasskey(
  cdp: CDPSession,
  authenticatorId: string,
): Promise<{ credentialId: Buffer; userHandle: Buffer }> {
  const credentialId = randomBytes(16);
  const userHandle = randomBytes(32);
  const { privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  await cdp.send('WebAuthn.addCredential', {
    authenticatorId,
    credential: {
      credentialId: credentialId.toString('base64'),
      isResidentCredential: true,
      rpId: RP_ID,
      privateKey: privateKey.export({ format: 'der', type: 'pkcs8' }).toString('base64'),
      userHandle: userHandle.toString('base64'),
      signCount: 0,
    },
  });
  return { credentialId, userHandle };
}

interface AccountApi {
  passkeys: unknown[];
  verified: boolean;
  methods: string[];
}

async function mockSignedInApi(page: Page, state: AccountApi): Promise<void> {
  await page.route('**/api/**', (route) => route.fulfill(envelope(404, null, 'Not mocked')));
  await page.route('**/api/auth/csrf', (route) => route.fulfill(envelope(200, { token: 'csrf' })));
  await page.route('**/api/auth/refresh', (route) =>
    route.fulfill(
      envelope(200, {
        accessToken: 'e2e-access-token',
        expiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
      }),
    ),
  );
  await page.route('**/api/auth/me', (route) => route.fulfill(envelope(200, USER)));
  await page.route('**/api/auth/mfa/step-up/status', (route) =>
    route.fulfill(
      state.verified
        ? envelope(200, null)
        : envelope(403, null, 'Verify your identity first.', 'MFA_REQUIRED'),
    ),
  );
  await page.route('**/api/auth/mfa/step-up/options', (route) =>
    route.fulfill(
      envelope(200, { availableMethods: state.methods, maskedPhone: null, maskedEmail: null }),
    ),
  );
  await page.route('**/api/auth/mfa', (route) => route.fulfill(envelope(200, MFA_SETTINGS)));
  await page.route('**/api/auth/mfa/recovery-codes/status', (route) =>
    route.fulfill(envelope(200, { remainingCount: 0, totalCount: 10, generatedAtUtc: null })),
  );
  await page.route('**/api/auth/providers', (route) =>
    route.fulfill(
      envelope(200, {
        hasPassword: true,
        providers: [
          { provider: 'google', available: true, link: null, canUnlink: false },
          { provider: 'microsoft', available: true, link: null, canUnlink: false },
        ],
      }),
    ),
  );
  await page.route('**/api/auth/passkeys', (route) =>
    route.request().method() === 'GET'
      ? route.fulfill(envelope(200, { passkeys: state.passkeys, limit: 10 }))
      : route.fallback(),
  );
}

/** Lands on the security tab the way a provider link returns to it. */
async function openSecurityTab(page: Page): Promise<void> {
  await page.addInitScript(() => {
    if (!sessionStorage.getItem('e2e_link_seeded')) {
      sessionStorage.setItem('e2e_link_seeded', '1');
      sessionStorage.setItem(
        'oauth_link_pending',
        JSON.stringify({
          provider: 'google',
          state: 'state-passkeys',
          codeVerifier: null,
          startedAt: Date.now(),
        }),
      );
    }
  });
  await page.route('**/api/auth/providers/google/link/complete', (route) =>
    route.fulfill(
      envelope(200, {
        provider: 'google',
        email: 'owner@gmail.com',
        displayName: 'Owner',
        linkedAtUtc: '2026-11-07T12:00:00Z',
        lastUsedAtUtc: null,
      }),
    ),
  );

  await page.goto('/auth/google?code=auth-code&state=state-passkeys');
  await page.waitForURL(/\/account\/security(?:[?#]|$)/);
}

interface CapturedSignIn {
  assertion?: Record<string, unknown>;
  rememberMe?: unknown;
}

/** An anonymous visitor's API, with passkey sign-in recording what the page sent. */
async function mockPasskeySignIn(page: Page): Promise<CapturedSignIn> {
  const captured: CapturedSignIn = {};
  await page.route('**/api/**', (route) => route.fulfill(envelope(404, null, 'Not mocked')));
  await page.route('**/api/auth/csrf', (route) => route.fulfill(envelope(200, { token: 'csrf' })));
  await page.route('**/api/auth/me', (route) => route.fulfill(envelope(200, USER)));
  await page.route('**/api/auth/passkeys/sign-in/options', (route) =>
    route.fulfill(
      envelope(200, {
        ceremonyId: `sign-in-${Math.random()}`,
        expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
        publicKey: {
          challenge: challenge(),
          rpId: RP_ID,
          allowCredentials: [],
          userVerification: 'required',
          timeout: 60_000,
        },
      }),
    ),
  );
  await page.route('**/api/auth/passkeys/sign-in', (route: Route) => {
    const body = route.request().postDataJSON();
    captured.assertion = body['credential'];
    captured.rememberMe = body['rememberMe'];
    return route.fulfill(
      envelope(200, {
        type: 'authenticated',
        auth: {
          accessToken: 'e2e-access-token',
          expiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
        },
      }),
    );
  });
  return captured;
}

function expectAssertionFor(
  assertion: Record<string, unknown> | undefined,
  passkey: { credentialId: Buffer; userHandle: Buffer },
): void {
  expect(assertion?.['rawId']).toBe(base64Url(passkey.credentialId));
  const response = assertion?.['response'] as Record<string, string>;
  expect(response['userHandle']).toBe(base64Url(passkey.userHandle));
  expect(response['signature']).toEqual(expect.any(String));
  expect(clientData(assertion!)).toEqual(
    expect.objectContaining({ type: 'webauthn.get', origin: ORIGIN }),
  );
}

test.describe('passkeys', () => {
  test('adds a passkey with the device authenticator', async ({ page }) => {
    const state: AccountApi = { passkeys: [], verified: true, methods: ['email'] };
    await mockSignedInApi(page, state);
    await addAuthenticator(page);
    let registration: Record<string, unknown> | undefined;
    let requestedName: unknown;
    await page.route('**/api/auth/passkeys/registration/options', (route: Route) => {
      requestedName = route.request().postDataJSON()['name'];
      return route.fulfill(
        envelope(200, {
          ceremonyId: 'register-ceremony',
          expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
          publicKey: {
            rp: { id: RP_ID, name: 'EventXperience' },
            user: { id: base64Url(randomBytes(32)), name: 'owner', displayName: 'Owner' },
            challenge: challenge(),
            pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
            timeout: 60_000,
            attestation: 'none',
            authenticatorSelection: { residentKey: 'required', userVerification: 'required' },
            excludeCredentials: [],
          },
        }),
      );
    });
    await page.route('**/api/auth/passkeys/registration', (route: Route) => {
      const body = route.request().postDataJSON();
      registration = body['credential'];
      expect(body['ceremonyId']).toBe('register-ceremony');
      state.passkeys = [PASSKEY];
      return route.fulfill(envelope(200, PASSKEY));
    });
    await openSecurityTab(page);

    const card = page.getByTestId('passkeys-card');
    await expect(card).toContainText('No passkeys yet.');
    await card.getByLabel('Name (optional)').fill('Work laptop');
    await card.getByRole('button', { name: 'Add a passkey' }).click();

    await expect(page.getByTestId('passkeys-banner')).toHaveText(
      'Work laptop was added. You can use it to sign in.',
    );
    await expect(card.getByRole('list', { name: 'Passkeys' })).toContainText('Work laptop');
    expect(requestedName).toBe('Work laptop');
    expect(registration?.['type']).toBe('public-key');
    expect((registration?.['response'] as Record<string, unknown>)['attestationObject']).toEqual(
      expect.any(String),
    );
    expect(clientData(registration!)).toEqual(
      expect.objectContaining({ type: 'webauthn.create', origin: ORIGIN }),
    );
  });

  test('signs in with the passkey button, without a username or password', async ({ page }) => {
    const { cdp, authenticatorId } = await addAuthenticator(page);
    const passkey = await seedPasskey(cdp, authenticatorId);
    const signIn = await mockPasskeySignIn(page);
    // Without autofill passkeys, the page waits for the button.
    await page.addInitScript(() => {
      Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', {
        value: () => Promise.resolve(false),
      });
    });

    await page.goto('/auth/login');
    const button = page.getByRole('button', { name: 'Sign in with a passkey' });
    await expect(button).toBeVisible();
    await page.getByLabel('Remember me').check();
    await button.click();

    await page.waitForURL((url) => !url.pathname.startsWith('/auth/login'));
    expect(signIn.rememberMe).toBe(true);
    expectAssertionFor(signIn.assertion, passkey);
  });

  test('signs in from the username field autofill', async ({ page }) => {
    // Chromium's virtual authenticator answers an autofill (conditional) request at once, as a
    // user picking the passkey from the username field's suggestions would.
    const { cdp, authenticatorId } = await addAuthenticator(page);
    const passkey = await seedPasskey(cdp, authenticatorId);
    const signIn = await mockPasskeySignIn(page);

    await page.goto('/auth/login');

    await page.waitForURL((url) => !url.pathname.startsWith('/auth/login'));
    expect(signIn.rememberMe).toBe(false);
    expectAssertionFor(signIn.assertion, passkey);
  });

  test('a dismissed passkey prompt leaves the password form in place', async ({ page }) => {
    // No authenticator, so the browser finds no passkey and the request fails as dismissed.
    await page.route('**/api/**', (route) => route.fulfill(envelope(404, null, 'Not mocked')));
    await page.route('**/api/auth/csrf', (route) =>
      route.fulfill(envelope(200, { token: 'csrf' })),
    );
    await page.route('**/api/auth/passkeys/sign-in/options', (route) =>
      route.fulfill(
        envelope(200, {
          ceremonyId: 'sign-in',
          expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
          publicKey: { challenge: challenge(), rpId: RP_ID, timeout: 1_000 },
        }),
      ),
    );
    const { cdp, authenticatorId } = await addAuthenticator(page);
    await cdp.send('WebAuthn.setUserVerified', { authenticatorId, isUserVerified: false });

    await page.goto('/auth/login');
    await page.getByRole('button', { name: 'Sign in with a passkey' }).click();

    await expect(page.getByText(/Passkey sign-in (was cancelled|failed)/)).toBeVisible();
    await expect(page.getByPlaceholder('Enter your password')).toBeVisible();
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('verifies identity with a passkey, then removes a passkey', async ({ page }) => {
    const { cdp, authenticatorId } = await addAuthenticator(page);
    const { credentialId } = await seedPasskey(cdp, authenticatorId);
    const second = { ...PASSKEY, id: 8, name: 'Phone', syncable: true };
    const state: AccountApi = {
      passkeys: [PASSKEY, second],
      verified: false,
      methods: ['passkey', 'email'],
    };
    await mockSignedInApi(page, state);
    let stepUp: Record<string, unknown> | undefined;
    await page.route('**/api/auth/passkeys/step-up/options', (route) =>
      route.fulfill(
        envelope(200, {
          ceremonyId: 'step-up-ceremony',
          expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
          publicKey: {
            challenge: challenge(),
            rpId: RP_ID,
            allowCredentials: [{ type: 'public-key', id: base64Url(credentialId) }],
            userVerification: 'required',
            timeout: 60_000,
          },
        }),
      ),
    );
    await page.route('**/api/auth/passkeys/step-up', (route: Route) => {
      stepUp = route.request().postDataJSON()['credential'];
      state.verified = true;
      return route.fulfill(envelope(200, null));
    });
    let removed = 0;
    await page.route(`**/api/auth/passkeys/${second.id}`, (route: Route) => {
      if (route.request().method() !== 'DELETE') {
        return route.fallback();
      }
      removed++;
      state.passkeys = [PASSKEY];
      return route.fulfill(envelope(200, null));
    });
    await openSecurityTab(page);

    // The tab is locked; a passkey is offered first and needs no code.
    await page.getByRole('button', { name: 'Verify identity' }).click();
    const verify = page.getByRole('dialog', { name: 'Verify your identity' });
    await expect(verify.getByRole('button', { name: /Passkey/ })).toBeVisible();
    await verify.getByRole('button', { name: 'Use passkey' }).click();
    await expect(verify).toBeHidden();
    expect(stepUp?.['rawId']).toBe(base64Url(credentialId));
    expect(clientData(stepUp!).type).toBe('webauthn.get');

    const card = page.getByTestId('passkeys-card');
    await expect(card.getByRole('list', { name: 'Passkeys' }).getByRole('listitem')).toHaveCount(2);
    await card.getByRole('button', { name: 'Remove Phone' }).click();
    const confirm = page.getByRole('dialog', { name: 'Remove Phone?' });
    await confirm.getByRole('button', { name: 'Remove passkey' }).click();

    await expect(page.getByTestId('passkeys-banner')).toHaveText(
      'Phone was removed. It can no longer sign in.',
    );
    expect(removed).toBe(1);
    await expect(card.getByRole('list', { name: 'Passkeys' }).getByRole('listitem')).toHaveCount(1);
  });
});
