"use client"

import { Autocomplete as AutocompletePrimitive } from "@base-ui/react/autocomplete"
import { cn } from "@/lib/utils"

import { InputGroup, InputGroupInput } from "@/components/ui/input-group"

/*
 * Base UI Autocomplete: the free-text member of the Combobox family (same ARIA combobox keyboard model), for inputs
 * whose suggestions only optionally complete the text. Styled like the shadcn Combobox; behaviour is Base UI's.
 */

const Autocomplete = AutocompletePrimitive.Root

function AutocompleteInputGroup({
  className,
  ...props
}: AutocompletePrimitive.InputGroup.Props) {
  return (
    <AutocompletePrimitive.InputGroup
      render={<InputGroup />}
      className={cn(className)}
      {...props}
    />
  )
}

function AutocompleteInput({
  className,
  ...props
}: AutocompletePrimitive.Input.Props) {
  return (
    <AutocompletePrimitive.Input
      render={<InputGroupInput />}
      className={cn(className)}
      {...props}
    />
  )
}

/*
 * While the popup is open, Base UI sets aria-hidden on everything outside it and the input (axe reports
 * aria-hidden-focus for the controls in there). Base UI 1.9 has no option to turn that off for an input outside its
 * popup: ComboboxPopup always renders FloatingFocusManager with modal = !inputInsidePopup || modal (true here), and
 * that applies markOthers(..., { ariaHidden: modal || isUntrappedTypeableCombobox }). Root `modal` only matters when
 * the input is inside the popup, Popup `initialFocus` only changes the second operand, and Root `inline` drops the
 * popup altogether (an always-expanded list). The page comes back the moment focus leaves the input, which closes
 * the list.
 */
function AutocompleteContent({
  className,
  side = "bottom",
  sideOffset = 8,
  align = "start",
  alignOffset = 0,
  anchor,
  collisionAvoidance,
  ...props
}: AutocompletePrimitive.Popup.Props &
  Pick<
    AutocompletePrimitive.Positioner.Props,
    | "side"
    | "align"
    | "sideOffset"
    | "alignOffset"
    | "anchor"
    | "collisionAvoidance"
  >) {
  return (
    <AutocompletePrimitive.Portal>
      <AutocompletePrimitive.Positioner
        side={side}
        sideOffset={sideOffset}
        align={align}
        alignOffset={alignOffset}
        anchor={anchor}
        collisionAvoidance={collisionAvoidance}
        className="isolate z-50"
      >
        <AutocompletePrimitive.Popup
          data-slot="autocomplete-content"
          className={cn(
            "group/autocomplete-content relative max-h-(--available-height) w-(--anchor-width) max-w-(--available-width) origin-(--transform-origin) overflow-hidden rounded-panel border border-line bg-popover text-popover-foreground shadow-composer duration-150 ease-(--ease-out-soft) data-open:animate-in data-open:fade-in-0 data-[side=bottom]:slide-in-from-top-1 data-[side=top]:slide-in-from-bottom-1",
            className
          )}
          {...props}
        />
      </AutocompletePrimitive.Positioner>
    </AutocompletePrimitive.Portal>
  )
}

function AutocompleteList({
  className,
  ...props
}: AutocompletePrimitive.List.Props) {
  return (
    <AutocompletePrimitive.List
      data-slot="autocomplete-list"
      className={cn(
        "max-h-[min(26rem,calc(var(--available-height)-2px))] scroll-py-1.5 overflow-y-auto overscroll-contain p-1.5 data-empty:p-0",
        className
      )}
      {...props}
    />
  )
}

function AutocompleteItem({
  className,
  ...props
}: AutocompletePrimitive.Item.Props) {
  return (
    <AutocompletePrimitive.Item
      data-slot="autocomplete-item"
      className={cn(
        "relative flex w-full cursor-default items-center gap-3 rounded-lg px-2.5 py-2 text-base outline-none select-none data-disabled:pointer-events-none data-disabled:opacity-50 data-highlighted:bg-accent data-highlighted:text-accent-foreground data-highlighted:outline-2 data-highlighted:-outline-offset-2 data-highlighted:outline-ring data-highlighted:outline-solid [&_svg]:pointer-events-none [&_svg]:shrink-0",
        className
      )}
      {...props}
    />
  )
}

export {
  Autocomplete,
  AutocompleteInputGroup,
  AutocompleteInput,
  AutocompleteContent,
  AutocompleteList,
  AutocompleteItem,
}
