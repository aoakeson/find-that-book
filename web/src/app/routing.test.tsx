import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { fakeApi, json } from "@/test/fake-api"
import { adminStatus, fixtures } from "@/test/fixtures"
import { renderApp } from "@/test/render"

/** Flip on to make the Admin page (a route) or the header (the layout itself) throw while rendering. */
const crash = vi.hoisted(() => ({ admin: false, header: false }))

vi.mock("@/app/AppHeader", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/app/AppHeader")>()
  function MaybeCrashingAppHeader() {
    if (crash.header) throw new Error("Kaboom in the layout")
    return <actual.AppHeader />
  }
  return { ...actual, AppHeader: MaybeCrashingAppHeader }
})

vi.mock("@/features/admin/AdminPage", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/admin/AdminPage")>()
  function MaybeCrashingAdminPage() {
    if (crash.admin) throw new Error("Kaboom: internal detail that must not reach the screen")
    return <actual.AdminPage />
  }
  return { ...actual, AdminPage: MaybeCrashingAdminPage }
})

const HOME_HEADING = "Which book are you thinking of?"

/** Every free call the pages may make on their own; no search is ever expected here. */
function quietApi() {
  return fakeApi({
    "GET /api/admin/ai": () => json(adminStatus()),
    "GET /api/suggest": () => json(fixtures.suggestEmpty()),
    "GET /api/health": () => json(fixtures.health()),
  })
}

const heading = (name: string) => screen.findByRole("heading", { level: 1, name })

beforeEach(() => {
  crash.admin = false
  crash.header = false
})

describe("direct entry", () => {
  it("/ renders the search page", async () => {
    quietApi()
    await renderApp("/")
    expect(await heading(HOME_HEADING)).toHaveAttribute("id", "page-heading")
    expect(screen.getByLabelText("Describe the book")).toBeInTheDocument()
    expect(document.title).toBe("Find That Book")
  })

  it("/admin renders the Admin page", async () => {
    const api = quietApi()
    await renderApp("/admin")
    expect(await heading("Admin")).toHaveAttribute("id", "page-heading")
    expect(screen.getByRole("link", { name: "Back to search" })).toBeInTheDocument()
    expect(await screen.findByRole("heading", { level: 2, name: "AI provider" })).toBeInTheDocument()
    await waitFor(() => expect(document.title).toBe("Admin – Find That Book"))
    expect(api.count("GET", "/api/books/search")).toBe(0)
  })

  it("/how-it-works renders the How it works page", async () => {
    const api = quietApi()
    await renderApp("/how-it-works")
    expect(await heading("How Find That Book works")).toHaveAttribute("id", "page-heading")
    expect(screen.getByRole("link", { name: "How it works" })).toHaveAttribute("aria-current", "page")
    await waitFor(() => expect(document.title).toBe("How it works – Find That Book"))
    expect(api.count("GET", "/api/books/search")).toBe(0)
  })

  it("an unknown path renders the 404 page, and Back to search works", async () => {
    quietApi()
    const { router } = await renderApp("/no/such/page")
    expect(await heading("Page not found")).toHaveAttribute("id", "page-heading")
    await waitFor(() => expect(document.title).toBe("Page not found – Find That Book"))

    await userEvent.click(screen.getByRole("link", { name: "Back to search" }))
    expect(await heading(HOME_HEADING)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/")
    await waitFor(() => expect(document.title).toBe("Find That Book"))
  })
})

describe("error boundary", () => {
  beforeEach(() => {
    // React and the error page both log the thrown error; keep the test output readable.
    vi.spyOn(console, "error").mockImplementation(() => {})
  })

  it("shows recovery UI without the error's own text, and Try again recovers", async () => {
    quietApi()
    crash.admin = true
    await renderApp("/admin")

    expect(await heading("Something went wrong")).toBeInTheDocument()
    expect(screen.queryByText(/Kaboom/)).not.toBeInTheDocument()
    expect(screen.getByText(/^FTB-[0-9A-F]{8}$/)).toBeInTheDocument()
    await waitFor(() => expect(document.title).toBe("Something went wrong – Find That Book"))

    crash.admin = false
    await userEvent.click(screen.getByRole("button", { name: "Try again" }))
    expect(await heading("Admin")).toBeInTheDocument()
  })

  it("a layout that throws gets the root error screen (outside the shell), and Try again recovers", async () => {
    quietApi()
    crash.header = true
    await renderApp("/")

    expect(await heading("Something went wrong")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "History" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Back to search" })).toHaveAttribute("href", "/")

    crash.header = false
    await userEvent.click(screen.getByRole("button", { name: "Try again" }))
    expect(await heading(HOME_HEADING)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "New search" })).toBeInTheDocument()
  })

  it("offers Back to search", async () => {
    quietApi()
    crash.admin = true
    await renderApp("/admin")
    expect(await heading("Something went wrong")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Back to search" })).toHaveAttribute("href", "/")
  })

  it("a page that throws keeps the header, and Back to search returns in-app", async () => {
    quietApi()
    crash.admin = true
    const { router } = await renderApp("/admin")
    expect(await heading("Something went wrong")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "History" })).toBeInTheDocument()

    crash.admin = false
    await userEvent.click(screen.getByRole("link", { name: "Back to search" }))
    expect(await heading(HOME_HEADING)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe("/")
  })
})

