import { useId, type ReactNode } from "react"

/** One titled area of the Admin page. Each section owns its data and its own loading/error states. */
export function AdminSection({
  title,
  description,
  children,
}: {
  title: string
  description?: ReactNode
  children: ReactNode
}) {
  const headingId = useId()
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 id={headingId} className="font-heading text-title font-semibold">
          {title}
        </h2>
        {description && <p className="max-w-measure text-ink-secondary">{description}</p>}
      </div>
      {children}
    </section>
  )
}

/** A bordered block inside a section, with its own small heading (not a landmark: one region per section is enough). */
export function AdminPanel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-4 rounded-panel border border-line bg-surface p-4 sm:p-6">
      <h3 className="font-heading text-base font-semibold">{title}</h3>
      {children}
    </div>
  )
}
