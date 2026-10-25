# Find That Book: design guide

How the web app looks, moves and speaks, and why. Tokens live once in `web/src/index.css`; this file explains them.
If code and this guide disagree, fix one of them in the same change.

## 1. Principles

1. **One task, one place.** A single composer is the page. Results, history and admin exist to serve it; nothing
   competes with it (no marketing sections, dashboard stats, sidebars or model pickers on the homepage).
2. **Show the backend's decisions, never invent them.** The API ranks, labels status, assigns author roles and writes
   explanations. The UI renders them in server order and never re-ranks, guesses confidence, or fabricates a year,
   translation, author role or explanation. Missing data gets an honest fallback, not a plausible guess.
3. **Calm by default, bold in one place.** Everything is quiet: pale surfaces, hairline borders, one accent colour.
   The boldness is spent on the paper background and the page-turn book while a search runs.
4. **Readable first.** Legible type, ≥ 4.5:1 text contrast on every surface it sits on, 16px editable text,
   natural page scrolling, no hover-only information.
5. **State in words.** Every state is said in plain text with an icon; colour only reinforces it.

## 2. Colour

Light theme only (no dark mode in scope). Contrast is WCAG 2.x, measured on the backgrounds each colour is actually
used on.

| Token (Tailwind) | Hex | Role | Contrast on paper / surface / mist / sage |
|---|---|---|---|
| `paper` (`bg-background`) | `#FAFCFC` | Page background | n/a |
| `surface` (`bg-card`, `bg-popover`) | `#FFFFFF` | Composer, result panels, sheets, menus | n/a |
| `mist` (`bg-muted`, `bg-secondary`) | `#F0F6F7` | Quiet fills: skeletons, secondary buttons, hover | n/a |
| `sage` (`bg-accent`) | `#EAF3EF` | Accent fills: status icon tiles, success surface | n/a |
| `ink` (`text-foreground`) | `#283D43` | Body text, headings, titles | 11.09 / 11.42 / 10.46 / 10.10 |
| `ink-secondary` (`text-muted-foreground`) | `#5C7077` | Meta text, descriptions, captions, idle icons | 5.05 / 5.20 / 4.76 / 4.60 |
| `teal` (`bg-primary`, `text-teal`) | `#3D706A` | Primary action, links, focus ring, active book | 5.48 / 5.64 / 5.16 / 4.98 |
| `teal-hover` | `#315E59` | Primary hover | 7.09 / 7.30 / 6.69 / 6.46 |
| `line-strong` (`border-input`) | `#7B8E93` | Outlines of editable controls (non-text, needs 3:1) | 3.33 / 3.42 / 3.14 / 3.03 |
| `line` (`border-border`) | `#E1E9E9` | Hairline dividers and panel borders (decorative only) | 1.20 / 1.23 / 1.13 / 1.09 |

White on teal (primary buttons) is 5.64:1, on teal-hover 7.30:1. Tooltips are paper on ink (11.09:1).

**Why `#5C7077`, not the brief's `#60747B`:** `#60747B` measures 4.76:1 on paper but only 4.49:1 on mist and 4.34:1 on
sage, so secondary text inside a skeleton, a secondary button or a sage tile would fail AA. Darkening it by about 4%
lifts the worst case to 4.60:1 without changing its character.

**`line` is never the only boundary of something you must find or operate.** Editable controls use `line-strong`
(≥ 3:1). Panels may use `line` because their content, not their edge, identifies them.

### State colours

| State | Text / icon | Surface | Border | Text on its surface |
|---|---|---|---|---|
| Danger (failed search, destructive action) | `text-danger` `#A8321F` | `bg-danger-surface` `#FDF1EE` | `border-danger-line` `#F1C9C0` | 6.05:1 (ink 10.33, ink-secondary 4.70) |
| Warning (degraded answer, history unavailable) | `text-warning` `#7A5200` | `bg-warning-surface` `#FBF3E2` | `border-warning-line` `#ECD9AD` | 6.27:1 (ink 10.34, ink-secondary 4.71) |
| Success / match | teal | sage | none | 4.98:1 |

**Never colour alone.** Every state pairs its colour with an icon and words ("Best match", "Search stopped",
"Open Library is unavailable"). A red border without a sentence is a bug. The state lines are decorative (1.3:1).

**Disabled.** No colour of its own: a disabled control keeps its colours at 50% opacity (`disabled:` and
`aria-disabled:` in `button`/`input`), and a control that is only waiting (Try again during a Retry-After, Save while a
save runs) stays focusable with `aria-disabled` and says why next to it. Disabled text is exempt from contrast rules,
so nothing a user must read is ever shown only in a disabled control.

### Focus

