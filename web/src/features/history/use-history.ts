import { useSyncExternalStore } from "react"
import { historyStore, type HistoryState } from "./storage"

export function useHistory(): HistoryState {
  return useSyncExternalStore(historyStore.subscribe, historyStore.getSnapshot, historyStore.getSnapshot)
}
