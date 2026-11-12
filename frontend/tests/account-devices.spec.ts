import { expect, test, type Page, type Route } from 'playwright/test';

/**
 * Session and trusted-device management (#325) in the browser, against a mocked API: the lists
 * with this device marked, a sign-out refused for a missing step-up that verifies and then
 * finishes, signing out everywhere, and forgetting a device.
 *
 * A full page load of /account/* lands on sign-in during server rendering, so each test enters
 * the account pages the way a provider link returns to them, then opens the Devices tab.
 */

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
    isConfigured: true,
    isEnabled: true,
    canEnroll: false,
    canEnable: false,
    canDisable: true,
    canRemove: true,
  },
};

const THIS_SESSION = {
  id: 'a'.repeat(32),
  current: true,
  transport: 'browser',
  client: 'chrome',
  deviceClass: 'desktop',
  network: '203.0.113.0/24',
  rememberMe: false,
  createdAt: '2026-11-10T09:00:00Z',
  lastSeenAt: '2026-11-11T09:00:00Z',
  expiresAt: '2026-11-12T09:00:00Z',
};

const PHONE_SESSION = {
  ...THIS_SESSION,
  id: 'b'.repeat(32),
  current: false,
  client: 'safari',
  deviceClass: 'mobile',
  network: '198.51.100.0/24',
};

const THIS_DEVICE = {
  id: 1,
  current: true,
  client: 'chrome',
  deviceClass: 'desktop',
  network: '203.0.113.0/24',
  createdAt: '2026-11-01T09:00:00Z',
  lastSeenAt: '2026-11-11T09:00:00Z',
  expiresAt: '2027-01-30T09:00:00Z',
};

const TABLET_DEVICE = {
  ...THIS_DEVICE,
  id: 2,
  current: false,
  client: 'firefox',
  deviceClass: 'tablet',
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

interface DevicesApi {
  sessions: unknown[];
  devices: unknown[];
  /** Whether the session holds a fresh step-up proof. */
  verified: boolean;
}

/**
 * A signed-in session with the lists served from `state`. Routes registered later take
 * precedence, so each test overrides only the requests it is about.
 */
async function mockSignedInApi(page: Page, state: DevicesApi): Promise<void> {
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
      envelope(200, { availableMethods: ['totp'], maskedPhone: null, maskedEmail: null }),
    ),
  );
  await page.route('**/api/auth/mfa/step-up/verify', (route) => {
    state.verified = true;
    return route.fulfill(envelope(200, null));
  });
  await page.route('**/api/auth/mfa', (route) => route.fulfill(envelope(200, MFA_SETTINGS)));
  await page.route('**/api/auth/mfa/recovery-codes/status', (route) =>
    route.fulfill(envelope(200, { remainingCount: 10, totalCount: 10, generatedAtUtc: null })),
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
  await page.route('**/api/auth/sessions', (route) =>
    route.request().method() === 'GET'
      ? route.fulfill(envelope(200, { sessions: state.sessions }))
      : route.fallback(),
  );
  await page.route('**/api/auth/devices', (route) =>
    route.request().method() === 'GET'
      ? route.fulfill(envelope(200, { devices: state.devices }))
      : route.fallback(),
  );
}

