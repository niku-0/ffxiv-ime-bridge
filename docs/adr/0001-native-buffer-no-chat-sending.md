---
status: accepted
---

# Committed text goes into the game's own chat box; the plugin never sends chat

The original spec had the plugin keep the assembled message in its own buffer
and send it through a chat-sending function. Dalamud has no official
chat-send API (plugins that send use a game-function signature), which puts
the plugin in automation-adjacent territory with real policy and account risk
for users. We decided instead that on Commit the plugin writes the committed
text into the native chat box's text input and does nothing else: the user
presses Enter and the game sends. Channel selection, slash commands, length
limits and input history stay the game's responsibility.

## Consequences

- No chat automation anywhere in the codebase, by design. Do not add a
  send path "for convenience".
- The preedit must be positioned relative to the native input's cursor, and
  inserting into the native buffer needs its own feasibility check in M0.
- "Full channel support" stops being a milestone: it's inherited from the
  native chat box.
