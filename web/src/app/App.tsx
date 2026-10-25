import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { RouterProvider } from "@tanstack/react-router"
import { useState } from "react"
import type { createAppRouter } from "@/router"

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      // Explicit retries only: the API already retries Open Library, and stacked retries multiply paid calls.
      queries: { retry: false, refetchOnWindowFocus: false, refetchOnReconnect: false, staleTime: 60_000 },
      mutations: { retry: false },
    },
  })
}

export function App({
  router,
  queryClient,
}: {
  router: ReturnType<typeof createAppRouter>
  queryClient?: QueryClient
}) {
  const [client] = useState(() => queryClient ?? createQueryClient())
  return (
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