/** Lands on the account pages the way a provider link returns to them, then opens Devices. */
async function openDevicesTab(page: Page): Promise<void> {
  await page.addInitScript(() => {
    if (!sessionStorage.getItem('e2e_link_seeded')) {
      sessionStorage.setItem('e2e_link_seeded', '1');
      sessionStorage.setItem(
        'oauth_link_pending',
        JSON.stringify({
          provider: 'google',
          state: 'state-devices',
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

  await page.goto('/auth/google?code=auth-code&state=state-devices');
  await page.waitForURL(/\/account\/security(?:[?#]|$)/);
  // A click that lands before the server-rendered page hydrates does nothing, so retry it.
  const devicesLink = page
    .getByRole('navigation', { name: 'Account settings' })
    .getByRole('link', { name: 'Devices' });
  await expect(async () => {
    await devicesLink.click();
    await expect(page).toHaveURL(/\/account\/devices(?:[?#]|$)/, { timeout: 1_000 });
  }).toPass();
}

test.describe('sessions and trusted devices', () => {
  test('lists both, marking this device, without asking to verify first', async ({ page }) => {
    const state: DevicesApi = {
      sessions: [THIS_SESSION, PHONE_SESSION],
      devices: [THIS_DEVICE, TABLET_DEVICE],
      verified: false,
    };
    await mockSignedInApi(page, state);
    await openDevicesTab(page);

    const sessions = page.getByRole('list', { name: 'Signed-in sessions' });
    await expect(sessions.getByRole('listitem')).toHaveCount(2);
    await expect(sessions.getByRole('listitem').first()).toContainText('Chrome · Desktop');
    await expect(sessions.getByRole('listitem').first()).toContainText('This device');
    await expect(sessions.getByRole('listitem').nth(1)).toContainText('Safari · Phone');
    const devices = page.getByRole('list', { name: 'Trusted devices' });
    await expect(devices.getByRole('listitem')).toHaveCount(2);
    await expect(devices).toContainText('Trusted until');
    await expect(page.getByText('Verification required')).toHaveCount(0);
  });

  test('signing out another session verifies identity, then finishes', async ({ page }) => {
    const state: DevicesApi = {
      sessions: [THIS_SESSION, PHONE_SESSION],
      devices: [],
      verified: false,
    };
    let revoked = 0;
    await mockSignedInApi(page, state);
    await page.route(`**/api/auth/sessions/${PHONE_SESSION.id}`, (route: Route) => {
      if (!state.verified) {
        return route.fulfill(envelope(403, null, 'Verify your identity first.', 'MFA_REQUIRED'));
      }
      revoked++;
      state.sessions = [THIS_SESSION];
      return route.fulfill(envelope(200, { revokedCount: 1, currentSessionRevoked: false }));
    });
    await openDevicesTab(page);

    await page.getByRole('button', { name: 'Sign out Safari · Phone session' }).click();
    const confirm = page.getByRole('dialog', { name: 'Sign out Safari · Phone?' });
    await expect(confirm).toContainText('This device stays signed in.');
    await confirm.getByRole('button', { name: 'Sign out', exact: true }).click();

    const verify = page.getByRole('dialog', { name: 'Verify your identity' });
    await expect(verify).toBeVisible();
    await verify.getByRole('button', { name: 'Continue' }).click();
    await verify.getByPlaceholder('123456').fill('123456');
    await verify.getByRole('button', { name: 'Verify and continue' }).click();

    await expect(page.getByTestId('sessions-banner')).toHaveText('Safari · Phone was signed out.');
    expect(revoked).toBe(1);
    await expect(
      page.getByRole('list', { name: 'Signed-in sessions' }).getByRole('listitem'),
    ).toHaveCount(1);
  });

  test('signing out everywhere returns to the sign-in page', async ({ page }) => {
    const state: DevicesApi = {
      sessions: [THIS_SESSION, PHONE_SESSION],
      devices: [],
      verified: true,
    };
    let revokedAll = false;
    await mockSignedInApi(page, state);
    await page.route('**/api/auth/sessions', (route) => {
      if (route.request().method() !== 'DELETE') {
        return route.fallback();
      }
      revokedAll = true;
      return route.fulfill(envelope(200, { revokedCount: 2, currentSessionRevoked: true }));
    });
    await openDevicesTab(page);

    await page.getByRole('button', { name: 'Sign out everywhere' }).click();
    const confirm = page.getByRole('dialog', { name: 'Sign out everywhere?' });
    await expect(confirm).toContainText('including this one');
    await confirm.getByRole('button', { name: 'Sign out everywhere' }).click();

    await page.waitForURL(/\/auth\/login(?:[?#]|$)/);
    expect(revokedAll).toBe(true);
  });

  test('forgetting a device keeps the session and says so', async ({ page }) => {
    const state: DevicesApi = {
      sessions: [THIS_SESSION],
      devices: [THIS_DEVICE, TABLET_DEVICE],
      verified: true,
    };
    await mockSignedInApi(page, state);
    await page.route(`**/api/auth/devices/${TABLET_DEVICE.id}`, (route) => {
      state.devices = [THIS_DEVICE];
      return route.fulfill(envelope(200, { revokedCount: 1, currentDeviceRevoked: false }));
    });
    await openDevicesTab(page);

    await page.getByRole('button', { name: 'Forget Firefox · Tablet' }).click();
    const confirm = page.getByRole('dialog', { name: 'Forget Firefox · Tablet?' });
    await expect(confirm).toContainText('No one is signed out.');
    await confirm.getByRole('button', { name: 'Keep trusting' }).click();
    await expect(confirm).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Forget Firefox · Tablet' })).toBeFocused();

    await page.getByRole('button', { name: 'Forget Firefox · Tablet' }).click();
    await page
      .getByRole('dialog', { name: 'Forget Firefox · Tablet?' })
      .getByRole('button', { name: 'Forget device' })
      .click();

    await expect(page.getByTestId('trusted-devices-banner')).toHaveText(
      'Firefox · Tablet was forgotten.',
    );
    await expect(
      page.getByRole('list', { name: 'Trusted devices' }).getByRole('listitem'),
    ).toHaveCount(1);
  });
});
