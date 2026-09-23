# M0.4 — Write into the native Chat Box at the cursor

Status: resolved
Type: prototype
Blocked by: 02

Prove the ADR-0001 assumption: the plugin can insert text into the game's
chat input at the cursor without breaking the native input.

## Done when

- Reads the ChatLog addon's text input (`AtkComponentTextInput`): current
  UTF-8 text, cursor index, max length. Note which FFXIVClientStructs
  fields/methods were used and their reliability.
- `/imebridge debug insert こんにちは` splices the text at the cursor via
  `SetText`, and the cursor ends up after the inserted text. Then native
  Enter sends it and Japanese appears correctly in the chat log.
- Mid-string insert and insert-at-end both work; input history is unaffected.
- Screen position of the cursor derived (text node position + measured
  width of text before the cursor, or the input's own cursor node) — this
  is where the preedit will anchor. Record how accurate it is.
- Overflow: inserting past the game's limit is detected *before* `SetText`
  (max-length field) and refused with a count.
- If the cursor index is not readable/writable, record it and fall back to
  append-at-end per spec.

## Answer

**Yes — confirmed in-game 2026-09-18** (logs in Comments): the plugin
reads the Chat Box's text and cursor, splices at the cursor with `SetText`,
puts the cursor after the inserted text, and native Enter sends the
Japanese intact. Per done-when item:

- **Reading the input.** `AddonChatLog.TextInput` is a direct
  `AtkComponentTextInput*` (no node walk), valid focused or not. Text:
  `AtkComponentInputBase.RawString` (UTF-8; "can contain unevaluated fixed
  macros" — auto-translate payloads, not exercised). Cursor: the
  component's `CursorPos`/`SelectionStart`/`SelectionEnd` (`int`) and the
  input module's `AtkModule.TextInput.CursorPos`/`SelectionStart`/
  `SelectionEnd` (`short`, live iff `TargetTextInputEventInterface ==
  &input->AtkTextInputEventInterface`) **agree and are both live**, and the
  index is in **code points** (`aあ`, cursor at end → 2). The module's
  `RawTextBeforeSelection`/`RawSelectedText`/`RawTextAfterSelection` byte
  lengths track typing (1/0/2 for `a|bc`) but are **not refreshed by
  `SetText`** (still 0/0/0, `TextLength=0`, after writing 15 bytes) — they
  are a view the game rebuilds on its own edits, not state it reads back;
  don't depend on them. Limits: `ComponentTextData.MaxByte=500`,
  `MaxChar=0`, `MaxLine=0`, `MaxWidth=0`; `HandlerValues.MaxByte=500`,
  `MaxChar=0` (int-typed zeros); `GetInputMaxLength()=500`. The Chat Box's
  limit is **500 bytes**, not characters.
- **Insert via `SetText` + cursor write: works.** At end: `""` →
  `こんにちは`, cursor 0 → 5 (byte 15), held at the next frame, caret drawn
  after は (node x 170 = measured 170). Mid-string: `a|b` → `aこんにちはb`,
  cursor 1 → 6 (byte 16), held, caret after は. Enter sent the Japanese
  correctly; input history intact. The one speculative write — the module's
  cursor fields — sticks; whether it was even needed (the component field
  alone might do) was not isolated.
- **The game's `InsertText(text, unique: false)`: splices at the cursor
  but leaves the cursor where it was** (0 → 0, 1 → 1; it does update the
  module's split strings and `TextLength`, oddly 9 for 7 code points). So
  it is not a drop-in; the `SetText` splice stays the M1 primitive.
  (`InsertText` + our cursor write would also work but buys nothing.)
- **Cursor screen position: use the caret node.**
  `AtkComponentInputBase.CursorContainer` (`ScreenX/Y`, 4×24) sits exactly
  on the drawn caret (≤1 px), follows horizontal scrolling once the text is
  wider than the box, and stays right across input font sizes and box
  resizes. The measured derivation (text node `ScreenX` +
  `GetTextDrawSize` on the prefix) matches for plain runs (117, 129, 170)
  but ignores scrolling (x=3596 at 498 chars) and was 8 px short after a
  mixed Latin/CJK prefix (177 vs 185). Anchor the preedit to the caret
  node; keep the measurement only as a fallback if the node is ever null.
  The text node reports `hidden` while the box is empty; the caret node
  does not.
- **Overflow: refused before `SetText`.** 498 bytes + 15 → `refused: 13
  bytes over the limit of 500`, box untouched. The run also showed the
  handler's int-typed `MaxChar=0` being taken as "set to 0", masking the
  `GetInputMaxLength()` fallback; harmless because bytes is the real
  limit, fixed anyway (a handler 0 now means "unset").
- **Fallback:** not needed — the cursor is readable and writable.
  Append-at-end stays for an unusable cursor and for payload-bearing
  buffers (untested).

**For M1:** hardcode `CursorSource.Module` (or `Component`; equal) and
`CursorUnit.CodePoints`, drop the switches and the `InsertText` path,
anchor on `CursorContainer`, and treat the limit as 500 bytes.

Code: `src/FfxivImeBridge/NativeBuffer/` (`ChatBoxSplice` pure,
`CursorIndex`, `ChatBoxState`, `ChatBoxAccess` unsafe, `ChatBoxInserter`),
the Chat Box tab in `DebugWindow`, `insert`/`inserttext` commands. How to
test: `docs/dev-plugin.md`, "Native Chat Box buffer (M0.4)".

### In-game confirmation (done 2026-09-18)

Use a channel where stray text is harmless (`/echo`). Nothing here sends;
you press Enter yourself.

1. Rebuild, reload the dev plugin, `/imebridge debug` → **Chat Box** tab.
   Click into the chat box: the readout should turn green (module targets
   the Chat Box). Note the `limits:` line — which of `GetInputMaxLength`,
   `maxChar`, `maxByte`, handler values are non-zero, and what they are.
2. **Cursor unit and source.** Type `abc`, press Left twice, read
   `component cursor=` and `module cursor=`. Which one moved to 1? Paste or
   auto-translate something multi-byte if you can (or skip): with `aあ` and
   the cursor at the end, does the live one read 2 (code points) or 4
   (bytes)? Set the **Cursor from** / **Index unit** radios accordingly. Also
   note whether `before/sel/after` bytes track the cursor as you move it.
3. **Insert at end.** Clear the box, `/imebridge insert`, click back into
   the box, wait. Expect `こんにちは` in the box, chat line `IME Bridge:
   SetText — text is in the buffer; cursor 5 as expected`. Is the caret
   drawn after the text? Press Enter: does `こんにちは` appear correctly in
   the log (not `?????`)? Press Up: is history intact (the previous entries,
   and this one)?
4. **Mid-string.** `/imebridge insert`, click into the box, type `ab`,
   press Left once, wait. Expect `aこんにちはb`, caret between `は` and `b`,
   report cursor `6 as expected`. If the report says "expected 6" but the
   cursor reads something else, or the caret is drawn elsewhere, copy the
   report — that is the "cursor not writable" case and the answer becomes
   append-at-end. Type one more letter to see where it lands.
5. **The game's own InsertText.** Repeat 3 and 4 with `/imebridge
   inserttext`. Does it splice at the cursor and leave the caret after the
   text on its own?
6. **Overflow.** Type or paste enough to be within 5 characters of the
   limit found in step 1 (or lower the check: note if the limits read 0 —
   then the check cannot fire and that is a finding), `/imebridge insert`.
   Expect `IME Bridge: SetText — refused: N characters over the limit of M`
   and an untouched box.
7. **Cursor position.** Tick **Show cursor markers**, click into the box,
   type and move the cursor. Does the red line (cursor node) sit on the
   caret? Does the blue one (measured width)? By how many pixels are they
   off, and does that change with the HUD scale of the chat window?
8. **Copy readout** (the live readout, the switches and the last report as
   text) after steps 1, 2, 4, 5 and 7, paste them below; if 3–6 behave, set `Status: resolved` and tick M0.4 in
   the spec.

Safety notes for that run: the plugin reads the ChatLog addon's text input
through ClientStructs' public surface, writes into it only when you run
`insert`/`inserttext`, and never sends (Enter is never synthesised;
ADR-0001 stands). Writing the module's cursor fields while the box is
focused is the one speculative write; a wrong value is at worst a misplaced
caret until the next keystroke. Unloading the plugin leaves nothing behind.

## Comments

- 2026-09-18: built and host-tested (35 tests on the splice, cursor-unit
  conversion, limits and the cursor-source fallback). Cursor source and unit are
  runtime switches because ClientStructs documents neither; the in-game run
  above decides them, and M1 should then hardcode the answer and drop the
  switches. `InsertText` is offered alongside the spec's `SetText` splice
  on purpose: if the game keeps the cursor right by itself, M1 should
  prefer it.
- 2026-09-18 code review: "insert"/"buffer" are on `CONTEXT.md`'s avoid
  lists (for Commit and Preedit); here they name the mechanical write into
  the game's input, which the glossary has no term for. Candidate for
  `/domain-modeling` before M1 hardens `ChatBoxInserter`/`NativeBuffer`
  (e.g. *Native Write*), together with the M0.3 terms.
  → Done 2026-09-18: **Native Write** and **Cursor** are in `CONTEXT.md`
  ("insert" and "caret" are on the avoid lists). The M0 prototype names
  (`NativeBuffer`, `ChatBoxInserter`, `InsertMethod`, `/imebridge insert`)
  stay until M1 replaces them; `InsertText` is the game's own method name
  and may keep it.
- human test step logs:
  1: cursor source=Module unit=CodePoints delay=3s
  raw: "" (0 bytes, 0 code points) active=True
  component cursor=0 sel=0..0; module cursor=0 len=0 sel=0..0 before/sel/after/input bytes=0/0/0/0
  limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
  text node (110,1344) 517×22 hidden; cursor node (110,1342) 4×24; measured cursor x=110
  
  2: cursor source=Module unit=CodePoints delay=3s
  raw: "abc" (3 bytes, 3 code points) active=True
  component cursor=1 sel=1..1; module cursor=1 len=3 sel=1..1 before/sel/after/input bytes=1/0/2/3
  limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
  text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=117
  
  cursor source=Module unit=CodePoints delay=3s
  raw: "aあ" (4 bytes, 2 code points) active=True
  component cursor=2 sel=2..2; module cursor=2 len=2 sel=2..2 before/sel/after/input bytes=4/0/0/4
  limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
  text node (110,1344) 517×22; cursor node (129,1342) 4×24; measured cursor x=129

  3:cursor source=Module unit=CodePoints delay=3s
raw: "こんにちは" (15 bytes, 5 code points) active=True
component cursor=5 sel=5..5; module cursor=5 len=0 sel=5..5 before/sel/after/input bytes=0/0/0/0
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (110,1344) 517×22; cursor node (170,1342) 4×24; measured cursor x=170
last insert:
SetText "こんにちは" source=Module unit=CodePoints
outcome: text is in the buffer; cursor at byte 15 as expected
before: raw: "" (0 bytes, 0 code points) active=True
        component cursor=0 sel=0..0; module cursor=0 len=0 sel=0..0 before/sel/after/input bytes=0/0/0/0
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22 hidden; cursor node (110,1342) 4×24; measured cursor x=110
plan: cursorByteOffset=15 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=5
after write: raw: "こんにちは" (15 bytes, 5 code points) active=True
        component cursor=5 sel=5..5; module cursor=5 len=0 sel=5..5 before/sel/after/input bytes=0/0/0/0
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (110,1342) 4×24; measured cursor x=170
next frame: raw: "こんにちは" (15 bytes, 5 code points) active=True
        component cursor=5 sel=5..5; module cursor=5 len=0 sel=5..5 before/sel/after/input bytes=0/0/0/0
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (170,1342) 4×24; measured cursor x=170

4: cursor source=Module unit=CodePoints delay=3s
raw: "aこんにちはb" (17 bytes, 7 code points) active=True
component cursor=6 sel=6..6; module cursor=6 len=2 sel=6..6 before/sel/after/input bytes=1/0/1/2
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (110,1344) 517×22; cursor node (185,1342) 4×24; measured cursor x=177
last insert:
SetText "こんにちは" source=Module unit=CodePoints
outcome: text is in the buffer; cursor at byte 16 as expected
before: raw: "ab" (2 bytes, 2 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=2 sel=1..1 before/sel/after/input bytes=1/0/1/2
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=117
plan: cursorByteOffset=16 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=6
after write: raw: "aこんにちはb" (17 bytes, 7 code points) active=True
        component cursor=6 sel=6..6; module cursor=6 len=2 sel=6..6 before/sel/after/input bytes=1/0/1/2
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=177
next frame: raw: "aこんにちはb" (17 bytes, 7 code points) active=True
        component cursor=6 sel=6..6; module cursor=6 len=2 sel=6..6 before/sel/after/input bytes=1/0/1/2
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (185,1342) 4×24; measured cursor x=177

5: cursor source=Module unit=CodePoints delay=3s
raw: "こんにちは" (15 bytes, 5 code points) active=True
component cursor=0 sel=0..0; module cursor=0 len=5 sel=0..0 before/sel/after/input bytes=0/0/15/15
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (110,1344) 517×22; cursor node (110,1342) 4×24; measured cursor x=110
last insert:
InsertText "こんにちは" source=Module unit=CodePoints
outcome: text is in the buffer; cursor at byte 0
before: raw: "" (0 bytes, 0 code points) active=True
        component cursor=0 sel=0..0; module cursor=0 len=0 sel=0..0 before/sel/after/input bytes=0/0/0/0
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22 hidden; cursor node (110,1342) 4×24; measured cursor x=110
after write: raw: "こんにちは" (15 bytes, 5 code points) active=True
        component cursor=0 sel=0..0; module cursor=0 len=5 sel=0..0 before/sel/after/input bytes=0/0/15/15
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (110,1342) 4×24; measured cursor x=110
next frame: raw: "こんにちは" (15 bytes, 5 code points) active=True
        component cursor=0 sel=0..0; module cursor=0 len=5 sel=0..0 before/sel/after/input bytes=0/0/15/15
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (110,1342) 4×24; measured cursor x=110

cursor source=Module unit=CodePoints delay=3s
raw: "aこんにちはb" (17 bytes, 7 code points) active=True
component cursor=1 sel=1..1; module cursor=1 len=9 sel=1..1 before/sel/after/input bytes=1/0/16/17
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=117
last insert:
InsertText "こんにちは" source=Module unit=CodePoints
outcome: text is in the buffer; cursor at byte 1
before: raw: "ab" (2 bytes, 2 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=2 sel=1..1 before/sel/after/input bytes=1/0/1/2
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=117
after write: raw: "aこんにちはb" (17 bytes, 7 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=9 sel=1..1 before/sel/after/input bytes=1/0/16/17
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=117
next frame: raw: "aこんにちはb" (17 bytes, 7 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=9 sel=1..1 before/sel/after/input bytes=1/0/16/17
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (117,1342) 4×24; measured cursor x=117

6: cursor source=Module unit=CodePoints delay=3s
raw: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" (498 bytes, 498 code points) active=True
component cursor=498 sel=498..498; module cursor=498 len=498 sel=498..498 before/sel/after/input bytes=498/0/0/498
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (110,1344) 517×22; cursor node (596,1342) 4×24; measured cursor x=3596
last insert:
SetText "こんにちは" source=Module unit=CodePoints
outcome: refused: 13 bytes over the limit of 500
before: raw: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" (498 bytes, 498 code points) active=True
        component cursor=498 sel=498..498; module cursor=498 len=498 sel=498..498 before/sel/after/input bytes=498/0/0/498
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (110,1344) 517×22; cursor node (596,1342) 4×24; measured cursor x=3596
plan: cursorByteOffset=498 appendedAtEnd=False charsOver=0 bytesOver=13 expectedCursorIndex=-

7: They are both exactly at cursor pos initially or maybe 1 pixel to the left. When text is longer than the chat input box width and it starts scrolling, red stays at cursor but blue keeps going to the right. Cant scale the chat box element, but when changing the input font size or dragging the box to be bigger, the cursors behave consistently.
- 2026-09-18: closed out from the logs above. Steps 1–7 all behaved; the
  spec's ADR-0001 section, Rendering anchor, M0.4 milestone and the risk
  list are updated. Unblocks M1.