One indicator everywhere: a solid 2px `teal` outline with a 2px offset (5.5:1 on paper), set unlayered in
`index.css` so component `outline-none` utilities can't remove it. Input groups show it on the group, not the inner
input. Only the programmatic route-focus targets (`#main`, `#page-heading`) suppress it; they are not controls.

## 3. Typography

- **Family:** Atkinson Hyperlegible Next (variable, self-hosted via `@fontsource-variable`, Latin subset). Designed by
  the Braille Institute for low-vision readers: distinct `Il1`, `O0`, open apertures, which helps with messy
  half-remembered queries, ISBN-like numbers and author initials. Non-Latin text (CJK, Cyrillic, Greek, Arabic…) falls
  through to the system stacks listed in `--font-sans` (system-ui, Noto Sans, Hiragino/PingFang/YaHei, Apple SD Gothic
  Neo), so titles in any script render without tofu. One family for everything; mono only for reference ids.
- **Scale:**

  | Use | Class | Size / line-height | Weight |
  |---|---|---|---|
  | Captions, timestamps, reference ids | `text-caption` | 13 / 1.45 | 400 |
  | Secondary UI text, buttons, badges | `text-sm` | 14 / 1.43 | 400, 500 for labels and buttons |
  | Body, every editable field | `text-base` | 16 / 1.6 (body) | 400 |
  | Result titles | `text-title` | 18 / 1.35 | 600 |
  | Page heading (h1) | `text-heading` | 26 → 32, fluid `clamp()` / 1.2 | 600, `tracking-tight` |

- Editable text is never below 16px (prevents iOS zoom on focus and keeps typed queries legible).
- Headings use `text-balance`; running text stays at about 70 characters (`max-w-measure`, a 33rem token: in this
  font `1ch` is 10.4px, so `70ch` would allow about 95 characters) even inside the 3xl column.
- Sentence case everywhere. No ALL-CAPS labels, no letter-spaced eyebrows.

## 4. Space and layout

- Tailwind's 4px scale (with its 2px half-steps for tight inline gaps such as icon + label): `gap-1/1.5/2/3/4/6/8`, `p-4/6`. No arbitrary pixel spacing except the home heading's
  fluid top offset `pt-[clamp(2.5rem,14vh,9rem)]`, shared by the home and full-page status screens.
- **Gutters:** 16px on phones (`px-4`), 24px from `sm` (`sm:px-6`). Header and content use the same gutter.
- **Columns:** content `max-w-3xl` (768px) centred; the header row is wider (`max-w-5xl`) so actions sit outside the
  reading column on large screens.
- **Layout:** header (64px) → `main#main` (flex-1) → footer (the motion control on phones). The paper background is
  `position: fixed` behind everything (`z-0`); page content is `relative z-10`. One document scroll; no nested
  fixed-height scroll panes. The search page switches from the centred home layout to a compact composer-on-top layout
  once a search starts.

## 5. Radii and elevation

| Element | Radius | Elevation |
|---|---|---|
| Composer | `rounded-composer` 24px | `shadow-composer` (the only soft shadow in the page) |
| Panels: best match, result groups, notices | `rounded-panel` 16px | Border (`line`); `shadow-panel` (1px) at most on the best-match panel |
| Controls: buttons, inputs, chips, menus | `rounded-lg` 12px (default) | None |
| Motion control | `rounded-full` | None |

Borders before shadows. Overlays (Sheet, dropdown menu, alert dialog, popups) keep their component shadow because
they float above content; nothing else in the page gets a shadow or a gradient. The paper background is the only
gradient-like surface, and it is flat fills with hairline edges.

## 6. Motion

| What | How | Duration |
|---|---|---|
| State changes (hover, press, compact layout padding, disclosure) | `transition-*` with `ease-(--ease-out-soft)` | 150–250ms (`duration-150/200/250`) |
| Paper background drift | CSS keyframes, three layers ±1.6% horizontally and 6px vertically, `alternate`, desynchronised | 58s / 74s / 96s per cycle |
| Paper pointer parallax | Two CSS variables written at most once per animation frame; layers move ≤ 6px × 3px with a 1.2s ease | Fine pointers only: `(hover: hover) and (pointer: fine)` |
| Page-turn book | CSS keyframes on one leaf of a 20px open-book icon, only while a search runs | 1.6s loop |
| Trace shimmer | The Trace component's own running shimmer on the activity title | Component default |

Rules:
- Nothing else moves: no entrance animations on result lists, no hover lift, no skeleton shimmer, no spinners
  competing with the book. Real results are never delayed for an animation.
- **Reduced motion** (`prefers-reduced-motion: reduce`): a global kill-switch in `index.css` collapses every animation
  and transition; the paper layers get `animation: none`, parallax is not attached, the book is a still icon and the
  shimmer is off. The motion control is hidden because there is nothing left to pause.
