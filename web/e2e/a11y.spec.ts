import AxeBuilder from "@axe-core/playwright"
import type { Page } from "@playwright/test"
import { adminStatus, answer, examples, expect, search, test } from "./support/mock-api.ts"

const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]

// Reduced motion: sheets and transitions settle at once, so axe never measures a half-faded colour.
test.use({ reducedMotion: "reduce" })

async function expectNoViolations(page: Page, screen: string) {
  const { violations } = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()
  const found = violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`)
  expect.soft(found, `axe on ${screen}`).toEqual([])
}

test("no WCAG 2.2 A/AA violations on home, results with activity open, History, Admin, How it works and 404", async ({
  page,
  api,
}) => {
  api.on("GET", "/api/books/search", answer(examples.match))
  api.on("GET", "/api/admin/ai", { body: adminStatus() })

  await page.goto("/")
  await expect(page.getByRole("heading", { level: 1, name: "Which book are you thinking of?" })).toBeVisible()
  await expectNoViolations(page, "home")

  await search(page, "tolkien hobbit")
  await expect(page.getByRole("heading", { level: 2, name: "Best match" })).toBeVisible()
  await page.getByRole("button", { name: /^Search activity/ }).click()
  await expect(page.getByText("Searching Open Library")).toBeVisible()
  await expectNoViolations(page, "match results with Search activity open")

  await page.getByRole("button", { name: "History" }).click()
  await expect(page.getByRole("dialog", { name: "Search history" })).toBeVisible()
  await expectNoViolations(page, "History sheet")
  await page.keyboard.press("Escape")

  await page.goto("/admin")
  await expect(page.getByRole("button", { name: "Add key for Anthropic" })).toBeVisible()
  await expectNoViolations(page, "Admin")

  await page.goto("/how-it-works")
  await expect(page.getByRole("heading", { level: 1, name: "How Find That Book works" })).toBeVisible()
  await expectNoViolations(page, "How it works")
  // The other states: a rejected sentence, removed AI values, penalties, a "few books" outcome, skipped steps.
  await page.locator("#explain figure").getByRole("button", { name: "Invented praise" }).click()
  await page.locator("#read figure").getByRole("button", { name: "Noisy" }).click()
  await page.locator("#rank figure").getByRole("button", { name: "hobbit dixon" }).click()
  await page.locator("#decide figure").getByRole("button", { name: "dickens" }).click()
  await page.locator("#search figure").getByRole("button", { name: "dickens" }).click()
  await expect(page.locator("#explain figure").getByText("The check rejects it")).toBeVisible()
  await expectNoViolations(page, "How it works, other examples")

  await page.goto("/nope")
  await expect(page.getByRole("heading", { level: 1, name: "Page not found" })).toBeVisible()
  await expectNoViolations(page, "404")
})
