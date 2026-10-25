import { useState } from "react"
import { BookIcon } from "lucide-react"
import type { SuggestionItem } from "@/api/types"
import { AutocompleteItem } from "@/components/ui/autocomplete"
import { safeExternalUrl } from "@/lib/format"

/** What accepting a suggestion puts in the composer: enough for a precise search, still editable. */
export function suggestionText(item: SuggestionItem): string {
  return item.author ? `${item.title} ${item.author}` : item.title
}

/**
 * The option's name is its visible text (so voice control can say what it sees); the hidden words only make it read
 * as a sentence: "Title by Author, first published 1937".
 */
export function SuggestionOption({ item }: { item: SuggestionItem }) {
  return (
    <AutocompleteItem value={item}>
      <CoverThumb url={item.cover_url} />
      {/* Spaces as their own text nodes: name computation trims each element's text, so " by " alone would glue. */}
      <span className="min-w-0 flex-1 leading-snug">
        <span className="block font-medium wrap-break-word">{item.title}</span>
        {item.author && (
          <>
            {" "}
            <span className="block text-sm wrap-break-word text-muted-foreground">
              <span className="sr-only">by</span> {item.author}
            </span>
          </>
        )}
      </span>
      {item.first_publish_year !== null && (
        <span className="shrink-0 self-start pt-0.5 text-sm text-muted-foreground">
          <span className="sr-only">, first published</span> {item.first_publish_year}
        </span>
      )}
    </AutocompleteItem>
  )
}

function CoverThumb({ url }: { url: string | null }) {
  const src = safeExternalUrl(url)
  const [failed, setFailed] = useState(false)
  return (
    <span
      aria-hidden
      className="relative flex h-12 w-8 shrink-0 items-center justify-center overflow-hidden rounded-sm bg-mist"
    >
      <BookIcon className="size-4 text-muted-foreground" />
      {src && !failed && (
        <img
          src={src}
          alt=""
          width={32}
          height={48}
          loading="lazy"
          decoding="async"
          referrerPolicy="no-referrer"
          onError={() => setFailed(true)}
          className="absolute inset-0 size-full object-cover"
        />
      )}
    </span>
  )
}