- **Pause control:** a 36px round button (44px on touch) labelled **"Pause background motion"**, a toggle with
  `aria-pressed` (the name stays constant; the icon and tooltip switch between pause and resume). Remembered in
  `localStorage` (`ftb.motionPaused`, failure-safe). Pausing freezes the paper where it is, detaches parallax, stills
  the page-turn book and the Trace shimmer (`html[data-motion="paused"]`). Placement: in the page footer below `lg`,
  fixed bottom-right from `lg` (1024px) up, where the side gutter is wide enough; `scroll-padding-bottom` keeps focused elements clear of it.
- **Hidden tabs:** the paper stops (`animation-play-state: paused`) and the pointer listener is removed while
  `document.visibilityState` is `hidden`.
- Cheap by construction: no animation library, compositor-only transforms, listeners and frames cleaned up on unmount.

## 7. Responsive rules

- Supported: 320px to 1440px+ wide, phone landscape, 200% zoom (behaves like a 640px → 320px viewport). No horizontal
  page overflow at any width: `min-w-0` on flex children, `wrap-break-word`/`break-words` on every user- or API-supplied
  string (titles, authors, queries, ids), `truncate` only where the full text is available elsewhere.
- **Breakpoints actually used:** `sm` (640px): header action labels appear, gutters grow to 24px, the Search button
  shows its word; `md` (768px): the Turn on AI panel puts its two ways side by side; `lg` (1024px): the motion control
  becomes fixed in the corner. Layout otherwise flows because the column caps at 768px. Short viewports
  (`max-height: 32rem`: phone landscape, 200% zoom) get the quiet paper background.
- **Header at 320px:** mark + name (150px) and three 44px icon actions fit with 6px to spare; the name truncates
  rather than wrapping or overflowing if a font fallback is wider.
- **Touch targets:** ≈ 44px on coarse pointers (`pointer-coarse:` variants already in `button`/`input`), 40px with a
  mouse. Header actions are icon-only below `sm` with the label kept as the accessible name and a Tooltip as a
  supplementary hint for pointer users; the tooltip is disabled once the label is visible.
- No autofocus on initial mobile load. Toasts sit at the bottom (`bottom-center`, safe-area aware) because the
  composer lives in the top half of the page, so they never cover it with the keyboard open.
- Viewport units use `svh` so the mobile browser toolbar doesn't resize the background or the home layout.

## 8. Component usage

All components are shadcn/ui (style `base-nova`) on **Base UI** primitives. Use them as they are; restyle only layout
at call sites. One primitive family, no second state or form framework.

| Need | Component | Notes |
|---|---|---|
| Composer | **Base UI Autocomplete** (`ui/autocomplete.tsx`) in an Input Group, with Button | Autocomplete, not Combobox: Combobox selects a value from a list, Autocomplete keeps free text (any query is valid; suggestions only optionally complete it). Same ARIA combobox keyboard model: Up/Down, Enter accepts an active suggestion, Escape dismisses. Accessible name "Describe the book". |
| Example queries | Button (outline chips) in a group labelled "Examples" | Fill the composer and focus it; the user starts the search. Never fake results. |
| Search activity | Trace + TraceCollapsible | Collapsed one-line summary with the page-turn book; only stages the backend reports. |
| Results | Semantic lists (`ul`/`ol` + `li`, h3 titles) in `rounded-panel` surfaces, AspectRatio for covers, Badge sparingly | Cover box keeps its size whether the image loads or not. Topic requests show their matched Open Library subjects as quiet mist chips. |
| History | Sheet (right), Item list, DropdownMenu for per-entry actions, AlertDialog to clear all | Opened by the header's History action. |
| Admin | Field, Label, Input, Select, Switch, Button, AlertDialog to remove a key | Errors sit next to their field or provider. |
| Zero states, 404, error boundary | Empty | Full-page ones use the shared `StatusScreen` (icon tile, h1, one sentence, actions). One icon tile everywhere: `size-10 rounded-lg bg-sage text-teal` with a 20px icon (danger colours on the error screen). |
| Persistent recoverable errors | Alert with a "Try again" button | Close to the affected task, never a toast. |
| Loading | Skeleton (results), the page-turn book (activity) | No Spinner next to the book. |
| Secondary confirmations | Toast (sonner) | "History cleared"-type messages only; nothing that needs action. |
| Supplementary labels | Tooltip | Never the only place information lives. |
| Header actions | `HeaderActionButton` / `HeaderActionLink` (`app/HeaderAction.tsx`) | Ghost style, icon-only below `sm`, `aria-current="page"` on the active route. |

## 9. States catalogue

