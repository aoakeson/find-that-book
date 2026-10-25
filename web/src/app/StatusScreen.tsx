import type { ReactNode } from "react"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia } from "@/components/ui/empty"
import { MotionProvider } from "@/lib/motion"
import { cn } from "@/lib/utils"
import { BrandLink } from "./AppHeader"
import { PaperBackground } from "./PaperBackground"
import { useInShell } from "./shell-context"

interface StatusScreenProps {
  icon: ReactNode
  /** The page's h1 (route focus lands on it). */
  title: string
  description: ReactNode
  actions: ReactNode
  tone?: "neutral" | "danger"
  children?: ReactNode
}

/** Full-page states (404, error boundary): same column, type and background as the search page, one clear way out. */
export function StatusScreen({ icon, title, description, actions, tone = "neutral", children }: StatusScreenProps) {
  const content = (
    <div className="mx-auto flex w-full max-w-3xl flex-col px-4 pt-[clamp(2.5rem,14vh,9rem)] pb-16 sm:px-6">
      <Empty className="gap-6 p-0">
        <EmptyHeader className="max-w-md gap-3">
          <EmptyMedia
            variant="icon"
            className={cn(
              "mb-1 size-10 rounded-lg [&_svg:not([class*='size-'])]:size-5",
              tone === "danger" ? "bg-danger-surface text-danger" : "bg-sage text-teal"
            )}
          >
            {icon}
          </EmptyMedia>
          <h1 id="page-heading" tabIndex={-1} className="font-heading text-heading font-semibold tracking-tight">
            {title}
          </h1>
          <EmptyDescription className="text-base text-ink-secondary">{description}</EmptyDescription>
        </EmptyHeader>
        <EmptyContent className="flex-row flex-wrap justify-center gap-3">{actions}</EmptyContent>
        {children}
      </Empty>
    </div>
  )
  return useInShell() ? content : <MinimalShell>{content}</MinimalShell>
}

/** For screens rendered by the root boundary, where RootLayout (and its providers) isn't on screen. */
function MinimalShell({ children }: { children: ReactNode }) {
  return (
    <MotionProvider>
      <PaperBackground />
      <div className="relative z-10 flex min-h-svh flex-col">
        <header>
          <div className="mx-auto flex h-16 w-full max-w-5xl items-center px-4 sm:px-6">
            <BrandLink />
          </div>
        </header>
        <main id="main" tabIndex={-1} className="flex-1">
          {children}
        </main>
      </div>
    </MotionProvider>
  )
}
