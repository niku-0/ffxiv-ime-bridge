---
status: accepted
---

# The Gate blocks the game thread on fcitx5 and swallows on timeout

Swallow-or-pass for a keystroke must be known before `DispatchMessageW`
returns, but fcitx5 answers over D-Bus. We block the game's main thread in
the hook waiting for `ProcessKeyEvent`, capped at 50 ms (measured ~10 ms
from inside Wine, ticket 02), and a key that times out is **swallowed**, not
passed. Only presses of non-modifier keys are waited on; modifier presses,
all releases and focus calls are sent fire-and-forget (D-Bus keeps them in
order on one connection). A release's verdict is not fcitx5's to give: it
goes where its press finally went. Three consecutive timeouts, or fcitx5 leaving the
bus, put Forwarding into Degraded until the next Chat Box focus gain.
(Amended by ticket 13, 2026-09-19: the bus connection itself dying is a
third Degraded cause, and that one lifts only by a Reconnect — nothing on a
dead connection can be recreated on a focus gain.)

## Considered options

- **Never block; re-inject unhandled keys.** Swallow every text key at
  once, ask fcitx5 asynchronously, post back what it declines. No stall,
  but a re-injected key lands after later keys that passed directly, so
  `ka` can reach the Chat Box as `ak`. Rejected: reordering is a
  correctness bug in a text field.
- **Pass through on timeout** (the original spec wording). A slow fcitx5
  may still consume the key into the preedit after the game has inserted
  it — the double input M0.3 exists to prevent. Rejected: a lost keystroke
  is visible and recoverable, a doubled one inside a composition is not.

## Consequences

- One late frame (~10 ms, worst 50 ms) per character typed while
  Forwarding is on and the Chat Box is focused; nothing otherwise.
- Nothing in the D-Bus completion path may need the game thread — the
  Fcitx library stays `ConfigureAwait(false)` throughout and raises its
  events on the reader thread; the hook reads the state snapshot after the
  wait and does the Native Write there, and only out-of-band signals go
  through the framework tick.
- The keysym for a printing key comes from its `WM_CHAR`, so its keydown
  is swallowed provisionally while Forwarding; the game never sees
  keydowns of printing keys in that state. That the Chat Box still inserts
  a `WM_CHAR` whose keydown it never saw was **verified in-game** (ticket
  07, 2026-09-18): on direct input every letter's keydown was swallowed,
  its char passed, and the text appeared. Measured waits: ~1 ms typical,
  4.6 ms mean, with a lone 50 ms timeout seen once mid-word.
