import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"
import type { HealthResponse } from "@/api/types"
import { adminStatus, fixtures } from "@/test/fixtures"
import { fakeApi, json } from "@/test/fake-api"
import { renderApp } from "@/test/render"

const health = (change: Partial<HealthResponse>) => ({ ...fixtures.health(), ...change })

describe("AI is off note", () => {
  it("says how to turn AI on either way: .env, or Admin on a local Development API", async () => {
    fakeApi({
      "GET /api/health": () => json(health({ ai_configured: false, environment: "Development" })),
      "GET /api/admin/ai": () => json(adminStatus()),
    })
    const { router } = await renderApp("/")
    expect(await screen.findByText(/AI is off, so searches use basic matching/)).toHaveTextContent(
      "set GEMINI_API_KEY in .env and restart, or add a key in Admin."
    )
    await userEvent.click(screen.getByRole("link", { name: "add a key in Admin" }))
    expect(router.state.location.pathname).toBe("/admin")
  })

  it("points only to .env where Admin isn't available", async () => {
    fakeApi({ "GET /api/health": () => json(health({ ai_configured: false, environment: "Production" })) })
    await renderApp("/")
    expect(await screen.findByText(/AI is off/)).toHaveTextContent("set GEMINI_API_KEY in .env and restart.")
    expect(screen.queryByRole("link", { name: "add a key in Admin" })).not.toBeInTheDocument()
  })

  it("names the key of a provider that was chosen explicitly", async () => {
    fakeApi({ "GET /api/health": () => json(health({ ai_configured: false, ai_provider: "anthropic" })) })
    await renderApp("/")
    expect(await screen.findByText(/AI is off/)).toHaveTextContent("set ANTHROPIC_API_KEY in .env")
  })

  it("stays quiet when AI is on", async () => {
    fakeApi({ "GET /api/health": () => json(health({ ai_configured: true })) })
    await renderApp("/")
    await screen.findByRole("heading", { level: 1 })
    await new Promise((r) => setTimeout(r, 20))
    expect(screen.queryByText(/AI is off/)).not.toBeInTheDocument()
  })
})
