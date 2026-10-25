import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import type { AdminAiStatus } from "@/api/types"
import { fakeApi, json } from "@/test/fake-api"
import { renderApp } from "@/test/render"
import { adminStatus, providerIn } from "./admin.test-data"

const SECRET = "plainly-fake-test-secret-for-anthropic"

/** No key anywhere: the default Gemini provider is selected but can't run. */
const noKeys = () =>
  adminStatus((s) => {
    s.ai_configured = false
    for (const p of s.providers) Object.assign(p, { configured: false, key_source: "none", key_fingerprint: null })
    Object.assign(s.judge, { configured: false, key_source: "none", key_fingerprint: null })
  })

function turnOnPanel(): HTMLElement {
  const panel = screen.getByRole("heading", { level: 3, name: "Turn on AI" }).parentElement
  if (!panel) throw new Error("no Turn on AI panel")
  return panel
}

/** The API's default: automatic selection, and no provider has a key, so it resolves to "none". */
const autoNoKeys = () =>
  adminStatus((s) => {
    Object.assign(s, noKeys(), { active_provider: "none", provider_selection: "auto" })
    for (const p of s.providers) p.active = false
  })

describe("Turn on AI", () => {
  it("shows on the default automatic setup with no keys, and a pasted key needs no provider switch", async () => {
    const user = userEvent.setup()
    let status = autoNoKeys()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(status),
      "PUT /api/admin/ai/keys/gemini": () => {
        status = adminStatus((s) => (s.provider_selection = "auto"))
        return json(status)
      },
    })
    await renderApp("/admin")

    const panel = await waitFor(turnOnPanel)
    expect(screen.getByText(/Automatic: no provider has a key yet/)).toBeInTheDocument()
    await user.type(within(panel).getByLabelText("Gemini API key"), SECRET)
    await user.click(within(panel).getByRole("button", { name: "Save key" }))

    expect(
      await screen.findByText("AI is on: Gemini, using gemini-3.5-flash-lite (picked automatically).")
    ).toBeInTheDocument()
    expect(api.count("PUT", "/api/admin/ai")).toBe(0)
    expect(screen.queryByRole("heading", { name: "Turn on AI" })).not.toBeInTheDocument()
  })

  it('offers automatic selection in the provider list and sends it as "auto"', async () => {
    const user = userEvent.setup()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai": () => json(adminStatus((s) => (s.provider_selection = "auto"))),
    })
    await renderApp("/admin")

    await user.click(await screen.findByRole("combobox", { name: "Use for searches" }))
    await user.click(await screen.findByRole("option", { name: "Automatic (first with a key)" }))
    await user.click(screen.getByRole("button", { name: "Save provider" }))

    await waitFor(() => expect(api.count("PUT", "/api/admin/ai")).toBe(1))
    expect(api.calls.find((c) => c.method === "PUT")?.body).toEqual({ provider: "auto" })
    expect(await screen.findByText("Provider set to automatic")).toBeInTheDocument()
  })

  it("offers both ways when no key is set: paste one here, or put it in .env", async () => {
    fakeApi({ "GET /api/admin/ai": () => json(noKeys()) })
    await renderApp("/admin")

    const panel = await waitFor(turnOnPanel)
    expect(within(panel).getByLabelText("Gemini API key")).toHaveAttribute("type", "password")
    expect(within(panel).getByText("GEMINI_API_KEY=your-key")).toBeInTheDocument()
    expect(within(panel).getByRole("link", { name: /Get a free Gemini key/ })).toHaveAttribute(
      "href",
      "https://aistudio.google.com/apikey"
    )
    expect(within(panel).queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument()
  })

  it("is not shown once AI is configured, or when AI was turned off on purpose", async () => {
    fakeApi({
      "GET /api/admin/ai": () =>
        json(
          adminStatus((s) => {
            s.active_provider = "none"
            s.ai_configured = false
          })
        ),
    })
    await renderApp("/admin")
    await screen.findByRole("heading", { level: 3, name: "Active provider" })
    expect(screen.queryByRole("heading", { name: "Turn on AI" })).not.toBeInTheDocument()
  })

  it("adding the first key for another provider switches searches to it, and the key stays out of every cache", async () => {
    const user = userEvent.setup()
    let status: AdminAiStatus = noKeys()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(status),
      "PUT /api/admin/ai/keys/anthropic": () => {
        const p = providerIn(status, "anthropic")
        Object.assign(p, { configured: true, key_source: "runtime", key_fingerprint: "sha256:0badc0de" })
        return json(status)
      },
      "PUT /api/admin/ai": (_url, init) => {
        const body = JSON.parse(String(init?.body)) as { provider: string }
        status = { ...status, active_provider: body.provider, ai_configured: true }
        for (const p of status.providers) p.active = p.name === body.provider
        return json(status)
      },
    })
    const { queryClient } = await renderApp("/admin")

    const anthropic = (await screen.findByRole("heading", { level: 4, name: "Anthropic" })).closest("li")!
    await user.click(within(anthropic).getByRole("button", { name: "Add key for Anthropic" }))
    await user.type(within(anthropic).getByLabelText("Anthropic API key"), SECRET)
    await user.click(within(anthropic).getByRole("button", { name: "Save key" }))

    await waitFor(() => expect(api.count("PUT", "/api/admin/ai")).toBe(1))
    expect(api.calls.find((c) => c.method === "PUT" && c.url.pathname === "/api/admin/ai")?.body).toEqual({
      provider: "anthropic",
    })
    expect(await screen.findByText("Anthropic key saved. Searches now use Anthropic.")).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole("heading", { name: "Turn on AI" })).not.toBeInTheDocument())
    for (const mutation of queryClient.getMutationCache().getAll()) {
      expect(mutation.state.variables).toBeUndefined()
      expect(JSON.stringify(mutation.state)).not.toContain(SECRET)
    }
  })
})
