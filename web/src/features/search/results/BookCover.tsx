import { useState } from "react"
import { AspectRatio } from "@/components/ui/aspect-ratio"
import { safeExternalUrl } from "@/lib/format"
import { cn } from "@/lib/utils"

export type CoverSize = "lg" | "md" | "sm"

const WIDTH: Record<CoverSize, string> = {
  lg: "w-20 sm:w-28",
  md: "w-16 sm:w-20",
  sm: "w-12 sm:w-14",
}

const LETTER: Record<CoverSize, string> = {
  lg: "text-2xl sm:text-4xl",
  md: "text-xl sm:text-2xl",
  sm: "text-base sm:text-xl",
}

/** A fixed 2:3 box, so a cover that loads late, fails or doesn't exist never moves the text beside it. */
export function BookCover({
  url,
  title,
  size,
  className,
}: {
  url: string | null
  title: string
  size: CoverSize
  className?: string
}) {
  const src = safeExternalUrl(url)
  return (
    <div className={cn("shrink-0", WIDTH[size], className)}>
      <AspectRatio ratio={2 / 3} className="overflow-hidden rounded-md border border-line bg-mist">
        {src ? (
          <CoverImage key={src} src={src} title={title} size={size} />
        ) : (
          <CoverPlaceholder title={title} size={size} />
        )}
      </AspectRatio>
    </div>
  )
}

function CoverImage({ src, title, size }: { src: string; title: string; size: CoverSize }) {
  const [failed, setFailed] = useState(false)
  if (failed) return <CoverPlaceholder title={title} size={size} />
  return (
    // Decorative: the title is the heading right beside it, so a screen reader shouldn't hear it twice.
    <img
      src={src}
      alt=""
      loading="lazy"
      decoding="async"
      referrerPolicy="no-referrer"
      className="absolute inset-0 size-full object-cover"
      onError={() => setFailed(true)}
      // Open Library answers some missing covers with a 1×1 pixel instead of an error.
      onLoad={(e) => {
        if (e.currentTarget.naturalWidth <= 1) setFailed(true)
      }}
    />
  )
}

/** A pale paper book with the title's first letter: designed, not a "No cover" box. */
function CoverPlaceholder({ title, size }: { title: string; size: CoverSize }) {
  return (
    <div
      aria-hidden="true"
      data-cover-placeholder=""
      className="absolute inset-0 flex items-center justify-center overflow-hidden bg-sage"
    >
      <span className="absolute inset-y-0 left-0 w-[14%] border-r border-line bg-teal/10" />
      <span className="absolute inset-y-[6%] right-0 w-[5%] border-y border-l border-line bg-surface" />
      <span className={cn("pl-[10%] font-heading leading-none font-semibold text-teal/80", LETTER[size])}>
        {initial(title)}
      </span>
    </div>
  )
}

function initial(title: string): string {
  const match = title.match(/[\p{L}\p{N}]/u)
  return match ? match[0].toLocaleUpperCase() : ""
}
