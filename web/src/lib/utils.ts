import { createCn } from "cn/config"

// Teach the class merger our custom tokens; unknown `text-*` would otherwise count as a colour and silently replace
// `text-muted-foreground` and friends.
export const cn = createCn({
  extend: {
    classGroups: {
      "font-size": [{ text: ["caption", "title", "heading"] }],
      shadow: [{ shadow: ["composer", "panel"] }],
      rounded: [{ rounded: ["panel", "composer"] }],
    },
  },
})
