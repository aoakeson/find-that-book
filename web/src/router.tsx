import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
  type RouterHistory,
} from "@tanstack/react-router"
import { NotFoundPage } from "@/app/NotFoundPage"
import { RootLayout } from "@/app/RootLayout"
import { RouteErrorPage } from "@/app/RouteErrorPage"
import { ENTRY_ID_PATTERN } from "@/features/history/storage"
import { SearchPage } from "@/features/search/SearchPage"

export interface SearchRouteSearch {
  /** A local history entry id. Snapshots live in this browser only, so these links are not shareable. */
  entry?: string
}

const rootRoute = createRootRoute({
  component: RootLayout,
  notFoundComponent: NotFoundPage,
  errorComponent: RouteErrorPage,
})

const searchRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  validateSearch: (search: Record<string, unknown>): SearchRouteSearch => {
    const entry = typeof search.entry === "string" ? search.entry.toLowerCase() : undefined
    // `undefined` (not a missing key) is what removes a bad value: the router merges the raw params otherwise.
    return { entry: entry && ENTRY_ID_PATTERN.test(entry) ? entry : undefined }
  },
  component: SearchPage,
})

const adminRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/admin",
  // Admin is a local, occasional page: keep it (and its form controls) out of the search page's bundle.
  component: lazyRouteComponent(() => import("@/features/admin/AdminPage"), "AdminPage"),
})

const routeTree = rootRoute.addChildren([searchRoute, adminRoute])

/** Tests pass a memory history; the app uses the browser's. */
export function createAppRouter(history?: RouterHistory) {
  return createRouter({
    routeTree,
    history,
    scrollRestoration: true,
    // Pages load nothing up front, and a search must never start from hovering a link.
    defaultPreload: false,
    // A page that throws is replaced inside the layout, so the header and History stay usable.
    defaultErrorComponent: RouteErrorPage,
  })
}

export const router = createAppRouter()

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router
  }
}
