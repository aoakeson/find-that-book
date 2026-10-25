import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { expect, it } from "vitest"
import { fakeApi } from "@/test/fake-api"
import { renderApp } from "@/test/render"

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
