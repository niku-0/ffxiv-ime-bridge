# M1.5 — Commit becomes a Native Write

Status: resolved
Type: task
Blocked by: 07

`CommitString` from fcitx5 goes into the Chat Box at the Cursor with the
M0.4 mechanism, hardened to what ticket 04 found. ADR-0001.

## Done when

- `NativeWrite` module replaces `NativeBuffer`: cursor source hardcoded
  (module `CursorPos` when the Chat Box is the module's target, else the
  component's), unit code points, `SetText` + cursor write; the
  `CursorSource`/`CursorUnit` switches, the `InsertText` path and the
  `/imebridge insert*` commands are removed. Names follow `CONTEXT.md`
  (`NativeWrite`, `NativeWriter`, no "insert"/"buffer").
- Fed from ticket 05's commit queue, drained in two places, both on the
  game thread: the hook right after the waited `ProcessKeyEvent` that
  produced the commit (ticket 07 — this ordering is why the write cannot
  be deferred to the tick: a later passed key would land first), and the
  framework tick for a commit that arrives out of band (a reply after its
  timeout, or fcitx5 committing on its own).
- Overflow: 500-byte limit read from the input (`MaxByte`, handler value
  first); refused before the write with a chat line stating bytes over
  and, for the user, the approximate character count (3 bytes per kana/
  kanji). Never truncated. The refused text is logged.
- A commit while the Chat Box is not focused (focus lost between the key
  and the reply, or an out-of-band commit): written anyway if the Chat Box
  exists — the text is the user's; logged if it cannot be.
- The Chat Box debug tab keeps the live readout and Copy readout; the
  insert buttons go. `docs/dev-plugin.md`'s "Native Chat Box buffer (M0.4)"
  section is replaced by a short Native Write section.
- Host tests: `ChatBoxSplice`/`CursorIndex` tests stay; the state tests
  drop the source/unit cases.

## In-game confirmation (human)

M0.4 wrote from a command handler (tick time). This ticket writes from
inside `DispatchMessageW`; step 1 checks that the write holds from there.

1. Forwarding on, `あ`, `konnichiha`, Enter: `こんにちは` lands in the box
   at the caret, caret after it, **and is still there on the next frame**
   (the M0.4 readout's "next frame" line; if it reverts, the write from the
   pump is the finding). Enter again: sends, Japanese intact in `/echo`.
2. Mid-string: `ab`, Left, compose, Enter → `aこんにちはb`.
3. Overflow: fill the box to ~495 bytes (paste), compose `こんにちは`,
   Enter: chat prints the refusal with bytes and ~characters, box untouched.
4. Focus loss mid-composition: compose, click out of the box: nothing is
   written (05's `Reset` before `FocusOut`), nothing sent.
5. **Focus loss after a write** (the 2026-09-18 finding): compose, Enter
   (`こんにちは` lands), click out of the box, click back in: the text is
   **still there**; Enter sends it. Then the same mid-string (`ab`, Left,
   compose, Enter, click out, back): `aこんにちはb` intact, caret still
   usable. If the text still vanishes, copy the Chat Box tab's report
   (the "next frame" line and the module's before/sel/after/input bytes)
   into the Comments.
6. Set `Status: resolved`.

Safety: the only game-memory writes are the game's own `InsertText` (the
auto-translate menu's path; `SetText` only in the append-at-end fallback)
and the M0.4 cursor write, now triggered by a Commit instead of a command;
nothing is sent (ADR-0001).

## Comments

- 2026-09-18 (agent): implemented; awaiting the in-game confirmation above
  (step 1 is the write-from-the-pump check). Host-tested: the splice and
  code-point cursor tests stay, the state tests are now module-vs-component
  cursor + limits, and `tests/FfxivImeBridge.Tests/OverflowTests.cs` pins the
  refusal wording; 219 in the plugin suite, 17 in the library's.
  - `src/FfxivImeBridge/NativeWrite/`: `NativeWriter.Write(text)` (find the
    Chat Box → `ChatBoxAccess.Read` → `ChatBoxSplice.Plan` → `SetText` +
    `SetCursor` → read back, and two ticks later the next-frame read; every
    outcome to the log as `Native Write: …`, a `WriteReport` for the debug
    tab), `Overflow.Describe` (pure), `CursorIndex` code points only,
    `ChatBoxState.CursorIndex`/`CursorByteOffset` with the source hardcoded
    (module while it targets the Chat Box, else the component). `CursorSource`,
    `CursorUnit`, `InsertMethod`, `ChatBoxInserter`, `ChatBoxAccess.InsertText`
    and `/imebridge insert*` are gone; `/imebridge debug` opens the window.
  - Drains: `Bridge.SettleAfterReply` (hook, after each waited reply) and
    `Bridge.OnUpdate` (tick) both call `writer.Write` for every queued Commit,
    in order. `NativeWriter.Write` never throws (the hook is the game's
    message loop); a second write while the first's next-frame read is pending
    is written at once and takes over the report.
  - Chat line only on Overflow: `IME Bridge: the committed text does not fit:
    13 bytes (about 5 characters) over the limit of 500 bytes` (characters =
    bytes over ÷ 3, rounded up: the least to remove). The refused text goes to
    the log at Warning. A successful write prints nothing; a missing Chat Box
    logs the text at Warning. Focus is not checked; the report notes `(Chat
    Box not focused)` when the module was not targeting the box.
  - Choices worth knowing: `ChatBoxLimits` keeps both axes (the char axis
    reads 0 in-game and never fires; the limit tests stay); a Commit arriving
    with Forwarding off is still written (the text is the user's); the
    "next frame" line is kept on purpose for step 1.
  - Not here: what to do with a Commit while a Slash Bypass is in progress
    (not reachable: bypass chars are never asked), and mid-write cursor drift
    from Wine (step 1 checks).
- 2026-09-18 (human): steps 1–4 pass — `こんにちは` lands at the caret, holds
  on the next frame, Enter sends it and `/echo` shows it intact. **Finding:**
  after a write, clicking out of the Chat Box makes the written Japanese
  disappear (typed text survives).
- 2026-09-18 (agent): cause, from ticket 04's own numbers: `SetText` fills
  the component's string but leaves the input module's copy
  (`AtkTextInput.RawInputString` and the before/sel/after split) untouched
  — `0/0/0/0`, `TextLength=0` after a 15-byte write — and the game hands
  that copy back to the component on focus loss. Keystrokes re-read the
  component, so typing and Enter worked. Fix: the write now goes through
  the game's own `AtkComponentTextInput.InsertText(text, unique: false)`
  (which ticket 04 saw refresh the module's strings: `0/0/15/15`) followed
  by the same cursor write; `SetText` remains only for the append-at-end
  fallback, where the game's cursor cannot be trusted. The readback verdict
  now also compares the whole text with the plan, so a game splice landing
  elsewhere would show as "not where planned". Step 5 above is the re-check.
  ClientStructs exposes no way to refresh the module's copy directly
  (`AtkTextInput` has only `OpenCompletion`/`ProcessKeyShortcut`;
  `AtkComponentInputBase.WriteString(Utf8String*)` looks like the
  module→component hand-back itself), so the game's splice is the least
  speculative route.
- 2026-09-18 (human): step 5 passes with the `InsertText` write — the text
  survives clicking out of the Chat Box and back, and sends. Resolved.

