import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.QA_BASE_URL || 'http://127.0.0.1:5173';

export default defineConfig({
  testDir: './tests',
  workers: 1,
  retries: 0,
  timeout: 90_000,
  reporter: 'list',
  use: {
    baseURL,
    ...devices['Desktop Chrome'],
    // No traces: login requests contain temporary QA credentials.
    trace: 'off',
  },
  webServer: {
    command: 'npm run dev',
    url: baseURL,
    reuseExistingServer: false,
    env: { QA_API_URL: process.env.QA_API_URL || 'http://127.0.0.1:5081' },
  },
});
