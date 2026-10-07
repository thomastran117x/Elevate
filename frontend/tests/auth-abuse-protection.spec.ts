import { expect, test, type Page, type Request } from 'playwright/test';

type GrecaptchaWindow = Window & {
  grecaptcha?: {
    ready(callback: () => void): void;
    execute(siteKey: string, options: { action: string }): Promise<string>;
  };
  captchaActions?: string[];
};

test.describe.serial('authentication abuse protection', () => {
  test.setTimeout(60_000);

  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      const target = window as GrecaptchaWindow;
      target.captchaActions = [];
      target.grecaptcha = {
        ready: (callback) => callback(),
        execute: async (_siteKey, options) => {
          target.captchaActions?.push(options.action);
          return `e2e-captcha-${options.action}`;
        },
      };
    });
  });

  test('login escalates CAPTCHA on the next manual submission and keeps failures generic', async ({
    page,
  }) => {
    const loginRequests: Request[] = [];
    page.on('request', (request) => {
      if (request.method() === 'POST' && request.url().endsWith('/api/auth/login')) {
        loginRequests.push(request);
      }
    });

    await page.goto('/auth/login');
    await page.getByPlaceholder('Enter your username').fill(`e2e-missing-${Date.now()}`);
    await page.getByPlaceholder('Enter your password').fill('WrongPassword123!');

    for (let attempt = 0; attempt < 4; attempt += 1) {
      const responsePromise = page.waitForResponse(
        (response) =>
          response.request().method() === 'POST' && response.url().endsWith('/api/auth/login'),
      );
      await page.getByRole('button', { name: 'Sign in', exact: true }).click();
      const response = await responsePromise;
      expect(response.status()).toBe(401);
      await expect(page.getByText('Authentication failed. Please try again.')).toBeVisible();
    }

    expect(loginRequests).toHaveLength(4);
    for (const request of loginRequests.slice(0, 3)) {
      expect(request.postDataJSON()).not.toHaveProperty('captcha');
    }
    expect(loginRequests[3].postDataJSON()).toMatchObject({ captcha: 'e2e-captcha-login' });
    expect(await captchaActions(page)).toEqual(['login']);

    await page.screenshot({
      path: 'test-results/auth-login-captcha-escalation.png',
      fullPage: true,
    });
  });

  test('signup checks email availability with CAPTCHA-protected POST', async ({ page }) => {
    let availabilityRequest: Request | undefined;
    page.on('request', (request) => {
      if (request.method() === 'POST' && request.url().endsWith('/api/auth/email/availability')) {
        availabilityRequest = request;
      }
    });

    await page.goto('/auth/signup');
    await page
      .getByPlaceholder('name@company.com')
      .fill(`browser-availability-${Date.now()}@example.com`);

    await expect(page.getByText('That email is available.')).toBeVisible();
    expect(availabilityRequest).toBeDefined();
    expect(availabilityRequest?.postDataJSON()).toMatchObject({
      captcha: 'e2e-captcha-email_availability',
    });
    expect(await captchaActions(page)).toContain('email_availability');

    await page.screenshot({
      path: 'test-results/auth-signup-email-availability.png',
      fullPage: true,
    });
  });

  for (const status of [401, 429]) {
    test(`signup treats ${status} availability responses as neutral`, async ({ page }) => {
      await page.route('**/api/auth/email/availability', async (route) => {
        await route.fulfill({
          status,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            message:
              status === 429
                ? 'Too many requests. Please try again later.'
                : 'Request could not be verified.',
            data: null,
            error: { code: status === 429 ? 'TOO_MANY_REQUESTS' : 'UNAUTHORIZED' },
            meta: null,
          }),
        });
      });

      await page.goto('/auth/signup');
      await page
        .getByPlaceholder('name@company.com')
        .fill(`browser-neutral-${status}-${Date.now()}@example.com`);

      await expect(page.getByText('Availability could not be checked.')).toBeVisible();
      await expect(page.getByText('That email is available.')).toBeHidden();
      await expect(page.getByText('That email is already registered.')).toBeHidden();
    });
  }

  test('profile email change uses the same CAPTCHA-protected POST probe', async ({ page }) => {
    await page.goto('/auth/login');
    await page.getByPlaceholder('Enter your username').fill('avaparticipant');
    await page.getByPlaceholder('Enter your password').fill('Password123!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await page.waitForURL(/\/dashboard(?:[/?#]|$)/);

    await page.getByRole('button', { name: /avaparticipant/i }).click();
    await page.getByRole('link', { name: 'Account settings', exact: true }).click();
    await page.waitForURL(/\/account\/profile(?:[/?#]|$)/);
    await page.getByRole('button', { name: 'Change email' }).click();
    const input = page.locator('#change-email');
    await expect(input).toBeVisible();

    const responsePromise = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        response.url().endsWith('/api/auth/email/availability'),
    );
    await input.fill(`profile-availability-${Date.now()}@example.com`);
    const response = await responsePromise;

    expect(response.status(), await response.text()).toBe(200);
    expect(response.request().postDataJSON()).toMatchObject({
      captcha: 'e2e-captcha-email_availability',
    });
    await expect(page.getByText('Availability could not be checked.')).toBeHidden();

    await page.screenshot({
      path: 'test-results/auth-profile-email-availability.png',
      fullPage: true,
    });
  });
});

async function captchaActions(page: Page): Promise<string[]> {
  return page.evaluate(() => (window as GrecaptchaWindow).captchaActions ?? []);
}
