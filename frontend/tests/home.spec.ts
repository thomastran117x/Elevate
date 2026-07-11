import { expect, test } from 'playwright/test';

test('home page loads', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle(/EventXperience/i);
  await expect(
    page.getByRole('heading', {
      name: /A cleaner way to discover, organize, and launch memorable events\./i,
    }),
  ).toBeVisible();
  await expect(page.getByPlaceholder('Artist, campus event, venue, organizer')).toBeVisible();
  await expect(page.getByRole('button', { name: /Explore events/i })).toBeVisible();
});
