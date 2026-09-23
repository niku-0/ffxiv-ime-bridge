---
status: accepted
---

# Mouse input for the overlay goes through the message hook, not an ImGui window

The Preedit and the candidate list are drawn on ImGui's foreground draw
list: no window, no widgets, nothing that can take focus. Clicks and wheel
over them are handled inside the `DispatchMessageW` hook — the same detour
the Gate lives in (ADR-0002) — by hit-testing the last frame's rects
(`CompositionPlan`), and a hit is Swallowed so the game never sees the
press, its release, or the wheel. A left click on a row calls
`SelectCandidate`, a click on `▲`/`▼` or the wheel pages; a click on the
Preedit is Swallowed and does nothing; everything else passes untouched.
Spec: "Rendering" (mouse); ticket 16.

## Considered options

- **An invisible ImGui window over the list with `NoFocusOnClick`/`NoNav`**
  — the spec's original wording and the usual Dalamud way. ImGui learns of
  a click in Dalamud's WndProc, which runs *after* the real
  `DispatchMessageW` has already given the message to the game's window
  procedure; by then the game may have moved the Cursor or unfocused the
  Chat Box. Dalamud's mouse capture (`WantCaptureMouse`) is per-frame and
  all-or-nothing, the same limitation that ruled it out for keyboard
  capture in M0.3. The spec's condition — selection "without disturbing
  native focus" — cannot be guaranteed this way. Rejected.
- **Keyboard only** — the spec's fallback if the condition could not be
  met. It can, so the fallback is not taken.

## Consequences

- The hook filter grows from keyboard messages to mouse buttons and the
  wheel (`WM_MOUSEMOVE` is never filtered). A release is Swallowed iff its
  press was, as for keys.
- The rendering layer publishes hit regions to the input layer: the layout
  plan carries rects with meanings (`Select(i)`, page, preedit box), and a
  `MouseGate` consumes them. That dependency would not exist with an ImGui
  window and is the part that is hard to undo.
- No hover state, tooltips or dragging: ImGui cannot help here, and each
  would have to be added to the hit-test. The selected row is fcitx5's
  state only.
- The calls are fire-and-forget; the `UpdateClientSideUI` they cause
  arrives out of band and the tick renders it, as any out-of-band signal
  already does.
