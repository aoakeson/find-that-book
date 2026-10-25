import { answer, composer, examples, expect, search, test } from "./support/mock-api.ts"

test("History restores an older search without searching again, and a reload keeps it", async ({ page, api }) => {
  api.on("GET", "/api/books/search", (url) =>
    answer(url.searchParams.get("q") === "dickens" ? examples.ambiguous : examples.match)(url, undefined)
  )
  await page.goto("/")
  await search(page, "dickens")
  await expect(page.getByRole("heading", { level: 2, name: "A few books fit your clues" })).toBeVisible()
  await search(page, "tolkien hobbit")
  await expect(page.getByRole("heading", { level: 2, name: "Best match" })).toBeVisible()

  await page.getByRole("button", { name: "History" }).click()
  const sheet = page.getByRole("dialog", { name: "Search history" })
  await expect(sheet).toBeVisible()
  await sheet.getByRole("button", { name: "dickens", exact: true }).click()
  await expect(sheet).toBeHidden()

  for (const when of ["after picking it", "after a reload"]) {
    await expect(page.getByRole("heading", { level: 2, name: "A few books fit your clues" }), when).toBeVisible()
    await expect(page.getByRole("button", { name: "Search again" }), when).toBeVisible()
    await expect(page.getByText(/^Saved /), when).toBeVisible()
    await expect(composer(page), when).toHaveValue("dickens")
    if (when === "after picking it") await page.reload()
  }
  expect(api.count("GET", "/api/books/search")).toBe(2)
})
