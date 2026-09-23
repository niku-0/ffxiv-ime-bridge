---
status: accepted
---

# The Toggle Key is a scancode, not a virtual key

The chord that flips Forwarding is stored and matched as modifier flags
plus a hardware scancode — default `Alt` + `0x29`, the key left of `1` —
so it names a physical key regardless of keyboard layout: `Alt+§` on a
Nordic layout, `Alt+\`` on US, the same config value. Matching
happens in the message hook, where the scancode is in `lParam` of every
`WM_KEYDOWN`. Spec: "Forwarding" (Toggle Key); ticket 12.

## Considered options

- **`VirtualKey` with Dalamud's usual key-binding UI** (`IKeyState`, a
  dropdown). Familiar to Dalamud users, but `VK_OEM_5`/`VK_OEM_3`/… land on
  different physical keys per layout, so the documented default would hold
  on one layout only and the chord would move under a user who switches
  layouts mid-session. `IKeyState` is also polled per frame, while the
  hook needs the verdict synchronously before the game sees the key
  (ADR-0002), so the match would be done in the hook regardless. Rejected.
- **A character** (`§`). Only exists at `WM_CHAR`, and Alt chords produce
  no char. Rejected.

## Consequences

- One default serves every layout, and the spec's `Alt+§` / `Alt+\``
  wording is literally the same binding.
- The settings window cannot offer a dropdown: it captures the next
  non-modifier keydown from the hook ("press a key"), and shows the chord
  as the character the current layout produces for the scancode, falling
  back to `sc 0x29` for a key that prints nothing.
- A config file is tied to keyboard hardware with the usual scancode set;
  an exotic keyboard may need rebinding. Rarer than layout differences.
- At least one modifier is required, enforced by the UI and the gate, so a
  bare letter cannot become the chord.
- Nearly every Dalamud plugin uses `VirtualKey`; this is why not.
