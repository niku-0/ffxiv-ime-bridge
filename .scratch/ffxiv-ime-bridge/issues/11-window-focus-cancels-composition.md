# M2.1 — Game window losing focus cancels the Composition

Status: resolved
Type: bug
Blocked by: 10

Found in M1 acceptance (ticket 10, step 7): alt-tab mid-Composition puts
the composed text into the Chat Box. Nothing is sent, but the spec says
losing focus mid-Composition cancels it ("Forwarding").

## Cause

The Chat Box keeps its focus while the game window is inactive, so
`ForwardingSession.ObserveFocus` sees no edge and no `Reset` goes out. The
window alt-tabbed to has its own fcitx5 Input Context; when it calls
`FocusIn`, fcitx5 focuses ours out by itself (one focused context per
group), and Mozc commits on focus-out whether or not `ClientUnfocusCommit`
is set (M0.1, `src/FfxivImeBridge.Fcitx/README.md`). The `CommitString`
arrives out of band, the tick drains it, and the Native Write writes it.

Window activation is a *sent* message (`WM_ACTIVATE`, `WM_ACTIVATEAPP`,
`WM_KILLFOCUS`), not posted, so the `DispatchMessageW` hook never sees it.
`FFXIVClientStructs...Framework.WindowInactive` (a bool on
`Framework.Instance()`) is the game's own view of it, readable on the tick.

## Done when

- The session's focus condition becomes "Chat Box focused **and** game
  window active": `WindowInactive` is read on the tick next to
  `ChatBoxFocus.Read`, and the window going inactive with the box focused
  is a focus loss (`Reset` then `FocusOut`), the window coming back with
  the box still focused a focus gain (`FocusIn`). Same edge rules as
  ticket 05: whichever reader sees the edge first issues the calls.
- The race: our `Reset` (next tick, ≤ 16 ms after Wine sees the X
  `FocusOut`) against the other client's `FocusIn` reaching fcitx5. If
  Mozc still commits sometimes, the fallback is decided here: a Commit
  that arrives while the window is inactive **and** no key was forwarded
  since the deactivation is fcitx5's focus-out commit, not the user's, and
  is dropped with a log line — write it down as a spec amendment if taken.
- Host tests for the state machine: window inactive with the box focused
  → `Reset`, `FocusOut`; active again → `FocusIn`; inactive with the box
  unfocused → nothing; the drop rule if adopted.
- `docs/dev-plugin.md`'s Forwarding section mentions alt-tab.

## In-game confirmation (human)

1. Forwarding on, `あ`, `konnichiha`, alt-tab away, alt-tab back: box
   empty, preedit gone, nothing sent. Trace/log shows the `Reset`.
2. Alt-tab with the box focused and **no** composition, back, type: keys
   still reach fcitx5 (the `FocusIn` on return was needed — without it
   fcitx5 may drop keys to an unfocused context; note what happens if you
   skip it).
3. Alt-tab with Forwarding on and the box **unfocused**: no calls, no
   chat lines, the Indicator unchanged.
4. Set `Status: resolved`.

Safety: two more fire-and-forget context calls per window switch; no game
memory is written; nothing is sent (ADR-0001).

## Comments

- 2026-09-18 (agent): implemented; awaiting the in-game confirmation above.
  The change is in the focus condition itself, not the state machine:
  `FocusSnapshot` gains `WindowActive` (`Framework.Instance()->WindowInactive`
  negated, read in `ChatBoxFocus.Read` next to the ATK focus walk) and
  `ChatBoxFocused` requires it. Both readers — the tick and the hook — go
  through that snapshot, so the edge rules of ticket 05 apply unchanged:
  window inactive with the box focused → `Reset`, `FocusOut`; active again
  with the box still focused → `FocusIn`; box unfocused → nothing. Host
  tests: `FocusSnapshotTests` (window inactive ⇒ not focused) and
  `ForwardingSessionTests` (the alt-tab sequence through snapshots); 221
  in the plugin suite, 17 in the library's. The debug window's focus line
  and the trace now show `windowActive=`. The drop rule is **not** taken:
  it waits for step 1 to show whether `Reset` loses the race to the other
  client's `FocusIn`. `docs/dev-plugin.md` Forwarding section mentions
  alt-tab.
- 2026-09-19 (human): all three in-game checks pass — alt-tab mid-Composition
  leaves the box empty with nothing sent, keys still reach fcitx5 after
  returning, and an unfocused box sees no calls. `Reset` wins the race, so
  the drop rule stays out. Resolved.
