import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import { toSnapshot } from "@/features/history/snapshot"
import { historyStore } from "@/features/history/storage"
import { fakeApi, json, problem } from "@/test/fake-api"
import { fixtures } from "@/test/fixtures"
import { renderApp } from "@/test/render"
import { adminStatus, notFound, providerIn } from "./admin.test-data"

function keyRow(title: string): HTMLElement {
  const row = screen.getByRole("heading", { level: 4, name: title }).closest("li")
  if (!row) throw new Error(`no row for ${title}`)
  return row
}

describe("Admin page", () => {
  it("shows the page and the AI status in words", async () => {
    fakeApi({ "GET /api/admin/ai": () => json(adminStatus()) })
    await renderApp("/admin")

    expect(screen.getByRole("heading", { level: 1, name: "Admin" })).toHaveAttribute("id", "page-heading")
    expect(screen.getByRole("heading", { level: 2, name: "AI provider" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Back to search" })).toHaveAttribute("href", "/")
    expect(document.title).toBe("Admin – Find That Book")

    expect(await screen.findByText("AI is on: Gemini, using gemini-3.5-flash-lite.")).toBeInTheDocument()
    expect(
      screen.getByText("Changes here last until the API restarts. Your .env file is never changed.")
    ).toBeInTheDocument()
    expect(screen.getByRole("combobox", { name: "Use for searches" })).toHaveTextContent("Gemini")
    expect(screen.getByRole("switch", { name: "Use the second opinion" })).not.toBeChecked()

    const gemini = keyRow("Gemini")
    expect(within(gemini).getByText("In use")).toBeInTheDocument()
    expect(within(gemini).getByText("Key set from .env")).toBeInTheDocument()
    expect(within(gemini).getByText("Fingerprint sha256:81536b1d")).toBeInTheDocument()
    expect(within(gemini).getByRole("button", { name: "Replace key for Gemini" })).toBeInTheDocument()
    expect(within(gemini).queryByRole("button", { name: /Remove/ })).not.toBeInTheDocument()

    const anthropic = keyRow("Anthropic")
    expect(within(anthropic).getByText("Not set")).toBeInTheDocument()
    expect(within(anthropic).getByRole("button", { name: "Add key for Anthropic" })).toBeInTheDocument()
    expect(within(anthropic).queryByRole("button", { name: /Test connection/ })).not.toBeInTheDocument()

    const judge = keyRow("Second opinion (OpenAI Decisions)")
    expect(within(judge).getByText("Same key as OpenAI")).toBeInTheDocument()
    expect(within(judge).getByRole("button", { name: "Add key for Second opinion" })).toBeInTheDocument()
    expect(within(judge).queryByRole("button", { name: /Test connection/ })).not.toBeInTheDocument()
  })

  it.each([
    [
      "AI is off",
      (s: ReturnType<typeof adminStatus>) => ((s.active_provider = "none"), (s.ai_configured = false)),
      "AI is off. Searches still work, without AI.",
    ],
    [
      "a selected provider without a key",
      (s: ReturnType<typeof adminStatus>) => {
        s.active_provider = "anthropic"
        s.ai_configured = false
      },
      "Anthropic is selected but has no key, so searches run without AI. Add a key for it below.",
    ],
    [
      "a key the provider turned down",
      (s: ReturnType<typeof adminStatus>) => (s.ai_configured = false),
      "Gemini is selected but isn't available right now, so searches run without AI. Test the connection below to see why.",
    ],
  ])("describes %s", async (_name, change, text) => {
    fakeApi({ "GET /api/admin/ai": () => json(adminStatus(change)) })
    await renderApp("/admin")
    expect(await screen.findByText(text)).toBeInTheDocument()
  })

  it("shows an unavailable state, not a form, when the admin API answers 404", async () => {
    fakeApi({ "GET /api/admin/ai": () => problem(notFound) })
    await renderApp("/admin")

    expect(await screen.findByText("Admin isn't available here")).toBeInTheDocument()
    expect(screen.getByText(/only work while the API runs in Development mode/)).toBeInTheDocument()
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /key/ })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument()
  })

  it("shows a load failure with Try again, and retries only when asked", async () => {
    let fail = true
    const api = fakeApi({
      "GET /api/admin/ai": () => {
        if (fail) throw new TypeError("Failed to fetch")
        return json(adminStatus())
      },
    })
    await renderApp("/admin")

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent("Couldn't load the AI settings")
    expect(alert).toHaveTextContent("Couldn't reach the API. Check that it's running, then try again.")
    expect(api.count("GET", "/api/admin/ai")).toBe(1)

    fail = false
    await userEvent.click(within(alert).getByRole("button", { name: "Try again" }))
    expect(await screen.findByText("AI is on: Gemini, using gemini-3.5-flash-lite.")).toBeInTheDocument()
    expect(api.count("GET", "/api/admin/ai")).toBe(2)
  })

  it("switches the provider and shows the API's new status", async () => {
    const user = userEvent.setup()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai": () =>
        json(
          adminStatus((s) => {
            s.active_provider = "openai"
            providerIn(s, "gemini").active = false
            providerIn(s, "openai").active = true
          })
        ),
    })
    await renderApp("/admin")

    const save = await screen.findByRole("button", { name: "Save provider" })
    expect(save).toHaveAttribute("aria-disabled", "true") // nothing changed yet

    await user.click(screen.getByRole("combobox", { name: "Use for searches" }))
    await user.click(await screen.findByRole("option", { name: "OpenAI" }))
    await user.click(save)

    expect(await screen.findByText("AI is on: OpenAI, using gpt-4.1-mini.")).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === "PUT")?.body).toEqual({ provider: "openai" })
    expect(within(keyRow("OpenAI")).getByText("In use")).toBeInTheDocument()
    expect(within(keyRow("Gemini")).queryByText("In use")).not.toBeInTheDocument()
  })

  it("warns before choosing a provider without a key, and shows a failed save next to the control", async () => {
    const user = userEvent.setup()
    const serverError = { status: 500, code: "internal_error", title: "An unexpected error occurred." }
    fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai": () => problem(serverError),
    })
    await renderApp("/admin")

    await user.click(await screen.findByRole("combobox", { name: "Use for searches" }))
    await user.click(await screen.findByRole("option", { name: "Anthropic" }))
    expect(screen.getByText("Anthropic has no key yet. Add one below, or searches run without AI.")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Save provider" }))
    expect(await screen.findByRole("alert")).toHaveTextContent("The API couldn't make this change. Try again.")
    // The status still says what the API has, not what was attempted.
    expect(screen.getByText("AI is on: Gemini, using gemini-3.5-flash-lite.")).toBeInTheDocument()
  })

  it("turns the second opinion on through the API", async () => {
    const user = userEvent.setup()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai": () => json(adminStatus((s) => (s.judge.enabled = true))),
    })
    await renderApp("/admin")

    const toggle = await screen.findByRole("switch", { name: "Use the second opinion" })
    await user.click(toggle)

    await waitFor(() => expect(toggle).toBeChecked())
    expect(api.calls.find((c) => c.method === "PUT")?.body).toEqual({ judge_enabled: true })
  })

  it("a status reload that was already running when a change was saved can't undo the change", async () => {
    const user = userEvent.setup()
    let gets = 0
    let releaseStaleGet: (() => void) | undefined
    fakeApi({
      "GET /api/admin/ai": () => {
        gets++
        if (gets === 1) return json(adminStatus())
        // The second GET (a background reload) answers late with the status from before the change.
        return new Promise<Response>((resolve) => (releaseStaleGet = () => resolve(json(adminStatus()))))
      },
      "PUT /api/admin/ai": () => json(adminStatus((s) => (s.active_provider = "openai"))),
    })
    const { queryClient } = await renderApp("/admin")
    await screen.findByRole("combobox", { name: "Use for searches" })
    act(() => {
      void queryClient.invalidateQueries({ queryKey: ["admin", "ai"] })
    })
    await waitFor(() => expect(gets).toBe(2))

    await user.click(screen.getByRole("combobox", { name: "Use for searches" }))
    await user.click(await screen.findByRole("option", { name: "OpenAI" }))
    await user.click(screen.getByRole("button", { name: "Save provider" }))
    expect(await screen.findByText(/AI is on: OpenAI/)).toBeInTheDocument()

    await act(async () => releaseStaleGet?.())
    await act(() => new Promise((r) => setTimeout(r, 20)))
    expect(screen.getByText(/AI is on: OpenAI/)).toBeInTheDocument()
  })

  it("two quick changes end with the later one's status, even when the first answer is slower", async () => {
    const user = userEvent.setup()
    // A server that applies each change on arrival but answers the first one late.
    const server = adminStatus()
    let answered = 0
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai": async (_url, init) => {
        const change = JSON.parse(String(init?.body)) as { provider?: string; judge_enabled?: boolean }
        if (change.provider) {
          server.active_provider = change.provider
          server.providers.forEach((p) => (p.active = p.name === change.provider))
        }
        if (change.judge_enabled !== undefined) server.judge.enabled = change.judge_enabled
        const status = structuredClone(server)
        const slow = api.count("PUT", "/api/admin/ai") === 1
        await new Promise((r) => setTimeout(r, slow ? 80 : 0))
        answered++
        return json(status)
      },
    })
    await renderApp("/admin")

    await user.click(await screen.findByRole("combobox", { name: "Use for searches" }))
    await user.click(await screen.findByRole("option", { name: "OpenAI" }))
    await user.click(screen.getByRole("button", { name: "Save provider" }))
    await user.click(screen.getByRole("switch", { name: "Use the second opinion" }))

    await waitFor(() => expect(answered).toBe(2))
    await act(() => new Promise((r) => setTimeout(r, 20)))
    expect(screen.getByText("AI is on: OpenAI, using gpt-4.1-mini.")).toBeInTheDocument()
    expect(screen.getByRole("switch", { name: "Use the second opinion" })).toBeChecked()
  })

  it("goes back to the answer that was on screen without searching again", async () => {
    const entry = historyStore.add("tolkien hobbit", toSnapshot(fixtures.match()))
    const api = fakeApi({ "GET /api/admin/ai": () => json(adminStatus()) })
    const { router } = await renderApp(`/?entry=${entry.id}`)

    await act(() => router.navigate({ to: "/admin" }))
    const back = await screen.findByRole("link", { name: "Back to search" })
    expect(back).toHaveAttribute("href", `/?entry=${entry.id}`)

    await userEvent.click(back)
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: entry.id }))
    expect(api.count("GET", "/api/books/search")).toBe(0)
  })
})
