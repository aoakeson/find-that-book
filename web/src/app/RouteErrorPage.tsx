import { Link, type ErrorComponentProps } from "@tanstack/react-router"
import { TriangleAlertIcon } from "lucide-react"
import { useEffect, useState } from "react"
import { Button, buttonVariants } from "@/components/ui/button"
import { useDocumentTitle } from "@/lib/use-document-title"
import { cn } from "@/lib/utils"
import { useInShell } from "./shell-context"
import { StatusScreen } from "./StatusScreen"

/**
 * The router's error boundary. Shows plain words and a short reference only: never the error's message or stack,
 * which can carry API or internal detail. The reference is logged with the error so the two can be matched.
 */
export function RouteErrorPage({ error, reset }: ErrorComponentProps) {
  const [reference] = useState(createReference)
  // Outside the shell the layout itself failed: a full page load is the most reliable way back.
  const inShell = useInShell()
  useDocumentTitle("Something went wrong")

  useEffect(() => {
    console.error(`Find That Book: unexpected error, reference ${reference}`, error)
  }, [error, reference])

  return (
    <StatusScreen
      tone="danger"
      icon={<TriangleAlertIcon />}
      title="Something went wrong"
      description="This page stopped working. Try again, or go back to search."
      actions={
        <>
          <Button size="lg" onClick={reset}>
            Try again
          </Button>
          <Link to="/" reloadDocument={!inShell} className={cn(buttonVariants({ variant: "secondary", size: "lg" }))}>
            Back to search
          </Link>
        </>
      }
    >
      <p className="text-caption text-ink-secondary">
        Reference <span className="font-mono tracking-wide text-foreground">{reference}</span>
      </p>
    </StatusScreen>
  )
}

function createReference(): string {
  const bytes = new Uint8Array(4)
  if (typeof crypto !== "undefined" && typeof crypto.getRandomValues === "function") crypto.getRandomValues(bytes)
  else bytes.forEach((_, i) => (bytes[i] = Math.floor(Math.random() * 256)))
  return `FTB-${Array.from(bytes, (b) => b.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase()}`
}
