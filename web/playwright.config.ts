import { defineConfig, devices } from "@playwright/test"

const PORT = 5199

/**
 * Browser smoke tests. Playwright starts its own Vite dev server; every /api call is mocked in the page
 * (e2e/support/mock-api.ts), and the dev proxy points at a closed port so nothing can reach a real API by mistake.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${PORT}`,
    locale: "en-US",
    timezoneId: "America/Denver",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `npx vite --port ${PORT} --strictPort`,
    url: `http://localhost:${PORT}`,
    reuseExistingServer: false,
    timeout: 60_000,
    env: { FTB_API_URL: "http://127.0.0.1:9" },
  },
})
