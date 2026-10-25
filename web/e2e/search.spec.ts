import { formatDuration } from "../src/lib/format.ts"
import { answer, composer, examples, expect, horizontalOverflow, problem, search, test } from "./support/mock-api.ts"

const HOME_HEADING = "Which book are you thinking of?"

test("home fits 390 and 1440 px without sideways scrolling, and an example fills the composer without searching", async ({
  page,
  api,
}) => {
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    await page.goto("/")
    await expect(page.getByRole("heading", { level: 1, name: HOME_HEADING })).toBeVisible()
    await expect(composer(page)).toBeVisible()
    expect(await horizontalOverflow(page), `overflow at ${width}px`).toBe(0)
  }
  await expect(page).toHaveTitle("Find That Book")

  await page.getByRole("group", { name: "Examples" }).getByRole("button", { name: "mark huckleberry" }).click()
  await expect(composer(page)).toHaveValue("mark huckleberry")
  await expect(composer(page)).toBeFocused()
  expect(api.count("GET", "/api/books/search")).toBe(0)
})

test("suggestions: ArrowDown and Enter fill title and author without searching; Escape keeps the text", async ({
  page,
  api,
}) => {
  const suggestions = examples.suggestions()
  api.on("GET", "/api/suggest", (url) => ({
    body: url.searchParams.get("prefix") ? suggestions : examples.noSuggestions(),
  }))
  await page.goto("/")

  await composer(page).pressSequentially("harry")
  await expect(page.getByRole("option")).toHaveCount(suggestions.suggestions.length)
  await composer(page).press("ArrowDown")
  await composer(page).press("Enter")
  const first = suggestions.suggestions[0]
  await expect(composer(page)).toHaveValue(`${first.title} ${first.author}`)
  await expect(page.getByRole("listbox")).toBeHidden()

  await composer(page).fill("")
  await composer(page).pressSequentially("harry")
  await expect(page.getByRole("listbox")).toBeVisible()
  await composer(page).press("Escape")
  await expect(page.getByRole("listbox")).toBeHidden()
  await composer(page).press("Escape")
  await expect(composer(page)).toHaveValue("harry")

  expect(api.count("GET", "/api/books/search")).toBe(0)
  const lookup = api.requests.find(
    (r) => r.url.pathname === "/api/suggest" && r.url.searchParams.get("prefix") === "harry"
  )
  expect(lookup?.url.searchParams.get("limit")).toBe("6")
})

test("a match shows the best match with its Open Library link, and Search activity expands to measured steps", async ({
  page,
  api,
}) => {
  const match = examples.match()
  api.on("GET", "/api/books/search", answer(examples.match))
  await page.goto("/")
  await search(page, "tolkien hobbit")

  const best = page.getByRole("region", { name: "Best match" })
  const [first] = match.candidates
  await expect(best.getByRole("heading", { level: 3, name: first.title })).toBeVisible()
  await expect(
    best.getByRole("link", {
      name: `Open “${first.title}” (${first.author}, ${first.first_publish_year}) on Open Library`,
    })
  ).toHaveAttribute("href", first.open_library_url)
  await expect(page).toHaveURL(/\/\?entry=[a-z0-9]+$/)
  await expect(page).toHaveTitle("“tolkien hobbit” – Find That Book")

  const activity = page.getByRole("button", { name: /^Search activity/ })
  await expect(activity).toHaveAttribute("aria-expanded", "false")
  await activity.click()
  await expect(activity).toHaveAttribute("aria-expanded", "true")
  await expect(page.getByText("Understanding your clues")).toBeVisible()
  await expect(page.getByText("Searching Open Library")).toBeVisible()
  await expect(page.getByText(formatDuration(match.meta.timings_ms.retrieve), { exact: true }).first()).toBeVisible()
  expect(api.count("GET", "/api/books/search")).toBe(1)
})

test("ambiguous and no-match answers show their own headings and the API's hint", async ({ page, api }) => {
  const ambiguous = examples.ambiguous()
  const none = examples.none()
  api.on("GET", "/api/books/search", (url) =>
    answer(url.searchParams.get("q") === "dickens" ? examples.ambiguous : examples.none)(url, undefined)
  )
  await page.goto("/")

  await search(page, "dickens")
  const several = page.getByRole("region", { name: "A few books fit your clues" })
  await expect(several.getByRole("listitem")).toHaveCount(ambiguous.candidates.length)
  await expect(several.getByText(ambiguous.hint ?? "")).toBeVisible()

  await search(page, "xqzv blorptangle")
  await expect(page.getByRole("heading", { level: 2, name: "No book matched" })).toBeVisible()
  if (none.hint) await expect(page.getByText(none.hint)).toBeVisible()
  await expect(page.getByRole("region", { name: "A few books fit your clues" })).toHaveCount(0)
})

test("a 503 keeps Try again disabled for Retry-After, then retries once; Stop shows Search stopped", async ({
  page,
  api,
}) => {
  let unavailable = true
  api.on("GET", "/api/books/search", (url) =>
    unavailable
      ? problem(examples.catalogUnavailable(), { "Retry-After": "2" })
      : answer(examples.match)(url, undefined)
  )
  await page.goto("/")
  await search(page, "tolkien hobbit")

  const tryAgain = page.getByRole("button", { name: "Try again" })
  await expect(tryAgain).toBeDisabled()
  await expect(page.getByText(/You can try again in \d+ s/)).toBeVisible()
  unavailable = false
  await expect(tryAgain).toBeEnabled({ timeout: 5_000 })
  expect(api.count("GET", "/api/books/search")).toBe(1)
  await tryAgain.click()
  await expect(page.getByRole("heading", { level: 2, name: "Best match" })).toBeVisible()
  expect(api.count("GET", "/api/books/search")).toBe(2)

  api.on("GET", "/api/books/search", (url) => ({
    body: { ...examples.match(), query: url.searchParams.get("q") },
    delayMs: 30_000,
  }))
  await search(page, "the hobbit")
  await page.getByRole("button", { name: "Stop" }).click()
  await expect(page.getByText("Search stopped", { exact: true })).toBeVisible()
  await expect(page.getByRole("button", { name: "Search again" })).toBeVisible()
  await expect(page.getByRole("button", { name: "Search", exact: true })).toBeVisible()
})
