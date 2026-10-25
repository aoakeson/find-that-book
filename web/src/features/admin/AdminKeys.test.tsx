import { act, fireEvent, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { QueryClient } from "@tanstack/react-query"
import { describe, expect, it } from "vitest"
import { deferred, fakeApi, json, problem } from "@/test/fake-api"
import { renderApp } from "@/test/render"
import { adminStatus, keyRejected, probeResult, providerIn } from "./admin.test-data"

const SECRET = "plainly-fake-test-secret-for-gemini"

function keyRow(title: string): HTMLElement {
  const row = screen.getByRole("heading", { level: 4, name: title }).closest("li")
  if (!row) throw new Error(`no row for ${title}`)
  return row
}

const geminiAtRuntime = () =>
  adminStatus((s) => {
    providerIn(s, "gemini").key_source = "runtime"
    providerIn(s, "gemini").key_fingerprint = "sha256:0badc0de"
  })

/** Everything TanStack keeps: mutation variables and state, and every query's state. */
function expectNoSecretInCaches(queryClient: QueryClient) {
  const mutations = queryClient.getMutationCache().getAll()
  for (const mutation of mutations) {
    expect(mutation.state.variables).toBeUndefined()
    expect(JSON.stringify(mutation.state)).not.toContain(SECRET)
    expect(JSON.stringify(mutation.options.mutationKey)).not.toContain(SECRET)
  }
  for (const query of queryClient.getQueryCache().getAll()) {
    expect(JSON.stringify(query.state)).not.toContain(SECRET)
  }
}

function expectNoSecretOnPage() {
  for (const input of document.querySelectorAll("input")) expect(input.value).not.toContain(SECRET)
  expect(document.documentElement.outerHTML).not.toContain(SECRET)
  expect(window.location.href).not.toContain(SECRET)
  expect(JSON.stringify({ ...window.localStorage })).not.toContain(SECRET)
  expect(JSON.stringify({ ...window.sessionStorage })).not.toContain(SECRET)
}

describe("Admin keys", () => {
  it("saves a key, shows the new status and keeps the key out of every cache", async () => {
    const user = userEvent.setup()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai/keys/gemini": () => json(geminiAtRuntime()),
    })
    const { queryClient, router } = await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Replace key for Gemini" }))
    const input = screen.getByLabelText("Gemini API key")
    expect(input).toHaveFocus()
    expect(input).toHaveAttribute("type", "password")
    expect(input).toHaveAttribute("autocomplete", "off")
    expect(input).toHaveAttribute("spellcheck", "false")
    expect(input).toHaveAttribute("maxlength", "512")
    expect(input).not.toHaveAttribute("name")

    await user.type(input, SECRET)
    await user.click(screen.getByRole("button", { name: "Save key" }))

    const gemini = keyRow("Gemini")
    expect(await within(gemini).findByText("Set here until the API restarts")).toBeInTheDocument()
    expect(within(gemini).getByText("Fingerprint sha256:0badc0de")).toBeInTheDocument()
    expect(screen.queryByLabelText("Gemini API key")).not.toBeInTheDocument()
    expect(input).toHaveValue("") // the detached input was emptied too
    await waitFor(() => expect(within(gemini).getByRole("button", { name: "Replace key for Gemini" })).toHaveFocus())

    // The key went out exactly once, in the request body, and nowhere else.
    expect(api.count("PUT", "/api/admin/ai/keys/gemini")).toBe(1)
    expect(api.calls.find((c) => c.method === "PUT")?.body).toEqual({ api_key: SECRET })
    expect(api.calls.every((c) => !c.url.href.includes(SECRET))).toBe(true)
    expect(queryClient.getMutationCache().getAll().length).toBeGreaterThan(0)
    expectNoSecretInCaches(queryClient)
    expectNoSecretOnPage()
    expect(router.state.location.href).not.toContain(SECRET)
  })

  it("shows the API's field error under the input and keeps the typed key so it can be fixed", async () => {
    const user = userEvent.setup()
    fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai/keys/anthropic": () => problem(keyRejected()),
    })
    const { queryClient } = await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Add key for Anthropic" }))
    const input = screen.getByLabelText("Anthropic API key")
    await user.type(input, "too short")
    await user.click(screen.getByRole("button", { name: "Save key" }))

    const error = await screen.findByText("A key has no spaces or control characters.")
    expect(input).toHaveAttribute("aria-invalid", "true")
    expect(input.getAttribute("aria-describedby")?.split(" ")).toContain(error.closest("[id]")?.id)
    expect(input).toHaveValue("too short")
    expect(input).toHaveFocus()
    expect(within(keyRow("Anthropic")).getByText("Not set")).toBeInTheDocument()
    for (const mutation of queryClient.getMutationCache().getAll()) expect(mutation.state.variables).toBeUndefined()

    // Typing again clears the error; the key stays.
    await user.type(input, "x")
    expect(input).not.toHaveAttribute("aria-invalid")
    expect(screen.queryByText("A key has no spaces or control characters.")).not.toBeInTheDocument()
  })

  it("asks for a key before sending an empty one", async () => {
    const user = userEvent.setup()
    const api = fakeApi({ "GET /api/admin/ai": () => json(adminStatus()) })
    await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Add key for Anthropic" }))
    await user.type(screen.getByLabelText("Anthropic API key"), "   {Enter}")
    expect(screen.getByText("Paste the key first.")).toBeInTheDocument()
    expect(api.calls.filter((c) => c.method !== "GET")).toHaveLength(0)
  })

  it("Cancel empties the key and closes the form", async () => {
    const user = userEvent.setup()
    const api = fakeApi({ "GET /api/admin/ai": () => json(adminStatus()) })
    await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Replace key for Gemini" }))
    const input = screen.getByLabelText("Gemini API key")
    await user.type(input, SECRET)
    await user.click(screen.getByRole("button", { name: "Cancel" }))

    expect(screen.queryByLabelText("Gemini API key")).not.toBeInTheDocument()
    expect(input).toHaveValue("")
    expect(screen.getByRole("button", { name: "Replace key for Gemini" })).toHaveFocus()
    await user.click(screen.getByRole("button", { name: "Replace key for Gemini" }))
    expect(screen.getByLabelText("Gemini API key")).toHaveValue("")
    expect(api.calls.filter((c) => c.method !== "GET")).toHaveLength(0)
  })

  it("leaving the page empties a typed key", async () => {
    const user = userEvent.setup()
    fakeApi({ "GET /api/admin/ai": () => json(adminStatus()) })
    const { router } = await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Replace key for Gemini" }))
    const input = screen.getByLabelText("Gemini API key")
    await user.type(input, SECRET)

    await act(() => router.navigate({ to: "/" }))
    expect(input).not.toBeInTheDocument()
    expect(input).toHaveValue("")

    await act(() => router.navigate({ to: "/admin" }))
    await user.click(await screen.findByRole("button", { name: "Replace key for Gemini" }))
    expect(screen.getByLabelText("Gemini API key")).toHaveValue("")
    expectNoSecretOnPage()
  })

  it("sends one request when Save key is clicked twice or Enter is pressed during a save", async () => {
    const user = userEvent.setup()
    const pending = deferred()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "PUT /api/admin/ai/keys/gemini": pending.handler,
    })
    await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Replace key for Gemini" }))
    await user.type(screen.getByLabelText("Gemini API key"), SECRET)
    const save = screen.getByRole("button", { name: "Save key" })
    // Two clicks in the same task, before React re-renders the button as busy, then Enter while it is busy.
    act(() => {
      fireEvent.click(save)
      fireEvent.click(save)
    })
    await user.type(screen.getByLabelText("Gemini API key"), "{Enter}")
    expect(screen.getByRole("button", { name: "Saving…" })).toHaveAttribute("aria-disabled", "true")
    expect(api.count("PUT", "/api/admin/ai/keys/gemini")).toBe(1)

    pending.resolve(json(geminiAtRuntime()))
    expect(await within(keyRow("Gemini")).findByText("Set here until the API restarts")).toBeInTheDocument()
    expect(api.count("PUT", "/api/admin/ai/keys/gemini")).toBe(1)
  })

  it("removes a runtime key only after confirming", async () => {
    const user = userEvent.setup()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(geminiAtRuntime()),
      "DELETE /api/admin/ai/keys/gemini": () => json(adminStatus()),
    })
    await renderApp("/admin")

    const remove = await screen.findByRole("button", { name: "Remove the Gemini key set here" })
    // Only a key set here can be removed.
    expect(within(keyRow("OpenAI")).queryByRole("button", { name: /Remove/ })).not.toBeInTheDocument()

    await user.click(remove)
    const dialog = await screen.findByRole("alertdialog")
    expect(dialog).toHaveTextContent("Remove the Gemini key set here?")
    expect(dialog).toHaveTextContent("The key from configuration applies again, if there is one.")

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }))
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument())
    expect(api.count("DELETE", "/api/admin/ai/keys/gemini")).toBe(0)

    await user.click(screen.getByRole("button", { name: "Remove the Gemini key set here" }))
    await user.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Remove key" }))

    const gemini = keyRow("Gemini")
    expect(await within(gemini).findByText("Key set from .env")).toBeInTheDocument()
    expect(api.count("DELETE", "/api/admin/ai/keys/gemini")).toBe(1)
    expect(within(gemini).queryByRole("button", { name: /Remove/ })).not.toBeInTheDocument()
    await waitFor(() => expect(within(gemini).getByRole("button", { name: "Replace key for Gemini" })).toHaveFocus())
  })

  it("shows the real outcome of a connection test, failure detail included", async () => {
    const user = userEvent.setup()
    let result = probeResult({
      ok: false,
      status: "provider_error",
      elapsed_ms: 212,
      input_tokens: null,
      output_tokens: null,
      detail: "http 401: API key not valid",
    })
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "POST /api/admin/ai/probe/gemini": () => json(result),
    })
    await renderApp("/admin")

    const test = await screen.findByRole("button", { name: "Test connection to Gemini" })
    await user.click(test)
    const gemini = keyRow("Gemini")
    expect(await within(gemini).findByText("The provider returned an error.")).toBeInTheDocument()
    await user.click(within(gemini).getByText("Details from the API"))
    expect(within(gemini).getByText("http 401: API key not valid")).toBeVisible()
    expect(within(gemini).queryByText(/Connected/)).not.toBeInTheDocument()

    result = probeResult()
    await user.click(test)
    expect(await within(gemini).findByText("Connected in 640 ms")).toBeInTheDocument()
    expect(within(gemini).getByText("gemini-3.5-flash-lite, 212 tokens")).toBeInTheDocument()
    expect(api.count("POST", "/api/admin/ai/probe/gemini")).toBe(2)
  })

  it("runs one connection test for a double click", async () => {
    const pending = deferred()
    const api = fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "POST /api/admin/ai/probe/openai": pending.handler,
    })
    await renderApp("/admin")

    const test = await screen.findByRole("button", { name: "Test connection to OpenAI" })
    act(() => {
      fireEvent.click(test)
      fireEvent.click(test)
    })
    expect(await within(keyRow("OpenAI")).findByText("Testing the connection to OpenAI…")).toBeInTheDocument()
    expect(api.count("POST", "/api/admin/ai/probe/openai")).toBe(1)
    pending.resolve(json(probeResult({ provider: "openai", model: "gpt-4.1-mini", elapsed_ms: 1300 })))
    expect(await within(keyRow("OpenAI")).findByText("Connected in 1.3 s")).toBeInTheDocument()
  })

  it("shows a failed test call as an error, not as a provider result", async () => {
    const user = userEvent.setup()
    fakeApi({
      "GET /api/admin/ai": () => json(adminStatus()),
      "POST /api/admin/ai/probe/gemini": () => {
        throw new TypeError("Failed to fetch")
      },
    })
    await renderApp("/admin")

    await user.click(await screen.findByRole("button", { name: "Test connection to Gemini" }))
    expect(await within(keyRow("Gemini")).findByRole("alert")).toHaveTextContent(
      "Couldn't reach the API. Check that it's running, then try again."
    )
  })
})
