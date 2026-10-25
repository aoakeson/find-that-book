import type { Page } from "@playwright/test"
import { adminStatus, expect, problem, test } from "./support/mock-api.ts"

test.describe("Admin", () => {
  test("shows the API's status, and puts a rejected key's message on the field", async ({ page, api }) => {
    api.on("GET", "/api/admin/ai", { body: adminStatus() })
    api.on(
      "PUT",
      "/api/admin/ai/keys/anthropic",
      problem({
        type: "/problems/validation-failed",
        title: "One or more validation errors occurred.",
        status: 400,
        errors: { ApiKey: ["A key has no spaces or control characters."] },
        code: "validation_failed",
        trace_id: "00-e2e-01",
      })
    )
    await page.goto("/admin")
    await expect(page.getByRole("heading", { level: 1, name: "Admin" })).toBeVisible()
    await expect(page.getByRole("heading", { level: 2, name: "AI provider" })).toBeVisible()
    await expect(page).toHaveTitle("Admin – Find That Book")

    await page.getByRole("button", { name: "Add key for Anthropic" }).click()
    const field = page.getByLabel("Anthropic API key")
    await field.fill("fake key with spaces")
    await page.getByRole("button", { name: "Save key" }).click()
    await expect(page.getByText("A key has no spaces or control characters.")).toBeVisible()
    await expect(field).toHaveAttribute("aria-invalid", "true")

    const put = api.requests.find((r) => r.method === "PUT")
    expect(put?.body).toEqual({ api_key: "fake key with spaces" })
    expect(put?.url.search).toBe("")

    await page.getByRole("link", { name: "Back to search" }).click()
    await expect(page.getByRole("heading", { level: 1, name: "Which book are you thinking of?" })).toBeVisible()
  })

  test("says it isn't available when the API answers 404", async ({ page, api }) => {
    api.on(
      "GET",
      "/api/admin/ai",
      problem({
        type: "/problems/not-found",
        title: "No such API endpoint",
        status: 404,
        code: "not_found",
        trace_id: "00-e2e-02",
      })
    )
    await page.goto("/admin")
    await expect(page.getByText("Admin isn't available here")).toBeVisible()
    await expect(page.getByRole("button", { name: /Add key|Replace key/ })).toHaveCount(0)
  })
})

test("an unknown address shows the 404 page, and Back to search goes home", async ({ page }) => {
  await page.goto("/nope")
  await expect(page.getByRole("heading", { level: 1, name: "Page not found" })).toBeVisible()
  await expect(page).toHaveTitle("Page not found – Find That Book")
  await page.getByRole("link", { name: "Back to search" }).click()
  await expect(page.getByRole("heading", { level: 1, name: "Which book are you thinking of?" })).toBeVisible()
  await expect(page).toHaveURL(/\/$/)
})

test.describe("background motion", () => {
  const paper = (page: Page) => page.locator(".paper")
  const animations = (page: Page, state?: string) =>
    page.evaluate<number>(
      `document.querySelector(".paper").getAnimations({ subtree: true }).filter((a) => ${JSON.stringify(state ?? null)} === null || a.playState === ${JSON.stringify(state ?? null)}).length`
    )

  test("runs by default, and the pause control stops it and is remembered", async ({ page }) => {
    await page.goto("/")
    await expect(paper(page)).toHaveAttribute("data-motion", "running")
    expect(await animations(page, "running")).toBeGreaterThan(0)

    const toggle = page.getByRole("button", { name: "Pause background motion" })
    await expect(toggle).toHaveAttribute("aria-pressed", "false")
    await toggle.click()
    await expect(toggle).toHaveAttribute("aria-pressed", "true")
    await expect(paper(page)).toHaveAttribute("data-motion", "paused")
    expect(await animations(page, "running")).toBe(0)

    await page.reload()
    await expect(paper(page)).toHaveAttribute("data-motion", "paused")
    await expect(page.getByRole("button", { name: "Pause background motion" })).toHaveAttribute("aria-pressed", "true")
  })

  test("is off, with no animations at all, when the system asks for reduced motion", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" })
    await page.goto("/")
    await expect(paper(page)).toHaveAttribute("data-motion", "off")
    expect(await animations(page)).toBe(0)
    await expect(page.getByRole("button", { name: "Pause background motion" })).toHaveCount(0)
  })
})