| State | What the user sees |
|---|---|
| First visit / home | h1 "Which book are you thinking of?", the composer, three examples. No fake history or results. |
| Suggestions failing | Nothing blocks: the list just doesn't appear; Search still works. |
| Invalid query (400) | Inline field error under the composer (`aria-invalid` + `aria-describedby`), text kept, previous answer restored. |
| Searching | Compact composer with **Stop**, the activity line with the turning book, "Searching…" and an elapsed counter (it becomes the "Search activity" summary when the answer arrives), result skeleton of stable height. |
| Match / ambiguous / none | h2 "Best match", "A few books fit your clues", or "No book matched" plus one useful refinement; server order. |
| Topic request ("a book about bears") | h2 "Books about bears" with the server's hint, all candidates as equals (never one best match), matched subjects as chips; "No books found about bears" when empty. |
| AI off (no key) | Home shows one quiet line: AI is off, set `GEMINI_API_KEY` in `.env` and restart, or add a key in Admin (the link only where Admin exists). Admin shows **Turn on AI** with both ways side by side. |
| Degraded (AI fallback) | Valid results stay; a quiet warning notice explains the fallback. Not an error. |
| Failed (catalogue, timeout, server, offline) | Danger Alert with a plain title, what to do, **Try again**; draft kept; the answer that was on screen stays below it as "Previous results for “…”"; nothing saved to history. |
| Rate limited (429) / unavailable (503) / timeout (504) | Same Alert with a countdown from `Retry-After`; **Try again** stays disabled (but focusable) until it ends; no automatic retry. A new search during the wait is held, and the composer shows a neutral "Searching resumes in N s." (not an input error). |
| Offline / network | "You're offline" or "Couldn't reach the search service"; draft and existing content kept; manual retry. |
| Cancelled | "Search stopped" with **Search again**; neutral styling, not an error. |
| Restored from history | The saved answer immediately, "Saved …" time and **Search again**; no request is made. |
| Missing saved entry | h2 "This saved search isn't on this device" and **Back to search**. |
| History empty / unavailable / full | Empty state in the sheet; warning text when this browser can't keep history; search still works. |
| Admin failure | Error next to the provider or field; never a fake success. |
| Unknown route (404) | Header, paper and type as everywhere; h1 "Page not found", one sentence, **Back to search**. Title "Page not found – Find That Book". |
| Unexpected render error | h1 "Something went wrong", "This page stopped working. Try again, or go back to search.", **Try again** and **Back to search**, and a short reference (`FTB-XXXXXXXX`) that is also `console.error`ed with the error. Never the error message or stack. If the layout itself failed, a minimal shell (mark, paper, motion control) is drawn instead. |

## 10. Copy

- Plain, sentence case, active verbs, second person where needed. Say what happened and what to do next; no apologies,
  no exclamation marks, no jokes on error screens.
- No middle-dot meta strings ("A · B · C"), no "→" in buttons, no eyebrow labels, no ALL-CAPS.
- Show the server's words only where the contract says they are user-facing (explanations, field hints); describe
  failures from `kind`/`code`/`status`, never from raw server messages.
- **Fixed vocabulary** (use exactly): Search · Stop (while running) · Search stopped · Search again · Try again ·
  History · New search · Admin · Back to search · Search activity · Open Library · Pause background motion.
- Document titles: "Find That Book" on the empty home page, "“<query>” – Find That Book" for a search,
  "<Page> – Find That Book" elsewhere (`useDocumentTitle`).

## 11. Accessibility checklist

- [ ] Landmarks: skip link "Skip to main content" first in tab order, `header`, `main#main`, `footer`; one h1 per page,
      no skipped heading levels.
- [ ] Route changes between pages move focus to `#page-heading` (fallback `#main`) without scrolling; search-param
      changes and search progress never move focus. If the new page already put focus in main (e.g. the composer
      after "New search"), it is left there.
- [ ] Every icon-only control has an accessible name; icons are `aria-hidden`.
- [ ] Field errors are associated (`aria-invalid`, `aria-describedby`) and keep the typed text.
- [ ] One polite live region announces search outcomes; nothing announces elapsed seconds; results aren't live.
- [ ] Sheets and dialogs trap focus, close on Escape and restore focus (Base UI does this; don't override it).
- [ ] Text ≥ 4.5:1 and control boundaries/focus ≥ 3:1 on their real backgrounds (tables above).
- [ ] State is never colour alone.
- [ ] 44px targets on touch; no hover-only content; works at 320px and 200% zoom without horizontal scroll.
- [ ] Reduced motion honoured; decorative motion pausable; nothing flashes.
- [ ] API text rendered as text (no `dangerouslySetInnerHTML`); external URLs pass through `safeExternalUrl`.
