import { expect, test, type Page } from 'playwright/test';

/**
 * MFA recovery codes end to end: generating a set on the Security tab against a real backend, and
 * using a code on the sign-in verification page.
 */
test.describe('MFA recovery codes', () => {
  test.skip(
    process.env['PLAYWRIGHT_LIVE_AUTH'] !== 'true',
    'Requires a provisioned backend with seed data and the non-production seed step-up bypass.',
  );
  test.setTimeout(90_000);

  test('a set is generated, shown once, saved, and counted on the Security tab', async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await signIn(page, 'avaparticipant', 'Password123!');

    await page.getByRole('button', { name: /avaparticipant/i }).click();
    await page.getByRole('link', { name: 'Account settings', exact: true }).click();
    await page.waitForURL(/\/account\/profile(?:[/?#]|$)/);
    await page
      .getByRole('navigation', { name: 'Account settings' })
      .getByRole('link', { name: 'Security', exact: true })
      .click();
    await page.waitForURL(/\/account\/security(?:[/?#]|$)/);
    await expect(page.getByRole('heading', { name: 'Recovery codes' })).toBeVisible();

    // A seeded account may already hold a set from an earlier run; replacing it asks first.
    const replace = page.getByRole('button', { name: 'Generate new codes' });
    if (await replace.isVisible()) {
      await replace.click();
      const confirm = page.getByRole('alertdialog', { name: 'Replace your recovery codes?' });
      await expect(confirm).toContainText('will stop working');
      await confirm.getByRole('button', { name: 'Replace codes' }).click();
    } else {
      await page.getByRole('button', { name: 'Generate codes' }).click();
    }

    const heading = page.getByRole('heading', { name: 'Save your recovery codes' });
    await expect(heading).toBeFocused();
    await expect(page.getByText("These codes won't be shown again.")).toBeVisible();
    const codes = page
      .getByRole('list', { name: 'Save your recovery codes' })
      .getByRole('listitem');
    await expect(codes).toHaveCount(10);
    const shown = await codes.allTextContents();
    shown.forEach((code) =>
      expect(code.trim()).toMatch(/^([0-9A-HJKMNP-TV-Z]{4}-){6}[0-9A-HJKMNP-TV-Z]{4}$/),
    );

    await page.getByRole('button', { name: 'Copy all' }).click();
    await expect(page.getByText('Copied to your clipboard.')).toBeVisible();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
      shown.map((code) => code.trim()).join('\n'),
    );

    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download .txt' }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe('eventxperience-recovery-codes.txt');

    const done = page.getByRole('button', { name: 'Done' });
    await expect(done).toBeDisabled();
    await page.getByRole('checkbox', { name: "I've saved these codes somewhere safe." }).check();
    await done.click();

    await expect(codes).toHaveCount(0);
    await expect(page.getByText('10 of 10')).toBeVisible();

    await page.screenshot({ path: 'test-results/account-recovery-codes.png', fullPage: true });
  });

  test('the sign-in verification page accepts a recovery code and reports a refused one', async ({
    page,
  }) => {
    await page.addInitScript(() => {
      sessionStorage.setItem(
        'pending_login_step_up',
        JSON.stringify({
          Challenge: 'e2e-challenge',
          ExpiresAtUtc: new Date(Date.now() + 10 * 60_000).toISOString(),
          AvailableMethods: ['totp', 'recovery_code', 'email'],
          MaskedPhone: null,
          MaskedEmail: 'a***@example.com',
        }),
      );
    });
    await page.route('**/api/auth/mfa/start', (route) =>
      route.fulfill(
        envelope(200, {
          Challenge: 'e2e-challenge',
          ExpiresAtUtc: new Date(Date.now() + 10 * 60_000).toISOString(),
          SelectedMethod: 'recovery_code',
          MaskedDestination: 'recovery code',
          CooldownEndsAtUtc: '0001-01-01T00:00:00',
          AvailableMethods: ['totp', 'recovery_code', 'email'],
          MaskedPhone: null,
          MaskedEmail: 'a***@example.com',
        }),
      ),
    );
    let verifyBody: unknown;
    await page.route('**/api/auth/mfa/verify/recovery-code', (route) => {
      verifyBody = route.request().postDataJSON();
      return route.fulfill(
        envelope(401, null, 'Invalid or already used recovery code.', 'UNAUTHORIZED'),
      );
    });

    await page.goto('/auth/mfa');
    await page.getByRole('button', { name: /Use a recovery code/ }).click();

    const input = page.getByLabel('Recovery code', { exact: true });
    await expect(input).toBeVisible();
    await input.fill('123456');
    await page.getByRole('button', { name: 'Verify and sign in' }).click();
    await expect(
      page.getByText('Enter a recovery code in the format shown when it was generated.'),
    ).toBeVisible();
    expect(verifyBody).toBeUndefined();

    await input.fill('abcd-efgh-jkmn-pqrs-tvwx-yz01-2345');
    await page.getByRole('button', { name: 'Verify and sign in' }).click();
    await expect(page.getByText('Invalid or already used recovery code.')).toBeVisible();
    expect(verifyBody).toEqual({
      challenge: 'e2e-challenge',
      code: 'abcd-efgh-jkmn-pqrs-tvwx-yz01-2345',
    });

    await page.screenshot({ path: 'test-results/sign-in-recovery-code.png', fullPage: true });
  });
});

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

async function signIn(page: Page, username: string, password: string): Promise<void> {
  await page.addInitScript(() => {
    (window as Window & { grecaptcha?: unknown }).grecaptcha = {
      ready: (callback: () => void) => callback(),
      execute: async (_siteKey: string, options: { action: string }) =>
        `e2e-captcha-${options.action}`,
    };
  });
  await page.goto('/auth/login');
  await page.getByPlaceholder('Enter your username').fill(username);
  await page.getByPlaceholder('Enter your password').fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.waitForURL(/\/dashboard(?:[/?#]|$)/);
}