describe("client-side navigation", () => {
  const focusedHeading = () =>
    document.activeElement?.id === "page-heading" ? document.activeElement.textContent : null

  it("leaves focus alone on the first load", async () => {
    quietApi()
    await renderApp("/admin")
    await heading("Admin")
    await new Promise((r) => setTimeout(r, 50))
    expect(focusedHeading()).toBeNull()
  })

  it("moves focus to the new page's heading and updates the title", async () => {
    quietApi()
    await renderApp("/")
    await heading(HOME_HEADING)

    await userEvent.click(screen.getByRole("link", { name: "Admin" }))
    await waitFor(() => expect(focusedHeading()).toBe("Admin"))
    await waitFor(() => expect(document.title).toBe("Admin – Find That Book"))

    await userEvent.click(screen.getByRole("link", { name: "Back to search" }))
    await waitFor(() => expect(focusedHeading()).toBe(HOME_HEADING))
    await waitFor(() => expect(document.title).toBe("Find That Book"))
  })

  it("the header's How it works tab opens the page, and its Back to search returns home", async () => {
    quietApi()
    await renderApp("/")
    await heading(HOME_HEADING)

    await userEvent.click(screen.getByRole("link", { name: "How it works" }))
    await waitFor(() => expect(focusedHeading()).toBe("How Find That Book works"))
    await waitFor(() => expect(document.title).toBe("How it works – Find That Book"))

    await userEvent.click(screen.getByRole("link", { name: "Back to search" }))
    await waitFor(() => expect(focusedHeading()).toBe(HOME_HEADING))
  })

  it("moves focus to the 404 heading when a link leads nowhere", async () => {
    quietApi()
    const { router } = await renderApp("/")
    await heading(HOME_HEADING)
    // Not a link the app offers; stands in for a stale bookmark followed in-app.
    await router.navigate({ to: "/no/such/page" as "/" })
    await waitFor(() => expect(focusedHeading()).toBe("Page not found"))
  })

  it("Admin's Back to search returns to the answer that was on screen, without searching again", async () => {
    const api = fakeApi({
      "GET /api/books/search": (url) => json({ ...fixtures.match(), query: url.searchParams.get("q") }),
      "GET /api/admin/ai": () => json(adminStatus()),
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
    })
    const { router } = await renderApp("/")
    await userEvent.type(screen.getByLabelText("Describe the book"), "tolkien hobbit{Enter}")
    await waitFor(() => expect(router.state.location.search).toHaveProperty("entry"))
    const { entry } = router.state.location.search as { entry: string }

    await userEvent.click(screen.getByRole("link", { name: "Admin" }))
    await heading("Admin")
    await userEvent.click(screen.getByRole("link", { name: "Back to search" }))

    await waitFor(() => expect(router.state.location.search).toEqual({ entry }))
    expect(await screen.findByRole("heading", { level: 2, name: "Best match" })).toBeInTheDocument()
    expect(api.count("GET", "/api/books/search")).toBe(1)
  })
})
