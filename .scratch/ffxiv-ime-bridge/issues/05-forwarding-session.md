# M1.1 — Forwarding session: the Input Context lives with the Chat Box, and the Indicator shows it

Status: resolved
Type: task
Blocked by: 04

The plugin owns one fcitx5 Input Context for the life of the connection and
ties its focus to the Chat Box. Forwarding becomes a real state instead of
the M0.3 "swallow" flag, and the Indicator makes that state visible. Nothing
composes yet; keys still use the M0.3 gate.
Spec: "Forwarding", "Resilience and errors", "Rendering" (Indicator),
"Native Path coexistence", ADR-0002.

## Done when

### Session

- On load, after the transport ladder passes, the plugin keeps the
  `FcitxConnection` and one `InputContext` (`ClientDrawsComposition`
  capabilities) instead of tearing the probe down. Ladder failure → the
  spec's "fcitx5 not reachable, disabled" chat line; the plugin is inert
  and the toggle chord prints a toast saying so.
- `Forwarding` (bool, user-owned) replaces `KeyboardCapture.Swallowing`:
  toggled by the Alt+§ chord and `/imebridge toggle` (`on|off`); chat
  prints `IME Bridge: forwarding on|off`. `/imebridge swallow` is gone.
- Chat Box focus gained → `FocusIn` fire-and-forget (and, per the README,
  the context starts on the keyboard layout: do **not** force Mozc — the
  user's fcitx5 state is theirs). Focus lost → `Reset` then `FocusOut`,
  fire-and-forget. The edge is detected from `ChatBoxFocus.Read` in **both**
  places that read it: the framework tick, and the message hook before it
  decides a key (a key can reach the hook before the tick has seen the
  focus gain; it must not go to an unfocused context). Whichever sees the
  edge first issues the call; the other sees no edge.
- `Degraded` (bool): set when fcitx5 leaves the bus; cleared, with the
  context recreated, when it is back and the Chat Box next gains focus.
  Ticket 07 also sets it (three timeouts) through the same setter, and
  that case lifts on the next focus gain **without** recreating the
  context. The gate acts iff `Forwarding && !Degraded`. One chat line per
  entry into Degraded.
- **Library work:** `FcitxConnection` grows a `NameOwnerChanged` watch on
  `org.fcitx.Fcitx5` exposed as an event (`FcitxAvailabilityChanged(bool)`),
  raised on the reader thread, `ConfigureAwait(false)` like everything
  else. Host-test it against a name the test itself requests and releases
  (make the watched name a parameter) rather than by killing fcitx5.
- Events from the library arrive on the D-Bus reader thread; a small
  `MainThreadQueue` drains them on `IFramework.Update`. No library event
  handler touches game memory directly. `Committed` goes into a dedicated
  commit queue that ticket 07 drains from the hook and ticket 09 from the
  tick.
- **Test seam:** the plugin talks to the context through a small interface
  (`IInputContextClient` or similar: the calls it makes, the events it
  consumes) so the state machine is host-testable with a fake. Ticket 07
  reuses this seam for its fake fcitx5; do not make it hook-specific.
- Unload: `FocusOut`, dispose the context and connection within the
  existing 5 s budget.
- Host tests for the state machine (`Forwarding`/`Degraded`/focus edges →
  which calls are issued, in what order) with the fake; no Dalamud.

### Indicator

- Drawn on the ImGui foreground list at the Chat Box input's left edge
  (text node `ScreenX` − glyph width, vertically centred on the input; the
  text node reports `hidden` while the box is empty but its position is
  still valid, ticket 04), only while the Chat Box is focused and
  Forwarding is on.
- Glyph from the context's `CurrentIM` signal, which `GetIMInfoOnFocus`
  (part of `ClientDrawsComposition`) makes fcitx5 emit after every
  `FocusIn` — no `Controller1.CurrentInputMethod` call needed: `あ` for
  `mozc`, `A` for a `keyboard-*` input method, the IM's first letter
  otherwise; `!` while Degraded.
- Hideable via a bool for now (`/imebridge indicator on|off`); config is M2.

### Docs

- `docs/dev-plugin.md`: the "Keyboard capture (M0.3)" section's `swallow`
  commands become `toggle`; add the Indicator and `indicator on|off`.

## In-game confirmation (human)

1. Rebuild, reload. Chat says the ladder passed (or "not reachable").
2. Click into the Chat Box, `Alt+§`: chat prints `forwarding on`, the
   Indicator appears at the box's left edge showing `A` (context starts on
   `keyboard-<layout>`). Ctrl+Space does **nothing** yet: the trigger key
   only reaches our context through `ProcessKeyEvent` (ticket 07); the
   M0.3 rule swallows and drops it, and a switch from a host terminal lands
   on the terminal's context. Use `/imebridge im` (3 s delay, click back
   into the box) → Indicator `あ`.
