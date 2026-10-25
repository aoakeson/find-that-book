import { Outlet, useRouterState } from "@tanstack/react-router"
import type { MouseEvent } from "react"
import { Toaster } from "@/components/ui/sonner"
import { TooltipProvider } from "@/components/ui/tooltip"
import { SearchSessionProvider, useSearchSession } from "@/features/search/session"
import { MotionProvider } from "@/lib/motion"
import { AppHeader } from "./AppHeader"
import { MotionToggle } from "./MotionToggle"
import { PaperBackground } from "./PaperBackground"
import { ShellContext } from "./shell-context"
import { useRouteFocus } from "./use-route-focus"

export function RootLayout() {
  useRouteFocus()
  return (
    <SearchSessionProvider>
      <MotionProvider>
        <TooltipProvider delay={300}>
          <ShellContext.Provider value>
            <a
              href="#main"
              onClick={skipToMain}
              className="fixed top-2 left-2 z-50 -translate-y-24 rounded-lg bg-surface px-4 py-2.5 text-sm font-medium text-foreground ring-1 ring-line focus:translate-y-0"
            >
              Skip to main content
            </a>
            <ShellBackground />
            <div className="relative z-10 flex min-h-svh flex-col">
              <AppHeader />
              <main id="main" tabIndex={-1} className="flex min-w-0 flex-1 flex-col">
                <Outlet />
              </main>
              {/* In the page flow below lg: only there is the gutter beside the content wide enough for a fixed corner. */}
              <footer className="mx-auto flex w-full max-w-5xl justify-end px-4 pb-4 sm:px-6 lg:p-0">
                <MotionToggle className="lg:fixed lg:right-5 lg:bottom-5 lg:z-20" />
              </footer>
            </div>
            {/* Bottom: the composer sits at the top half of the page, so toasts never cover it on phones. */}
            <Toaster position="bottom-center" mobileOffset={{ bottom: "calc(1rem + env(safe-area-inset-bottom))" }} />
          </ShellContext.Provider>
        </TooltipProvider>
      </MotionProvider>
    </SearchSessionProvider>
  )
}

function ShellBackground() {
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const { view } = useSearchSession()
  return <PaperBackground quiet={!(pathname === "/" && view.kind === "home")} />
}

/** In-page jump without letting the router treat #main as a navigation. */
function skipToMain(event: MouseEvent<HTMLAnchorElement>) {
  const main = document.getElementById("main")
  if (!main) return
  event.preventDefault()
  main.focus({ preventScroll: true })
  main.scrollIntoView({ block: "start" })
}
