import { Link } from "@tanstack/react-router"
import { SparklesIcon } from "lucide-react"
import { useHealth } from "@/api/health"

const KEY_VARIABLES: Record<string, string> = {
  anthropic: "ANTHROPIC_API_KEY",
  openai: "OPENAI_API_KEY",
}

/**
 * Shown only when the API reports no usable AI key. Searching still works (basic matching, standard explanations);
 * this says how to turn AI on either way: a .env key, or (on a local Development API) the Admin page.
 */
export function AiOffNote({ className }: { className?: string }) {
  const { data } = useHealth()
  if (!data || data.ai_configured !== false) return null
  const adminHere = data.environment === "Development"
  // A provider chosen explicitly needs its own key; otherwise any key works, and Gemini's is free.
  const variable = KEY_VARIABLES[data.ai_provider ?? ""] ?? "GEMINI_API_KEY"
  return (
    <p className={className}>
      <SparklesIcon aria-hidden="true" className="mr-1.5 inline size-4 align-[-0.15em] text-ink-secondary" />
      AI is off, so searches use basic matching. To turn it on, set <code className="font-medium">
        {variable}
      </code> in <code className="font-medium">.env</code> and restart
      {adminHere ? (
        <>
          , or{" "}
          <Link to="/admin" className="font-medium text-teal underline underline-offset-4 hover:text-teal-hover">
            add a key in Admin
          </Link>
          .
        </>
      ) : (
        "."
      )}
    </p>
  )
}
