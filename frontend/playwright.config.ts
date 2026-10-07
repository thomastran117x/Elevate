import { defineConfig, devices } from 'playwright/test';

export default defineConfig({
  testDir: './tests',
  reporter: 'list',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  workers: process.env['CI'] ? 1 : undefined,
  use: {
    baseURL: 'http://localhost:3101',
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: process.env['PLAYWRIGHT_CHROMIUM_EXECUTABLE']
          ? { executablePath: process.env['PLAYWRIGHT_CHROMIUM_EXECUTABLE'] }
          : undefined,
      },
    },
  ],
  webServer: {
    command: 'npm run start:e2e',
    url: 'http://localhost:3101',
    reuseExistingServer: !process.env['CI'],
    timeout: 120_000,
  },
});
