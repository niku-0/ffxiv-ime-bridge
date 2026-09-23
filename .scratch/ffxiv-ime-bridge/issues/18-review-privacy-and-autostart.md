# Review — chat drafts in the log, the keystroke trace, fcitx5 auto-start

Status: resolved
Type: task

From the security/safety review of 2026-09-21. No vulnerability and no
ban-risk finding came out of it; what did are two privacy leaks — text the
user typed ending up in `dalamud.log` and on the clipboard — and one side
effect on the user's desktop. All three are small changes; ticket 19 holds
the low-priority hardening.

## 1. Chat drafts are written to `dalamud.log` at Information level

`NativeWriter.Publish` (`src/FfxivImeBridge/NativeWrite/NativeWriter.cs:181`)
logs `report.ToString()` for every successful Native Write. The report
(`WriteReport.ToString`, `:46`) carries the committed text and, through
`Before`/`AfterWrite`/`NextFrame`, the **whole Chat Box contents** before and
after — so a `/tell Name …` draft lands verbatim in a file users upload to
support channels. The refused (`:186`) and failed (`:189`) lines carry the
text too. Dalamud's default log level is Information (`LogLevel: 2` in
`dalamudConfig.json`), so this is on for everyone.

Do:

- At Information, log outcome and numbers only: outcome, where it was written
  (byte offset / append-at-end), the byte and code-point counts before and
  after, the cursor indices, whether the text held at the next frame. No
  text.
- The text-bearing `report.ToString()` goes to Debug (the same line as today).
- The refused line: Warning with `Detail` only; the refused text at Debug.
- The failed line: Warning with `Detail` and the text's byte count; the text
  itself at Debug — it is lost to the user, so keep it *somewhere*, but not
  at a level that is on by default.
- The debug tab's report and **Copy report** keep the full text (a deliberate
  user action). `docs/dev-plugin.md`: "the refused text is in `dalamud.log`"
  becomes "…at Debug level".

## 2. The keyboard trace records everything typed anywhere in the game

`KeyboardCapture.Decide` records every keyboard message
(`src/FfxivImeBridge/Capture/KeyboardCapture.cs:135`), `WM_CHAR` with its
decoded character included, whatever Forwarding, focus and the active field
are — the mouse path already skips `Inactive` (`:157`), the keyboard path
does not. The 200-entry ring buffer is therefore a keylogger of every text
field in the game (mail, FC board, market search, tells), **Copy trace** puts
it on the clipboard next to a prompt to paste it into a ticket, and `:173`
logs each line at Debug.

Do:

- Record and log a keyboard message only when the gate acted on it: skip
  `GateRule.Inactive` as the mouse path does. `Seen`/`Swallowed` keep
  counting everything (the counts are useful and carry no text).
- The Toggle Key chord (`Toggle`) and a capture (`Captured`) stay traced —
  they are the gate acting.
- `docs/dev-plugin.md`: the Keyboard tab describes the trace as "the last
  200 keyboard messages" → "the last 200 messages the gate acted on; keys
  typed while Forwarding is off, or outside the Chat Box, are not recorded".
- Host test in `KeyboardGateTests`/a `KeyboardCapture` test if one exists:
  an Inactive keydown/char/keyup produces no trace entry.
- Same family, lower weight: **Dump input nodes** logs every text node's
  string at Information (`DebugWindow.cs:189`), which includes the current
  draft. Button-triggered, so acceptable; move the lines to Debug and say so
  in the tab's hint text.

## 3. Method calls can start `fcitx5` on the user's desktop

fcitx5 ships a D-Bus activation file
(`/usr/share/dbus-1/services/org.fcitx.Fcitx5.service`, present on the dev
machine), so any method call addressed to `org.fcitx.Fcitx5` while nobody
owns the name makes `dbus-daemon` exec `/usr/bin/fcitx5`. The plugin sends
such calls after fcitx5 has left the bus:

- `ForwardingSession.DisposeAsync` skips `DestroyIC` only for
  `ConnectionLost`, not `BusLost`
  (`src/FfxivImeBridge/Session/ForwardingSession.cs:257` →
  `src/FfxivImeBridge.Fcitx/InputContext.cs:147`): unloading, reloading or
  a Reconnect after the user quit fcitx5 restarts it.
- `KeyboardGate.DecideRelease` sends the release of a press forwarded before
  the loss (`src/FfxivImeBridge/Capture/KeyboardGate.cs:221`).
- `TransportProbe` has a small window between `ListNames` and `Version`.

Do:

- Pass `MessageFlags.NoAutoStart` on every `WriteMethodCallHeader` in the
  library (`FcitxConnection.cs`, `InputContext.cs`; Tmds.DBus.Protocol
  0.95.1 has the overload). The bus then answers
  `org.freedesktop.DBus.Error.ServiceUnknown` instead of spawning, which the
  existing fault logging already handles.
- `ForwardingSession.DisposeAsync`: skip the context's `DisposeAsync` while
  Degraded for `BusLost` as well (fcitx5 reaped its side when it left); the
  subscriptions still need disposing, so split `InputContext.DisposeAsync`
  or add a "forget without DestroyIC" path.
