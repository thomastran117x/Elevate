import { expect, test, type Page } from 'playwright/test';

/**
 * The account security history end to end: a real sign-in, recorded by the security-history
 * worker, read back on the Activity tab.
 */
test.describe('account security activity', () => {
  test.skip(
    process.env['PLAYWRIGHT_LIVE_AUTH'] !== 'true',
    'Requires a provisioned backend with seed data and a running security-history worker.',
  );
  test.setTimeout(90_000);

  test('a sign-in appears on the Activity tab and the tab works from the keyboard', async ({
    page,
  }) => {
    await signIn(page, 'avaparticipant', 'Password123!');

    await page.getByRole('button', { name: /avaparticipant/i }).click();
    await page.getByRole('link', { name: 'Account settings', exact: true }).click();
    await page.waitForURL(/\/account\/profile(?:[/?#]|$)/);

    const activityTab = page
      .getByRole('navigation', { name: 'Account settings' })
      .getByRole('link', { name: 'Activity', exact: true });
    await activityTab.focus();
    await page.keyboard.press('Enter');
    await page.waitForURL(/\/account\/activity(?:[/?#]|$)/);
    await expect(page.getByRole('heading', { name: 'Security activity' })).toBeVisible();

    // The worker records asynchronously, so revisit the tab until the entry arrives. Revisit it
    // through the app's own navigation: a full page load of /account/* while signed in currently
    // lands on the login page.
    const nav = page.getByRole('navigation', { name: 'Account settings' });
    const list = page.getByRole('list', { name: 'Security activity' });
    await expect(async () => {
      await nav.getByRole('link', { name: 'Profile', exact: true }).click();
      await page.waitForURL(/\/account\/profile(?:[/?#]|$)/);
      await nav.getByRole('link', { name: 'Activity', exact: true }).click();
      await page.waitForURL(/\/account\/activity(?:[/?#]|$)/);
      await expect(list.getByText('Signed in', { exact: true }).first()).toBeVisible({
        timeout: 3_000,
      });
    }).toPass({ timeout: 60_000 });

    const latest = list.getByRole('listitem').first();
    await expect(latest).toContainText('Support reference:');
    await expect(latest.locator('time')).toHaveAttribute('datetime', /\d{4}-\d{2}-\d{2}T/);
    await expect(page.getByText(/Page 1 of \d+/)).toBeVisible();

    await page.screenshot({ path: 'test-results/account-activity.png', fullPage: true });
  });
});

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
