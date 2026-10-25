import { answer, composer, examples, expect, horizontalOverflow, test } from "./support/mock-api.ts"

test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })

test("on a phone: no autofocus, search by tapping, read the answer, open History; nothing scrolls sideways", async ({
  page,
  api,
}) => {
  api.on("GET", "/api/books/search", answer(examples.match))
  await page.goto("/")
  await expect(page.getByRole("heading", { level: 1, name: "Which book are you thinking of?" })).toBeVisible()
  await expect(composer(page)).not.toBeFocused()

  await composer(page).tap()
  await composer(page).fill("tolkien hobbit")
  await page.getByRole("button", { name: "Search", exact: true }).tap()
  await expect(page.getByRole("heading", { level: 2, name: "Best match" })).toBeVisible()
  expect(await horizontalOverflow(page)).toBe(0)

  await page.getByRole("button", { name: "History" }).tap()
  const sheet = page.getByRole("dialog", { name: "Search history" })
  await expect(sheet.getByRole("button", { name: "tolkien hobbit", exact: true })).toBeVisible()
  expect((await sheet.boundingBox())?.width).toBeGreaterThanOrEqual(380)
  expect(api.count("GET", "/api/books/search")).toBe(1)
})
