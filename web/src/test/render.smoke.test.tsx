import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { expect, it } from "vitest"
import { fixtures } from "./fixtures"
import { fakeApi, json } from "./fake-api"
import { renderApp } from "./render"

it("renders the app, searches, and records the entry in the URL", async () => {
  const api = fakeApi({ "GET /api/books/search": () => json(fixtures.match()) })
  const { router } = await renderApp("/")
  await userEvent.type(screen.getByLabelText("Describe the book"), "tolkien hobbit{Enter}")
  await waitFor(() => expect(router.state.location.search).toHaveProperty("entry"))
  expect(api.count("GET", "/api/books/search")).toBe(1)
})