- Host test in `FfxivImeBridge.Fcitx.Tests`: a method call to an unowned
  name fails with `ServiceUnknown` and no process is started (assert the
  message header carries the flag; the real bus is the developer's, so do
  not test against `org.fcitx.Fcitx5` itself — use a made-up name).
- `src/FfxivImeBridge.Fcitx/README.md`: note the flag under "Things that are
  easy to get wrong".

## Done when

- The three items above are implemented with the tests named.
- `dalamud.log` at Information contains no Chat Box text and no keystrokes
  during a session of typing, committing and one refused Overflow.
- `docs/dev-plugin.md` and the library README updated as listed.
- In-game confirmation (human): with Forwarding on, `pkill fcitx5`, click
  into the Chat Box, unload the plugin: `pgrep fcitx5` is empty.

Safety: nothing here touches the Native Write itself or what is sent
(ADR-0001); logging levels, a trace filter and a D-Bus header flag.

## Comments

- 2026-09-22 (agent): all three implemented; `dotnet test` green (321).

  1. **Chat drafts.** `WriteReport.Summary` is the Information line: outcome,
     `Detail` (the cursor indices, and after the readback whether the text
     held), `Where` it went, and byte/code-point counts for the committed text
     and for each state read — no text. `Where` is its own property rather
     than the opening of `Detail` as before, because the next-frame publish
     replaces `Detail` with its verdict and the ticket wants both facts on the
     line; `ToString()` prints it too. `report.ToString()` moved to Debug,
     otherwise unchanged. Refused and Failed are a Warning with `Detail` (and,
     for Failed, the byte count) plus the text at Debug. `Verdict`'s "not
     where planned" branch no longer quotes the Chat Box; the next-frame state
     in the report has it. The tab's report and **Copy report** are untouched.
     `WriteReportTests` asserts the summary names neither the draft nor the
     committed text before *or* after the readback, that it still says where
     the text went afterwards, and that the full report keeps everything.

  2. **Keyboard trace.** `GateDecision.Acted` (`Rule != Inactive`) is the
     predicate; `KeyboardCapture.Record` counts first and returns before the
     trace and the Debug line. `Rule != Inactive` alone was not enough: the
     `WM_CHAR` and `WM_KEYUP` of an untouched press came back `FollowsPress`,
     and a `WM_CHAR` names the character typed — the leak the ticket
     describes. `Press.Untouched` now carries it, and `Following()` reports
     those as `Inactive`, as it does an orphan char with no press to follow.
     `Seen`/`Swallowed`/`Asked` still count everything on the keyboard path.
     `KeyboardGateTests` covers a whole keystroke with Forwarding off and one
     outside the Chat Box, and that Toggle and Captured stay recorded.
     **Dump input nodes** logs at Debug, said in the tab's hint. One line
     beyond the ticket: the `SurrogateHalf` Information line printed the code
     unit typed, so it went to Debug with the rest.

  3. **Auto-start.** `MethodCall.Flags` (`MessageFlags.NoAutoStart`) is passed
     at all ten `WriteMethodCallHeader`s. `IInputContextClient.Abandon()` lets
     a context go without `DestroyIC` (`InputContext.Abandon`, splitting
     `Unsubscribe` out of `DisposeAsync`), and `ForwardingSession.DisposeAsync`
     takes it while `NothingToTell` — BusLost as well as ConnectionLost.
     `CompleteRecreation` still disposes the replaced context: fcitx5 is back
     on the bus by then, and the flag covers the window where it is not.
     `NoAutoStartTests` starts a `dbus-daemon` of its own with an activatable
     name whose service file runs `touch`: with the flag the call is refused
     and the file never appears, without it the bus starts the service. The
     developer's fcitx5 is never addressed.

  Correction to the ticket: dbus-daemon 1.16.2 answers a `NoAutoStart` call to
  an activatable name nobody owns with `org.freedesktop.DBus.Error.NameHasNoOwner`,
  not `ServiceUnknown` (which is for a name it has never heard of). Either way
  it is an error reply the existing fault logging handles, and nothing starts.
  The README and the library doc say `NameHasNoOwner`.

  Two findings from `/code-review` are folded in above: the post-readback
  Information line had lost "where it was written" (hence `Where`), and the
  new `dev-plugin.md` sentence about `Messages seen`/`swallowed` overstated
  the mouse path, which counts only the messages it traces.

  Left for the human (the ticket's own in-game check, which is why this is
  `ready-for-human`): with Forwarding on, `pkill fcitx5`, click into the Chat
  Box, unload the plugin, and confirm `pgrep fcitx5` is empty; and that a
  session of typing, committing and one refused Overflow leaves no Chat Box
  text and no keystrokes in `dalamud.log` at Information.

- 2026-09-22 (human): in-game confirmation passed — with Forwarding on,
  `pkill fcitx5`, a click into the Chat Box and unloading the plugin leave
  `pgrep fcitx5` empty, and a session of typing, committing and a refused
  Overflow leaves no Chat Box text and no keystrokes in `dalamud.log` at
  Information. Resolved.
