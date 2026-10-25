import { useEffect } from "react"

/** "Admin" → "Admin – Find That Book". Pass null for the plain product name. */
export function useDocumentTitle(title: string | null) {
  useEffect(() => {
    document.title = title ? `${title} – Find That Book` : "Find That Book"
  }, [title])
}