3. Escape, click back in: still `あ` (one context, state kept). `Alt+§`
   again: Indicator gone, `forwarding off`.
4. **Native Path fight** (the spec's open question): with Forwarding on and
   `あ` showing, click elsewhere in the game window, click back into the
   Chat Box, alt-tab away and back. Does the Indicator ever revert or
   flicker, and does `busctl --user call org.fcitx.Fcitx5 /controller
   org.fcitx.Fcitx.Controller1 CurrentInputMethod` on the host say `mozc`
   while the box is focused? Record what you see; the spec's `FocusIn`
   re-issue is added only if the fight is real.
5. `killall fcitx5` on the host (or `fcitx5 -r` to restart): chat prints the
   Degraded line, Indicator `!`, typing is vanilla. fcitx5 back, Escape,
   click into the box: Indicator back to `A`.
6. Paste the relevant `dalamud.log` lines below and set `Status: resolved`.

Safety: the plugin holds one D-Bus connection to the session bus, reads
focus state through ClientStructs' public surface and draws one glyph; the
M0.3 hook is unchanged; nothing is sent (ADR-0001).

## Notes

- `ProbeRunner` keeps `/imebridge probe` for diagnostics but runs on a
  separate throwaway connection so it cannot disturb the live one.
- Persistence of `Forwarding` across loads is M2 (config); for M1 it
  starts off.

## Comments

- 2026-09-18 (agent): implemented; awaiting the in-game confirmation above.
  - Library: `FcitxConnection.WatchAvailabilityAsync(name)` +
    `AvailabilityChanged(bool)` over the bus's `NameOwnerChanged` (arg0
    match), host-tested with a name the test owns on a second connection.
    `ProbeReport.ConnectionOptions` hands out the settings the
    "authenticate" rung passed with.
  - Plugin: `Session/` — `IInputContextClient` + `IInputContextFactory`
    (the seam; ticket 07 adds the key call), `ForwardingSession` (state
    machine, 20 host tests with `FakeFcitx`), `MainThreadQueue`, real
    adapters in `FcitxContextFactory.cs`. `Bridge` owns startup (ladder →
    live connection → session) and the tick; `Indicator` draws the glyph;
    `KeyboardGate.Swallowing` → `Active`, the chord reports `Toggle` and the
    Bridge flips Forwarding. `/imebridge toggle|indicator [on|off]`.
  - Degraded lifts only on a Chat Box focus gain *after* the tick has seen
    fcitx5 return (bus loss → new context; timeouts → same context).
  - Not done: the Native Path fight check and the `FocusIn` re-issue are
    the human step 4, as written.
