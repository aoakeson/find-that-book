import { createContext, useContext } from "react"

/** True inside RootLayout. Error/404 screens rendered outside it (the root boundary) draw a minimal shell themselves. */
export const ShellContext = createContext(false)

export function useInShell(): boolean {
  return useContext(ShellContext)
}
