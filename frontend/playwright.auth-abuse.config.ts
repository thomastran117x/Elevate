import { defineConfig } from 'playwright/test';

import baseConfig from './playwright.config';

process.env['PLAYWRIGHT_LIVE_AUTH'] = 'true';

export default defineConfig(baseConfig, {
  testMatch: 'auth-abuse-protection.spec.ts',
});