- 2026-09-18 (in-game): forwarding toggles, Indicator `A` visible.
  Ctrl+Space did not switch — expected, step 2 corrected above (keys are
  not forwarded until 07). That the host hotkey did nothing to the game
  window is itself evidence for step 4 that fcitx5's frontends do not see
  the game's keys.
  - Human: あ indicator visible after /imebridge im (note that this cannot be typed while forwarding is on, all characters are swallowed). glyph persist correctly after Escape, forwarding off and on again. Does not flicker when intercting with game window elsewhere, or when alt tabbing. (glyph only visible when chat is focused so clicking elsewhere hides it). 
    - terminal doesnt show mozc ❯ busctl --user call org.fcitx.Fcitx5 /controller org.fcitx.Fcitx.Controller1 CurrentInputMethod
  s "keyboard-<layout>"
    - killall fcitx5 shows ! on next to chat correctly, indicator back to A after restart
    - logs: [INF] [LocalPlugin] Loading FfxivImeBridge.dll
    [INF] [LocalPlugin] Creating plugin instance for FfxivImeBridge (async=false)
    [INF] [FfxivImeBridge] Keyboard capture: DispatchMessageW import hooked
    [INF] [LocalPlugin] Finished loading FfxivImeBridge
    [INF] [FfxivImeBridge] Transport ladder: starting
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] platform: Microsoft Windows 6.1.7601 Service Pack 1; .NET 10.0.0; Wine, version export hidden (WINEPREFIX=/home/<user>/.xlcore/wineprefix)
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] environment: DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus; XDG_RUNTIME_DIR=/run/user/1000
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] address: path=/run/user/1000/bus wine-path=Z:\run\user\1000\bus uid=1000
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] socket: AF_UNIX stream socket created (handle 2136)
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] connect: connected to /run/user/1000/bus
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] authenticate: AUTH EXTERNAL as uid 1000 accepted
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] fcitx5: InputMethod1.Version = 1
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] input-context: /org/freedesktop/portal/inputcontext/76
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] focus: focused; input method keyboard-<layout>
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] input-method: keyboard-<layout> → mozc
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] key: ProcessKeyEvent(k) handled=True
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] preedit: ｋ
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] client-side-ui: UpdateClientSideUI received 2×
    [INF] [FfxivImeBridge] Transport ladder: Session bus reachable directly; fcitx5 composes.
    [INF] [FfxivImeBridge] Session: input context /org/freedesktop/portal/inputcontext/77 created
    [INF] [FfxivImeBridge] Session: open; forwarding off
    [INF] [LocalPlugin] Unloading FfxivImeBridge
    [INF] [LocalPlugin] Finished unloading FfxivImeBridge
    [INF] [Profile] Adding plugin FfxivImeBridge("8500e797-d0c0-4516-b1dd-2f2426f0c43b") to profile "00000000-0000-0000-0000-000000000000" with state false
    [INF] [Profile] Adding plugin FfxivImeBridge("8500e797-d0c0-4516-b1dd-2f2426f0c43b") to profile "00000000-0000-0000-0000-000000000000" with state true
    [INF] [LocalPlugin] Loading FfxivImeBridge.dll
    [INF] [LocalPlugin] Creating plugin instance for FfxivImeBridge (async=false)
    [INF] [FfxivImeBridge] Keyboard capture: DispatchMessageW import hooked
    [INF] [LocalPlugin] Finished loading FfxivImeBridge
    [INF] [FfxivImeBridge] Transport ladder: starting
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] platform: Microsoft Windows 6.1.7601 Service Pack 1; .NET 10.0.0; Wine, version export hidden (WINEPREFIX=/home/<user>/.xlcore/wineprefix)
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] environment: DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus; XDG_RUNTIME_DIR=/run/user/1000
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] address: path=/run/user/1000/bus wine-path=Z:\run\user\1000\bus uid=1000
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] socket: AF_UNIX stream socket created (handle 2136)
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] connect: connected to /run/user/1000/bus
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] authenticate: AUTH EXTERNAL as uid 1000 accepted
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] fcitx5: InputMethod1.Version = 1
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] input-context: /org/freedesktop/portal/inputcontext/78
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] focus: focused; input method keyboard-<layout>
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] input-method: keyboard-<layout> → mozc
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] key: ProcessKeyEvent(k) handled=True
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] preedit: ｋ
    [INF] [FfxivImeBridge] Transport ladder: [Passed ] client-side-ui: UpdateClientSideUI received 2×
    [INF] [FfxivImeBridge] Transport ladder: Session bus reachable directly; fcitx5 composes.
    [INF] [FfxivImeBridge] Session: input context /org/freedesktop/portal/inputcontext/79 created
    [INF] [FfxivImeBridge] Session: open; forwarding off
    [INF] [FfxivImeBridge] Session: fcitx5 left the bus
    [INF] [FfxivImeBridge] Session: forwarding degraded: BusLost
    [INF] [FfxivImeBridge] Session: fcitx5 appeared on the bus
    [INF] [FfxivImeBridge] Session: fcitx5 left the bus
    [INF] [FfxivImeBridge] Session: fcitx5 appeared on the bus
    [INF] [FfxivImeBridge] Session: fcitx5 is back; recreating the input context
    [INF] [FfxivImeBridge] Session: input context /org/freedesktop/portal/inputcontext/1 created
    [INF] [FfxivImeBridge] Session: input context recreated; forwarding degraded lifted
- 2026-09-18 (agent): resolved on the run above. Step 4 verdict: **no
  Native Path fight** — the glyph held `あ` through clicks elsewhere and
  alt-tab, and Ctrl+Space never reached any context. (The `busctl
  CurrentInputMethod` reading is not evidence: run from a terminal it
  describes the terminal's own context.) The spec's `FocusIn` re-issue is
  therefore **not** added; recorded in spec.md "Native Path coexistence".
  Two leave/appear pairs on `fcitx5 -r` are the name changing hands twice
  during the restart. `/imebridge im` cannot be typed while Forwarding is
  on until Slash Bypass (07).
