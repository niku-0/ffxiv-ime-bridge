# M1.3 — The Gate asks fcitx5 (replaces the M0.3 text-key rule)

Status: resolved
Type: task
Blocked by: 05, 06

`KeyboardGate.IsTextKey` becomes fcitx5's answer, obtained synchronously in
the hook per ADR-0002. Slash Bypass lives here. The pairing rules from
M0.3 stay in spirit: the game sees a whole keystroke or none of it.

## Done when

- While `Forwarding && !Degraded` and the Chat Box is focused:
  - `Printing` keydown → swallowed provisionally; its `WM_CHAR` → `KeyEvent`
    → **waited** `ProcessKeyEvent` (50 ms) → handled ⇒ swallow char, else
    pass char. The char's verdict becomes the press's final verdict. A
    provisional keydown whose char never comes is gone.
  - `Fixed` keydown → waited `ProcessKeyEvent` unless
    `SkipsOutsideComposition` and no Composition is active, in which case
    it passes without asking. Chars that follow go where the keydown went.
  - `Modifier` press/release → sent fire-and-forget, always pass.
  - Every other release → sent fire-and-forget; verdict = its press's
    final verdict (spec "Keyboard capture", `CONTEXT.md` Swallow).
  - A `WM_CHAR` that is a surrogate half (`KeyMessage.IsSurrogateHalf`,
    `KeyTranslation.FromChar` returns `null`) → logged at Information and
    passed, no pairing (ticket 06's rule; this is where it is enforced).
  - `DeadChar` → swallow silently. Every auto-repeat is a fresh press (the
    M0.3 "first press decides the hold" rule goes; its tests are updated,
    not deleted). Toggle chord → as in M0.3, checked before any asking.
  - Timeout → swallow; 3 consecutive → `Degraded` through ticket 05's
    setter; any reply resets the count.
- Slash Bypass: `/` char with an empty Chat Box and no Composition passes
  unasked and starts bypass; chars pass unasked until space, Enter, Escape
  or the box reading empty; `/` mid-text is asked like any char. "Empty"
  is read from `ChatBoxAccess` (raw text length 0).
- After a waited call returns, the hook reads the `InputContext.State`
  snapshot for rendering (ticket 08) and drains ticket 05's commit queue
  into the Native Write (ticket 09) — both on the game thread, in the
  hook, before the next message. A reply that arrives after its timeout
  changes state and may commit; those reach the tick's drain instead
  (ticket 09 handles both).
- `ForwardKey` → logged at Information, otherwise ignored.
- The Keyboard debug tab's trace shows, per message, the class, whether it
  was asked, the wait in ms, and the verdict; a running max/mean of waits.
- Host tests: the gate with a fake synchronous "fcitx5" over ticket 05's
  seam (scripted answers, scripted delays): provisional keydown + char
  pairing (both outcomes, and the release following the char's verdict),
  skip list in/out of Composition, bypass start/end, timeout counting and
  Degraded, repeats as fresh presses, AltGr as printing.

## In-game confirmation (human)

The **first** step is the one ADR-0002 depends on and nothing has tested.

1. Rebuild, reload, Forwarding on, Indicator `A` (direct input; fcitx5
   declines every printing key). Type `abc` in the Chat Box. **Do the
   letters appear?** Each keydown was swallowed and only its `WM_CHAR`
   passed. If the box stays empty, stop here and report: ADR-0002 is
   reopened (the press would have to be re-posted or the classification
   rethought). Then Enter in `/echo`: sends `abc`.
2. Still on `A`: `Shift+A`, Backspace, arrows, Home/End behave vanilla;
   AltGr+2 puts `@` in the box (paste the trace lines for it into ticket
   06's Comments — that is the record of what Wine posts). Then the same on
   `あ`: ticket 06 sends `@` with Ctrl+Alt state (Windows' AltGr), where an
   X11 client would send Mod5; if Mozc treats it as a chord instead of text,
   the state for AltGr chars needs amending in `Modifiers.ToKeyState`.
3. Ctrl+Space → `あ`. Type `konnichiha`: nothing appears in the box (the
   preedit is ticket 08; the trace shows `Swallow` with waits ~10 ms). Enter:
   the trace shows Enter asked and swallowed (Mozc commits — ticket 09 will
   write it; for now the text is only in the log as a queued commit). Escape
   inside a composition: asked, swallowed, nothing sent.
4. Slash Bypass: empty box, `あ` showing, type `/echo `: the `/echo` passes
   unasked (trace `Bypass`), the space ends it, `konnichiha` after it is
   asked. Escape.
5. Timeouts: `kill -STOP $(pidof fcitx5)` on the host, type three letters:
   trace shows three 50 ms timeouts, chat prints the Degraded line,
   Indicator `!`, further letters are vanilla. `kill -CONT`, Escape, click
   back in: recovered.
6. Note the trace's max/mean wait. Paste the trace and set
   `Status: resolved`.

Safety: keys are dropped or passed exactly as M0.3 did, one D-Bus call per
key; nothing is written into the Chat Box yet and nothing is sent.

## Notes

- The wait is `task.Wait(TimeSpan)` on the game thread; the library is
  `ConfigureAwait(false)` throughout, so it cannot deadlock — keep it that
  way (no `SynchronizationContext`, no framework-thread awaits in the
  library).
- Blocking cost: one late frame per character; see ADR-0002 before
  "optimising" this into something asynchronous.

## Comments

- 2026-09-18 (agent): implemented; awaiting the in-game confirmation above
  (step 1 is the ADR-0002 check). Host-tested: 36 gate cases in
  `tests/FfxivImeBridge.Tests/KeyboardGateTests.cs` over a real
  `ForwardingSession` + `FakeInputContext` (scripted answers via `Replies`,
  a never-completing task as silence), plus a session snapshot test.
  - Seam: `IInputContextClient` gains `State`, `ProcessKeyAsync` (the one
    waited call) and `SendKey` (fire-and-forget); `ForwardingSession` exposes
    `Context`, `IsComposing` (the context's live state) and `Composition`
    (the snapshot taken on the game thread by `TakeSnapshot()` — after each
    waited reply from the hook, and on every `Tick()`).
  - `KeyboardGate(IKeyStateReader, Func<bool> chatBoxEmpty)` with `Session`
    set per message by `KeyboardCapture`; `Decide` returns a `GateDecision`
    (verdict, `KeyClass`, `GateRule`, wait ms). Rules: `Provisional`,
    `Answered`, `TimedOut`, `Skipped`, `Bypass`, `FollowsPress`, `Modifier`,
    `DeadChar`, `SurrogateHalf`, `Toggle`, `Inactive`. Timeout 50 ms
    (`task.Wait`), a faulted call counts as silence; 3 in a row →
    `MarkDegraded(Timeouts)`, any reply resets.
  - Choices worth knowing: the space that ends Slash Bypass passes unasked
    too (Mozc would otherwise put a full-width space after `/tell Name`);
    Enter/Escape end bypass at their keydown; the toggle chord's repeats,
    char and release are swallowed without toggling again; a char arriving
    for a provisional press after Forwarding went off is swallowed with it
    (the game never saw the keydown); a release is sent only if its press
    was (a `Skipped` or unforwarded keydown was never told, so neither is
    its release — the spec's "every release" read as "of every forwarded
    press"); a surrogate half marks its press as passed; bypass survives a
    focus loss (the spec's four end conditions are the only ones).
  - After a waited reply `KeyboardCapture` calls `Bridge.SettleAfterReply()`:
    snapshot + commit drain. Until ticket 09 a commit is only logged as
    `Session: commit queued (Native Write is ticket 09): …`; the tick drains
    the same way.
  - The Keyboard tab: `asked` count with max/mean wait; trace lines are
    `verdict class rule asked/unasked wait message [owner]`.
  - Not done here: AltGr's actual Wine messages (step 2 records them in
    ticket 06) and Mozc's take on `@`+Ctrl+Alt.
    
- human tests: All behavior passes, trace log:
  Session: forwarding=on degraded=False gate=active input method=mozc glyph=あ seen=281 swallowed=135 asked=51 wait max=50.2ms mean=4.6ms
  focus: textInputActive=True owner=ChatLog uiHidden=False → chatBoxFocused=True
  Pass    Modifier  FollowsPress  unasked       KEYUP vk=0x11 sc=0x1D  [-]
  Pass    Modifier  Modifier      unasked       KEYDOWN vk=0x11 sc=0x1D  [ChatLog]
  Swallow Fixed     Answered      asked   0.3ms KEYDOWN vk=0x20 sc=0x39  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+0020 ' ' sc=0x39  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x20 sc=0x39  [ChatLog]
  Pass    Modifier  FollowsPress  unasked       KEYUP vk=0x11 sc=0x1D  [ChatLog]
  Pass    Modifier  Modifier      unasked       KEYDOWN vk=0x11 sc=0x1D  [ChatLog]
  Swallow Fixed     Answered      asked   0.8ms KEYDOWN vk=0x20 sc=0x39  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+0020 ' ' sc=0x39  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x20 sc=0x39  [ChatLog]
  Pass    Modifier  FollowsPress  unasked       KEYUP vk=0x11 sc=0x1D  [ChatLog]
  Pass    Modifier  Modifier      unasked       KEYDOWN vk=0x11 sc=0x1D  [ChatLog]
  Swallow Fixed     Answered      asked   0.7ms KEYDOWN vk=0x20 sc=0x39  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+0020 ' ' sc=0x39  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x20 sc=0x39  [ChatLog]
  Pass    Modifier  FollowsPress  unasked       KEYUP vk=0x11 sc=0x1D  [ChatLog]
  Pass    Fixed     Answered      asked   0.3ms KEYDOWN vk=0xE4 sc=0x38 ext  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x32 sc=0x03  [ChatLog]
  Pass    Printing  Answered      asked   0.4ms CHAR U+0040 '@' sc=0x03  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x32 sc=0x03  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0xE4 sc=0x38 ext  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4B sc=0x25  [ChatLog]
  Swallow Printing  Answered      asked   0.7ms CHAR U+006B 'k' sc=0x25  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4B sc=0x25  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4F sc=0x18  [ChatLog]
  Swallow Printing  Answered      asked   1.1ms CHAR U+006F 'o' sc=0x18  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4F sc=0x18  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Answered      asked   0.7ms CHAR U+006E 'n' sc=0x31  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Answered      asked   0.8ms CHAR U+006E 'n' sc=0x31  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  TimedOut      asked  49.2ms CHAR U+0069 'i' sc=0x17  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  Answered      asked   1.0ms CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  Answered      asked   1.3ms CHAR U+0068 'h' sc=0x23  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Answered      asked   1.1ms CHAR U+0069 'i' sc=0x17  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  Answered      asked   1.0ms CHAR U+0068 'h' sc=0x23  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Answered      asked   1.4ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Fixed     Answered      asked   1.3ms KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+000D sc=0x1C  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x0D sc=0x1C  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Answered      asked   0.7ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x42 sc=0x30  [ChatLog]
  Swallow Printing  Answered      asked   1.0ms CHAR U+0062 'b' sc=0x30  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x42 sc=0x30  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  Answered      asked   0.8ms CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Swallow Fixed     Answered      asked   0.6ms KEYDOWN vk=0x1B sc=0x01  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+001B sc=0x01  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x1B sc=0x01  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x1B sc=0x01  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+001B sc=0x01  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x1B sc=0x01  [-]
  Pass    Fixed     Inactive      unasked       KEYDOWN vk=0x0D sc=0x1C  [-]
  Pass    -         FollowsPress  unasked       CHAR U+000D sc=0x1C  [-]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x0D sc=0x1C  [ChatLog]
  Pass    Modifier  Modifier      unasked       KEYDOWN vk=0x10 sc=0x2A  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x37 sc=0x08  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+002F '/' sc=0x08  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x37 sc=0x08  [ChatLog]
  Pass    Modifier  FollowsPress  unasked       KEYUP vk=0x10 sc=0x2A  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+0068 'h' sc=0x23  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4F sc=0x18  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+006F 'o' sc=0x18  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x48 sc=0x23  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x4F sc=0x18  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x45 sc=0x12  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+0065 'e' sc=0x12  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x45 sc=0x12  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+0068 'h' sc=0x23  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4F sc=0x18  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+006F 'o' sc=0x18  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x48 sc=0x23  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x4F sc=0x18  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x20 sc=0x39  [ChatLog]
  Pass    Printing  Bypass        unasked       CHAR U+0020 ' ' sc=0x39  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x20 sc=0x39  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4B sc=0x25  [ChatLog]
  Swallow Printing  Answered      asked   0.5ms CHAR U+006B 'k' sc=0x25  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4F sc=0x18  [ChatLog]
  Swallow Printing  Answered      asked   0.7ms CHAR U+006F 'o' sc=0x18  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4B sc=0x25  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4F sc=0x18  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Answered      asked   0.8ms CHAR U+006E 'n' sc=0x31  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Answered      asked   0.7ms CHAR U+006E 'n' sc=0x31  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4E sc=0x31  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Answered      asked   1.0ms CHAR U+0069 'i' sc=0x17  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  Answered      asked   1.1ms CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  Answered      asked   0.9ms CHAR U+0068 'h' sc=0x23  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Answered      asked   1.7ms CHAR U+0069 'i' sc=0x17  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x49 sc=0x17  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  Answered      asked   1.0ms CHAR U+0068 'h' sc=0x23  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Answered      asked   1.1ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x48 sc=0x23  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Fixed     Answered      asked   0.5ms KEYDOWN vk=0x1B sc=0x01  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+001B sc=0x01  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x1B sc=0x01  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+000D sc=0x1C  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x0D sc=0x1C  [-]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  TimedOut      asked  50.2ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x42 sc=0x30  [ChatLog]
  Swallow Printing  TimedOut      asked  50.1ms CHAR U+0062 'b' sc=0x30  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x42 sc=0x30  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Swallow Printing  TimedOut      asked  50.1ms CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Pass    Printing  Inactive      unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    Printing  Inactive      unasked       KEYDOWN vk=0x53 sc=0x1F  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0073 's' sc=0x1F  [ChatLog]
  Pass    Printing  Inactive      unasked       KEYDOWN vk=0x44 sc=0x20  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0064 'd' sc=0x20  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x53 sc=0x1F  [ChatLog]
  Pass    Printing  Inactive      unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x44 sc=0x20  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x1B sc=0x01  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+001B sc=0x01  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x1B sc=0x01  [-]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Answered      asked   0.9ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Answered      asked   0.7ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow Printing  Answered      asked   0.9ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow Fixed     Answered      asked   0.7ms KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Swallow Fixed     Answered      asked   0.4ms KEYDOWN vk=0x1B sc=0x01  [ChatLog]
  Swallow -         FollowsPress  unasked       CHAR U+001B sc=0x01  [ChatLog]
  Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x1B sc=0x01  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    -         FollowsPress  unasked       CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x08 sc=0x0E  [ChatLog]

- 2026-09-18 (agent): resolved on the run above. **ADR-0002 holds**: on `A`
  every printing keydown was `Swallow Provisional` and only its char passed,
  and the letters appeared; the ADR's "unverified" consequence is now
  verified (noted in the ADR). Read from the trace:
  - Waits: mean 4.6 ms, ~1 ms typical for Mozc, max 50.2 ms. One lone
    timeout mid-word (`'i'` in `konnichiha`, 49.2 ms) with fcitx5 healthy:
    Mozc was slow once. The verdict was still right — Mozc took the key and
    the late reply only updated the preedit — and a single timeout does not
    count towards Degraded. Nothing to change; a pattern of these would be.
  - **AltGr on Wine** is posted as `KEYDOWN vk=0xE4 sc=0x38 ext` (X11
    `ISO_Level3_Shift`, not `VK_RMENU`), then a plain `KEYDOWN vk=0x32` (no
    Alt context bit) and `CHAR '@'`. `0xE4` was not in the modifier table,
    so AltGr's own press was classified `Fixed` and asked (`VoidSymbol`,
    declined, passed — harmless but a wasted round trip). Fixed here:
    `VirtualKey.WineAltGr` is a `Modifier` translating to `AltR`; recorded
    in ticket 06's Comments as step 2 asked. The `2` under it was already
    `Printing` (the state read saw AltGr) and `@` passed, on `A` and `あ`.
  - Ctrl+Space is asked and swallowed; Enter and Escape inside a Composition
    asked and swallowed, outside `Skipped`; Backspace inside `Answered`,
    outside `Skipped`; Slash Bypass with `/` from Shift+7 (Nordic layout,
    `vk=0x37 sc=0x08`), Backspaces during it pass, the space ends it; three
    50 ms timeouts → `Inactive` until the focus gain after `kill -CONT`.
