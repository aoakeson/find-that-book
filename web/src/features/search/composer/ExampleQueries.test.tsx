import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { expect, it } from "vitest"
import { fakeApi } from "@/test/fake-api"
import { renderApp } from "@/test/render"
import { SEARCH_EXAMPLE_GROUPS, STILL_MISSED_TOPICS } from "./example-catalog"

it("offers the brief's three examples; clicking one fills and focuses the composer without calling the API", async () => {
  const api = fakeApi({})
  const user = userEvent.setup()
  await renderApp("/")

  const examples = within(screen.getByRole("group", { name: "Examples" })).getAllByRole("button")
  expect(examples.map((b) => b.textContent)).toEqual([
    "tolkien hobbit illustrated deluxe 1937",
    "mark huckleberry",
    "tale two cities",
  ])

  await user.click(screen.getByRole("button", { name: "mark huckleberry" }))

  const composer = screen.getByRole("combobox", { name: "Describe the book" })
  expect(composer).toHaveValue("mark huckleberry")
  await waitFor(() => expect(composer).toHaveFocus())
  expect(screen.queryByRole("listbox")).not.toBeInTheDocument()
  // Longer than the suggestion debounce: an example neither searches nor asks for suggestions.
  await act(() => new Promise<void>((resolve) => setTimeout(resolve, 250)))
  expect(api.calls).toHaveLength(0)
})

it("keeps the longer catalog closed, then fills from a chip without searching", async () => {
  const api = fakeApi({})
  const user = userEvent.setup()
  await renderApp("/")

  expect(screen.queryByRole("heading", { name: "Just a title" })).not.toBeInTheDocument()
  await user.click(screen.getByRole("button", { name: "More things you can search for" }))

  const characters = screen.getByRole("group", { name: "Characters" })
  const holden = within(characters).getByRole("button", { name: "holden caulfield, Needs AI" })
  expect(holden).toHaveTextContent("Needs AI")
  await user.click(holden)

  const composer = screen.getByRole("combobox", { name: "Describe the book" })
  expect(composer).toHaveValue("holden caulfield")
  await waitFor(() => expect(composer).toHaveFocus())
  expect(screen.getByRole("heading", { name: "Characters" })).toBeVisible()

  await user.click(within(screen.getByRole("group", { name: "Other languages and scripts" })).getByRole("button", { name: "三体" }))
  expect(composer).toHaveValue("三体")

  await act(() => new Promise<void>((resolve) => setTimeout(resolve, 250)))
  expect(api.calls).toHaveLength(0)
})

it("marks only the searches that need the AI, and does not offer the known misses as buttons", async () => {
  fakeApi({})
  const user = userEvent.setup()
  await renderApp("/")
  await user.click(screen.getByRole("button", { name: "More things you can search for" }))

  for (const group of SEARCH_EXAMPLE_GROUPS) {
    const region = screen.getByRole("group", { name: group.title })
    for (const example of group.queries) {
      const name = example.needsAi ? `${example.query}, Needs AI` : example.query
      const button = within(region).getByRole("button", { name, exact: true })
      if (example.needsAi) expect(button).toHaveTextContent("Needs AI")
      else expect(button).not.toHaveTextContent("Needs AI")
    }
  }

  const missed = screen.getByText(/These still miss, even with the AI on/)
  for (const query of STILL_MISSED_TOPICS) {
    expect(missed).toHaveTextContent(query)
    expect(screen.queryByRole("button", { name: query, exact: true })).not.toBeInTheDocument()
  }
})
