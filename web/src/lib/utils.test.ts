import { expect, it } from "vitest"
import { cn } from "./utils"

it.each([
  [["text-muted-foreground", "text-caption"], "text-muted-foreground text-caption"],
  [["text-sm", "text-caption"], "text-caption"],
  [["shadow-sm", "shadow-panel"], "shadow-panel"],
  [["rounded-lg", "rounded-composer"], "rounded-composer"],
])("merges custom tokens by their real group: %s", (input, expected) => {
  expect(cn(...input)).toBe(expected)
})
