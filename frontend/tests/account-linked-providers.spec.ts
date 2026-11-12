import { expect, test, type Page, type Route } from 'playwright/test';

/**
 * Explicit provider linking (#329) in the browser, against a mocked API: a link that returns
 * from the provider, a cancelled consent, a conflicting identity, an expired request, unlinking
 * with the last-method guard, and the email-match proposal a provider sign-in makes.
 *
 * The provider itself is never contacted. Each test seeds the state a started link leaves in
 * session storage and opens the shared callback page as the provider would redirect to it.
 */

type Json = Record<string, unknown>;

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
    enrollmentAvailable: false,
    isConfigured: false,
    isEnabled: false,
    canEnroll: false,
    canEnable: false,
    canDisable: false,
    canRemove: false,
  },
};

const GOOGLE_LINK = {
  provider: 'google',
  email: 'owner@gmail.com',
  displayName: 'Owner',
  linkedAtUtc: '2026-11-07T12:00:00Z',
  lastUsedAtUtc: null,
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

function overview(google: Json | null, canUnlinkGoogle: boolean, hasPassword = true) {
  return {
    hasPassword,
    providers: [
      { provider: 'google', available: true, link: google, canUnlink: canUnlinkGoogle },
      { provider: 'microsoft', available: true, link: null, canUnlink: false },
    ],
  };
}

/**
 * A signed-in session whose step-up proof is fresh, so the Security tab opens. Routes registered
 * later take precedence, so each test overrides only what it is about.
 */
async function mockSignedInApi(page: Page, providers: () => unknown): Promise<void> {
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
  await page.route('**/api/auth/mfa/step-up/status', (route) => route.fulfill(envelope(200, null)));
  await page.route('**/api/auth/mfa', (route) => route.fulfill(envelope(200, MFA_SETTINGS)));
  await page.route('**/api/auth/mfa/recovery-codes/status', (route) =>
    route.fulfill(envelope(200, { remainingCount: 0, totalCount: 10, generatedAtUtc: null })),
  );
  await page.route('**/api/auth/providers', (route) =>
    route.request().method() === 'GET'
      ? route.fulfill(envelope(200, providers()))
      : route.fallback(),
  );
}

async function seedStartedLink(page: Page, provider: 'google' | 'microsoft', state: string) {
  await page.addInitScript(
    ({ provider, state }) => {
      if (!sessionStorage.getItem('e2e_link_seeded')) {
        sessionStorage.setItem('e2e_link_seeded', '1');
        sessionStorage.setItem(
          'oauth_link_pending',
          JSON.stringify({ provider, state, codeVerifier: null, startedAt: Date.now() }),
        );
      }
    },
    { provider, state },
  );
}

function banner(page: Page) {
  return page.getByTestId('linked-providers-banner');
}

test.describe('linked sign-in providers', () => {
  test('a Google link returns from the provider and is shown as linked', async ({ page }) => {
    let linked = false;
    let completeBody: unknown;
    await mockSignedInApi(page, () => overview(linked ? GOOGLE_LINK : null, linked));
    await page.route('**/api/auth/providers/google/link/complete', (route: Route) => {
      completeBody = route.request().postDataJSON();
      linked = true;
      return route.fulfill(envelope(200, GOOGLE_LINK));
    });
    await seedStartedLink(page, 'google', 'state-ok');

    await page.goto('/auth/google?code=auth-code&state=state-ok');

    await page.waitForURL(/\/account\/security(?:[?#]|$)/);
    await expect(banner(page)).toHaveText('Google is now linked. You can use it to sign in.');
    await expect(banner(page)).toBeFocused();
    expect(completeBody).toEqual({ state: 'state-ok', code: 'auth-code' });
    // The result is not left in the address bar to be replayed by a reload.
    await expect(page).not.toHaveURL(/providerLink=/);

    const google = page.getByTestId('provider-google');
    await expect(google).toContainText('Linked as owner@gmail.com');
    await expect(google.getByRole('button', { name: 'Unlink Google' })).toBeEnabled();
    await page.screenshot({ path: 'test-results/account-linked-providers.png', fullPage: true });
  });

  test('a declined consent changes nothing', async ({ page }) => {
    let completeCalled = false;
    await mockSignedInApi(page, () => overview(null, false));
    await page.route('**/api/auth/providers/google/link/complete', (route) => {
      completeCalled = true;
      return route.fulfill(envelope(200, GOOGLE_LINK));
    });
    await seedStartedLink(page, 'google', 'state-cancel');

    await page.goto('/auth/google?error=access_denied&state=state-cancel');

    await page.waitForURL(/\/account\/security(?:[?#]|$)/);
    await expect(banner(page)).toHaveText('Linking Google was cancelled. Nothing was changed.');
    expect(completeCalled).toBe(false);
  });

  test('an identity linked to another account is refused', async ({ page }) => {
    await mockSignedInApi(page, () => overview(null, false));
    await page.route('**/api/auth/providers/google/link/complete', (route) =>
      route.fulfill(envelope(409, null, 'Already linked.', 'PROVIDER_ALREADY_LINKED')),
    );
    await seedStartedLink(page, 'google', 'state-conflict');

    await page.goto('/auth/google?code=auth-code&state=state-conflict');

    await page.waitForURL(/\/account\/security(?:[?#]|$)/);
    await expect(banner(page)).toHaveText(
      'That Google account is already linked to a different account.',
    );
    await expect(banner(page)).toHaveAttribute('role', 'alert');
  });

  test('an expired link request asks to start again', async ({ page }) => {
    await mockSignedInApi(page, () => overview(null, false));
    await page.route('**/api/auth/providers/google/link/complete', (route) =>
      route.fulfill(envelope(410, null, 'Expired.', 'PROVIDER_LINK_EXPIRED')),
    );
    await seedStartedLink(page, 'google', 'state-expired');

    await page.goto('/auth/google?code=auth-code&state=state-expired');

    await page.waitForURL(/\/account\/security(?:[?#]|$)/);
    await expect(banner(page)).toHaveText('That Google link request expired. Start again.');
  });

  test('unlinking asks first, warns, and signs out everywhere', async ({ page }) => {
    let state = { google: GOOGLE_LINK as Json | null, canUnlink: true, hasPassword: true };
    let deleted = false;
    await mockSignedInApi(page, () => overview(state.google, state.canUnlink, state.hasPassword));
    await page.route('**/api/auth/providers/google', (route) => {
      if (route.request().method() !== 'DELETE') {
        return route.fallback();
      }
      deleted = true;
      state = { google: null, canUnlink: false, hasPassword: true };
      return route.fulfill(envelope(200, null, 'Provider unlinked.'));
    });
    await seedStartedLink(page, 'google', 'state-unlink');
    await page.route('**/api/auth/providers/google/link/complete', (route) =>
      route.fulfill(envelope(200, GOOGLE_LINK)),
    );
    await page.goto('/auth/google?code=auth-code&state=state-unlink');
    await page.waitForURL(/\/account\/security(?:[?#]|$)/);

    const google = page.getByTestId('provider-google');
    await google.getByRole('button', { name: 'Unlink Google' }).click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText('Stop signing in with Google?');
    await expect(dialog).toHaveAccessibleDescription(
      "You'll be signed out on every device, including this one.",
    );
    await dialog.getByRole('button', { name: 'Keep it' }).click();
    expect(deleted).toBe(false);

    await google.getByRole('button', { name: 'Unlink Google' }).click();
    await page
      .getByRole('alertdialog')
      .getByRole('button', { name: 'Unlink', exact: true })
      .click();

    // The server ended every session, this one included, so the app signs out locally.
    await page.waitForURL(/\/auth\/login(?:[?#]|$)/);
    expect(deleted).toBe(true);
  });

  test('the only sign-in method shows why it cannot be unlinked', async ({ page }) => {
    await mockSignedInApi(page, () => overview(GOOGLE_LINK, false, false));
    await seedStartedLink(page, 'google', 'state-last');
    await page.route('**/api/auth/providers/google/link/complete', (route) =>
      route.fulfill(envelope(200, GOOGLE_LINK)),
    );

    await page.goto('/auth/google?code=auth-code&state=state-last');
    await page.waitForURL(/\/account\/security(?:[?#]|$)/);

    const unlink = page.getByRole('button', { name: 'Unlink Google' });
    await expect(unlink).toBeDisabled();
    await expect(unlink).toHaveAccessibleDescription("It's your only way to sign in.");
    await expect(
      page.getByText('Not set. You sign in with a linked account or a passkey.'),
    ).toBeVisible();
  });

  test('a sign-in whose email matches an account proposes a link instead of signing in', async ({
    page,
  }) => {
    await page.route('**/api/**', (route) => route.fulfill(envelope(404, null, 'Not mocked')));
    await page.route('**/api/auth/csrf', (route) =>
      route.fulfill(envelope(200, { token: 'csrf' })),
    );
    await page.route('**/api/auth/refresh', (route) =>
      route.fulfill(envelope(401, null, 'No session.', 'UNAUTHORIZED')),
    );
    await page.route('**/api/auth/google/code', (route) =>
      route.fulfill(
        envelope(200, {
          Type: 'requires_link_confirmation',
          LinkConfirmation: {
            linkToken: 'proposal-token',
            provider: 'google',
            maskedEmail: 'o***@example.com',
            expiresAtUtc: new Date(Date.now() + 10 * 60_000).toISOString(),
          },
        }),
      ),
    );
    await page.addInitScript(() => {
      if (!sessionStorage.getItem('e2e_signin_seeded')) {
        sessionStorage.setItem('e2e_signin_seeded', '1');
        sessionStorage.setItem('google_oauth_state', 'signin-state');
        sessionStorage.setItem('google_code_verifier', 'verifier');
        sessionStorage.setItem('google_oauth_nonce', 'nonce');
      }
    });

    await page.goto('/auth/google?code=auth-code&state=signin-state');

    await page.waitForURL(/\/auth\/login(?:[?#]|$)/);
    const proposal = page.getByTestId('provider-link-proposal');
    await expect(proposal).toContainText('An account with o***@example.com already exists.');
    await expect(proposal).toContainText('confirm linking Google');

    await proposal.getByRole('button', { name: "Don't link Google" }).click();
    await expect(proposal).toBeHidden();
    expect(await page.evaluate(() => sessionStorage.getItem('pending_provider_link'))).toBeNull();
  });
});
