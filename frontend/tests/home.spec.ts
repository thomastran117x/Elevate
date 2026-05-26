import { expect, test } from 'playwright/test';

test('home page loads', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle(/EventXperience/i);
  await expect(page.getByRole('heading', { name: /Book unforgettable events/i })).toBeVisible();
  await expect(page.getByPlaceholder('Search artists, teams, venues...')).toBeVisible();
  await expect(page.getByRole('link', { name: /Get started/i })).toBeVisible();
});
