# M2.8 — The Cursor follows a Native Write

Status: resolved
Type: task
Blocked by: 17

Ticket 17's finding: after a Commit is written into the Chat Box, the cursor
is drawn at the beginning of the written text. The first round showed the
next keystroke landed there too; the second fixed that and left the drawn
cursor at the end of the whole text. The Cursor — index, where the next key
goes, and the drawn cursor — must be after the written text.

## Cause

Two separate things, one per round:

1. **Where keys go** (settled in round 2). The input module keeps the text
   as a before/after split around the selection (`RawTextBeforeSelection` /
   `RawSelectedText` / `RawTextAfterSelection`, ticket 04), and that split,
   not `CursorPos`, is where its keystrokes edit. The game's `InsertText`
   splices **at the split** and then **rebuilds the split from `CursorPos`**
   (round 1: `ｃ` landed at byte 1 while `CursorPos` was 6, split came out
   16/0/4 = six code points). With the cursor written *after* the splice
   (ticket 09) the index moved but the split stayed where the splice began.
   Writing the cursor **before** the splice fixes it: round 2, `あえ|う` +
   `そ` → split 9/0/3, typing lands after そ. `InsertText("")` was tried as a
   fallback rebuild and does nothing; dropped.
2. **Where the cursor is drawn** (open). Neither the splice nor the cursor
   write moves the cursor node. `SetText` with the unchanged text does —
   **at the end of the text** (round 2: index 3 of 4, node at 137 = the
   text's end, measured 125). Ticket 04 read the same thing as "caret after
   は" because in step 3 the text ended there and step 4's 185 was taken for
   measurement drift; it was the end of `aこんにちはb`. So `SetText` is not
   the tool.

## Fix

The write is: cursor index → the game's `InsertText` → the component's
`UpdateTextSelection` at that index → the cursor index again. Round 3 chose
the redraw among three candidates behind a radio in the Chat Box tab
(removed since):

- **None** — the cursor written first, the game's splice, nothing after.
  Rounds 1–2 cannot tell whether `InsertText` itself places the cursor node
  at the split it rebuilds (in round 1 that was the old cursor, where the
  node already stood; in round 2 `SetText` overrode it).
- **SetText** — round 2's, for comparison: expected at the end of the text.
- **UpdateTextSelection** — `AtkTextInputEventInterface.UpdateTextSelection
  (TextSelectionInfo*)` on the component, the one virtual besides
  `GetOwnerNode` on the interface the module drives the component through —
  so presumably what a keystroke's cursor move goes through. Filled with the
  selection collapsed at the written index, the module's `TextLength`, and
  its `RawInputString` / `EvaluatedInputString` for the struct's two unnamed
  strings (the component holds a raw and an evaluated string, so that is
  the guess). Only while the module targets the Chat Box.

**UpdateTextSelection** drew the cursor at the written index; the other two
did not (round 3). It is hardcoded (`ChatBoxAccess.NotifySelection`) and
the radio and the per-call trace are gone.

Not taken: writing the module's split strings ourselves (the split is
already right); `ProcessKeyShortcut` with arrow keys (unknown whether
arrows are "shortcuts").

## Round 4: the text changes on focus loss — cause found, round 5 confirmed

The two runs below settle it. Both show the game's `InsertText` leaving the
module's **evaluated** input string as old + new (`そうそかなたう`) with
`TextLength` 7 to match, while its raw string is right (`そかなたう`) —
the game's own quirk, harmless on its own: run 2 (no notice) survives the
clicks. Round 3's notice passed that evaluated string as the struct's
second string, and the component copied it into its own evaluated string
(run 1's `evaluated: "そうそかなたう"` on the component right after the
write, absent in run 2). On the next focus-in the game rebuilds the box
from the component's evaluated string, cut to the raw length: `そうそかな`.

Fix: the notice passes the module's **raw** input string for both strings
(raw and evaluated are the same for the text the splice accepts — no
payloads) and the raw text's code-point count as `StringLength`, not
`TextLength`. The bisect checkbox and the focus-edge log lines are gone;
the module strings stay in the readout.

### Round 5 script

1. Empty box, compose `そう`, Enter; Left; compose `かなた`, Enter →
   `そかなたう`, cursor drawn after た. Click out of the box and back:
   **still `そかなたう`**. Copy report: the `after write:` block should have
   no `evaluated:` line (the component's evaluated string equals the raw).
2. Type `x`, Backspace, Left ×2, Right ×2: nothing disappears, cursor
   walks. Enter sends `そかなたう` intact.
3. Two commits back to back, click out and back, Enter.
4. As expected → `Status: resolved`, M2 ticked (the spec's M2 line loses
   its "stays open" note). If the text still changes, paste the report.

## Round 4 (done)

Found after round 3: `そう` written, Left, `かなた` written → `そかなたう`,
typed right; click out of the box and back → `そうそかな`. That is the old
text and the new one run together (`そう` + `そかなたう`) cut to the new
text's five code points. A buffer somewhere holds old + new. One number in
every round's log fits that: the module's `TextLength` after `InsertText` is
always **old + new** (ticket 04: `ab` → 9 for 7 code points = 2 + 7; round
1: 15 = 7 + 8; round 2: 7 = 3 + 4), and that is what round 3's
`NotifySelection` passes as `StringLength`. Suspects, in order:

1. `UpdateTextSelection` itself — new in round 3, and ticket 09's click-out
   check passed without it. Its `StringLength` is that old + new number;
   its two strings are a guess.
2. The cursor written before the splice (round 2) — `InsertText` with a
   cursor past the text may fill something else oddly.

The build for this round: the readout and every state in the report now
print the module's strings as text (`module strings: before= sel= after=
input= evaluated=`), the Chat Box's state goes to `dalamud.log` at each
focus edge (`Native Write: Chat Box focus out: …` / `focus in: …`), and the
Chat Box tab has a checkbox to leave the `UpdateTextSelection` notice out
of a write (the drawn cursor then stays behind — that is expected).

### Script

`/echo` channel; `/imebridge debug` → Chat Box tab open beside the box.

1. Checkbox **on** (default). Empty box, compose `そう`, Enter; Left;
   compose `かなた`, Enter → `そかなたう`. **Copy report** (it now carries
   the module's strings before, right after and two frames after the
   write). Click out of the box, click back in: note the text. Then from
   `dalamud.log` copy the two `Native Write: Chat Box focus out:` / `focus
   in:` blocks for this click.
2. Checkbox **off**. Clear the box (select all, delete, or send it), the
   same sequence. Does the text survive the click out and back? Copy the
   same three things.
3. Paste both sets below. Suspect 1 confirmed if step 2 survives; the
   strings say where `そうそかなたう` lives either way.

Safety: reads and log lines only, beyond what round 3 already did.

## In-game confirmation (human, round 3 — done)

`/echo` channel; nothing here sends. `/imebridge debug` → Chat Box tab,
**Show cursor markers** on; the radio is there too. For each candidate,
in this order:

1. **None**: `あえ`, Left, compose `そ`, Enter → `あえそう`. Where is the
   game's cursor drawn: after そ (right), or at the end / after え? Type
   `a`: still lands after そ (round 2's fix must hold). Copy report.
2. **UpdateTextSelection**: the same. Watch for the box's text changing to
   something other than `あえそう` — that would mean the two strings are
   not raw/evaluated. Copy report. If it misbehaves, a keystroke or a
   click out and back should restore the box; note whether it did.
3. **SetText**: the same, once, to confirm it is the end of the text and
   not the last written character.
4. With the best candidate: two commits back to back, then Left ×2 (nothing
   disappears), click out and back (text survives), Enter sends intact.
5. Paste the three reports; if one candidate draws it right, set
   `Status: resolved` and tick M2 in the spec. If none, the report's
   `after …` lines are the next lead.

Safety: `UpdateTextSelection` is a virtual call on the game's own interface
with a struct ClientStructs has laid out but not documented; the pointers
passed are the module's own live strings. A wrong guess shows as wrong text
in the box until the next keystroke, not as anything sent (ADR-0001).

## Comments

- 2026-09-21 (agent): implemented — `ChatBoxAccess.RedrawCursor` and the
  call in `NativeWriter.Run` between the splice and the cursor write; the
  class comment, `docs/dev-plugin.md`'s Native Write section and the spec's
  Native Write bullet say why both writes are needed. Host build clean, 291
  tests pass (nothing new to test on the host: the change is two game
  calls in order). Awaiting the in-game confirmation above.
- (human): cursor now displays at the end of the word, but text is entered at the old position at the beginning of the word. Also, pressing arrow left after a write deletes the last written word from the chat box. see logs:
"ｃ" Written: text is in the Chat Box but not where planned ("aｃこんいちはb"); cursor at byte 19 as expected
before: raw: "aこんいちはb" (17 bytes, 7 code points) active=True
        component cursor=6 sel=6..6; module cursor=6 len=9 sel=6..6 before/sel/after/input bytes=1/0/16/17
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (164,1353) 4×24; measured cursor x=156
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
plan: cursorByteOffset=19 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=7
after write: raw: "aｃこんいちはb" (20 bytes, 8 code points) active=True
        component cursor=7 sel=7..7; module cursor=7 len=15 sel=7..7 before/sel/after/input bytes=16/0/4/20
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (164,1353) 4×24; measured cursor x=167
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
next frame: raw: "aｃこんいちはb" (20 bytes, 8 code points) active=True
        component cursor=7 sel=7..7; module cursor=7 len=15 sel=7..7 before/sel/after/input bytes=16/0/4/20
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (175,1353) 4×24; measured cursor x=167
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
- 2026-09-21 (agent): the log above says more than the finding did: `ｃ`
  went in at byte 1 — the module's split (`before/sel/after = 1/0/16`, left
  by the previous write) — not at `CursorPos` 6, and the split came out as
  16/0/4, six code points before: the game's splice lands at the split and
  rebuilds the split from `CursorPos`. The cursor write after the splice
  moved the index only; the split, the next keystroke and the drawn cursor
  stayed at the start of the word, and Left rebuilt from the mismatched pair.
  Cause and Fix above rewritten: the cursor is now written *before* the
  splice, with `InsertText("")` stacked behind as the fallback rebuild and a
  per-call trace in the report. Host build clean, 291 tests pass. Second
  in-game round per the script above.
- (human): text is written correctly at the end of the last written candidate, but cursor is always drawn at the end of the whole chat box text. With the following logs, Cursor debug markers show blue after そ, and red after う. Cursor should be drawn where the blue marker is. 
"そ" Written: text is in the Chat Box; cursor at byte 9 as expected
before: raw: "あえう" (9 bytes, 3 code points) active=True
        component cursor=2 sel=2..2; module cursor=2 len=3 sel=2..2 before/sel/after/input bytes=6/0/3/9
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (113,1353) 4×24; measured cursor x=113
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
plan: cursorByteOffset=9 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=3
after InsertText: component cursor=3 sel=3..3; module cursor=3 len=7 sel=3..3 before/sel/after/input bytes=9/0/3/12
after empty InsertText: component cursor=3 sel=3..3; module cursor=3 len=7 sel=3..3 before/sel/after/input bytes=9/0/3/12
after write: raw: "あえそう" (12 bytes, 4 code points) active=True
        component cursor=3 sel=3..3; module cursor=3 len=7 sel=3..3 before/sel/after/input bytes=9/0/3/12
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (113,1353) 4×24; measured cursor x=125
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
next frame: raw: "あえそう" (12 bytes, 4 code points) active=True
        component cursor=3 sel=3..3; module cursor=3 len=7 sel=3..3 before/sel/after/input bytes=9/0/3/12
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=125
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
- 2026-09-21 (agent): round 2 settles half of it — the cursor written before
  the splice is honoured (`after InsertText: before/sel/after = 9/0/3`),
  typing lands after the written text; `InsertText("")` is a no-op and is
  gone. The other half: `SetText` draws the cursor at the **end of the
  text** (node 137 vs measured 125), and ticket 04's "after は" was that
  same end-of-text. Cause 2 and the Fix rewritten: round 3 puts the
  candidates behind a radio in the Chat Box tab — `None`, `SetText`,
  `UpdateTextSelection` (the module's own selection notice to the
  component, filled with the written index and the module's strings). Host
  build clean, 291 tests pass. Script above; three commits, one per radio.
- 2026-09-21 (human): `UpdateTextSelection` works perfectly; `None` and
  `SetText` do not.
- 2026-09-21 (agent): resolved. `NotifySelection` hardcoded after the
  splice, the radio, the `CursorRedraw` switch and the per-call trace
  removed; `docs/dev-plugin.md` and the spec's Native Write bullet describe
  the three-call write and why each call is there. M2 ticked in the spec.
  
- Keyboard trace:
Session: forwarding=on degraded=False gate=active input method=mozc glyph=あ seen=1459 swallowed=417 asked=12 wait max=1.1ms mean=0.7ms
Composition: preedit [] cursor byte=0 char=0; candidates [] selected=0 layout=NotSet prev=False next=False; aux up="" down=""
focus: textInputActive=True owner=ChatLog uiHidden=False windowActive=True → chatBoxFocused=True
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x53 sc=0x1F  [ChatLog]
Swallow Printing  Answered      asked   0.6ms CHAR U+0073 's' sc=0x1F  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4F sc=0x18  [ChatLog]
Swallow Printing  Answered      asked   0.6ms CHAR U+006F 'o' sc=0x18  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x53 sc=0x1F  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x55 sc=0x16  [ChatLog]
Swallow Printing  Answered      asked   0.6ms CHAR U+0075 'u' sc=0x16  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4F sc=0x18  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x55 sc=0x16  [ChatLog]
Swallow Fixed     Answered      asked   0.8ms KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
Swallow -         FollowsPress  unasked       CHAR U+000D sc=0x1C  [ChatLog]
Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x0D sc=0x1C  [ChatLog]
Pass    Fixed     Skipped       unasked       KEYDOWN vk=0x25 sc=0x4B ext  [ChatLog]
Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x25 sc=0x4B ext  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4B sc=0x25  [ChatLog]
Swallow Printing  Answered      asked   0.8ms CHAR U+006B 'k' sc=0x25  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
Swallow Printing  Answered      asked   1.1ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4B sc=0x25  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x4E sc=0x31  [ChatLog]
Swallow Printing  Answered      asked   0.6ms CHAR U+006E 'n' sc=0x31  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
Swallow Printing  Answered      asked   0.8ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x4E sc=0x31  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x54 sc=0x14  [ChatLog]
Swallow Printing  Answered      asked   0.8ms CHAR U+0074 't' sc=0x14  [ChatLog]
Swallow Printing  Provisional   unasked       KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
Swallow Printing  Answered      asked   0.8ms CHAR U+0061 'a' sc=0x1E  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x54 sc=0x14  [ChatLog]
Swallow Printing  FollowsPress  unasked       KEYUP vk=0x41 sc=0x1E  [ChatLog]
Swallow Fixed     Answered      asked   0.6ms KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
Swallow -         FollowsPress  unasked       CHAR U+000D sc=0x1C  [ChatLog]
Swallow Fixed     FollowsPress  unasked       KEYUP vk=0x0D sc=0x1C  [ChatLog]
Pass    -         FollowsPress  unasked       LBUTTONUP (861,840)  [-]
Pass    -         FollowsPress  unasked       LBUTTONUP (182,1362)  [ChatLog]
Pass    -         FollowsPress  unasked       LBUTTONUP (1238,234)  [ChatLog]
Pass    -         FollowsPress  unasked       LBUTTONUP (74,1362)  [ChatLog]
Pass    Modifier  Modifier      unasked       KEYDOWN vk=0x11 sc=0x1D  [ChatLog]
Pass    Fixed     Answered      asked   0.4ms KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
Pass    -         FollowsPress  unasked       CHAR U+0003 sc=0x2E  [ChatLog]
Pass    Fixed     FollowsPress  unasked       KEYUP vk=0x43 sc=0x2E  [ChatLog]
Pass    Modifier  FollowsPress  unasked       KEYUP vk=0x11 sc=0x1D  [ChatLog]
Pass    -         FollowsPress  unasked       LBUTTONUP (1142,237)  [ChatLog]
Pass    -         FollowsPress  unasked       LBUTTONUP (1603,354)  [ChatLog]

- chat box:
raw: "そうそかな" (15 bytes, 5 code points) active=True
component cursor=0 sel=5..0; module cursor=0 len=5 sel=0..5 before/sel/after/input bytes=0/15/0/15
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (89,1353) 4×24; measured cursor x=89
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
preedit: not drawn (compose in the Chat Box to place it)
last native write:
"かなた" Written: text is in the Chat Box; cursor at byte 12 as expected
before: raw: "そう" (6 bytes, 2 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=2 sel=1..1 before/sel/after/input bytes=3/0/3/6
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=101
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
plan: cursorByteOffset=12 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=4
after write: raw: "そかなたう" (15 bytes, 5 code points) active=True
        evaluated: "そうそかなたう"
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
next frame: raw: "そかなたう" (15 bytes, 5 code points) active=True
        evaluated: "そうそかなたう"
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E

- Text in chat box after click away and back in: そうそかな
- 2026-09-21 (human): another bug: `そう`, Left, `かなた` goes to the right
  place, but clicking away and back turns the text into `そうそかな`.
- 2026-09-21 (agent): reopened; analysis and the round-4 script above. The
  build prints the module's strings as text in the readout and the report,
  logs the Chat Box's state at each focus edge, and has a checkbox to leave
  `UpdateTextSelection` out of a write for the bisect. Host build clean, 291
  tests pass.

1. "かなた" Written: text is in the Chat Box; cursor at byte 12 as expected
before: raw: "そう" (6 bytes, 2 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=2 sel=1..1 before/sel/after/input bytes=3/0/3/6
        module strings: before="そ" sel="" after="う" input="そう" evaluated="そう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=101
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
plan: cursorByteOffset=12 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=4
after write: raw: "そかなたう" (15 bytes, 5 code points) active=True
        evaluated: "そうそかなたう"
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
next frame: raw: "そかなたう" (15 bytes, 5 code points) active=True
        evaluated: "そうそかなたう"
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
- From dalamud.log:
[INF] [FfxivImeBridge] Native Write: Chat Box focus out: raw: "そかなたう" (15 bytes, 5 code points) active=True
evaluated: "そうそかなたう"
component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus in: raw: "そかなたう" (15 bytes, 5 code points) active=True
evaluated: "そうそかなたう"
component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus out: raw: "そかなたう" (15 bytes, 5 code points) active=False
evaluated: "そうそかなたう"
component cursor=4 sel=4..4; module (not targeting the Chat Box) cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (137,1353) 4×24 hidden; measured cursor x=137
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus in: raw: "そうそかな" (15 bytes, 5 code points) active=True
component cursor=5 sel=5..5; module cursor=5 len=5 sel=5..5 before/sel/after/input bytes=15/0/0/15
module strings: before="そうそかな" sel="" after="" input="そうそかな" evaluated="そうそかな"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (149,1353) 4×24; measured cursor x=149
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus out: raw: "そうそかな" (15 bytes, 5 code points) active=True
component cursor=5 sel=5..5; module cursor=5 len=5 sel=5..5 before/sel/after/input bytes=15/0/0/15
module strings: before="そうそかな" sel="" after="" input="そうそかな" evaluated="そうそかな"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (149,1353) 4×24; measured cursor x=149
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E

2:
"かなた" Written: text is in the Chat Box; cursor at byte 12 as expected
before: raw: "そう" (6 bytes, 2 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=2 sel=1..1 before/sel/after/input bytes=3/0/3/6
        module strings: before="そ" sel="" after="う" input="そう" evaluated="そう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=101
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
plan: cursorByteOffset=12 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=4
after write: raw: "そかなたう" (15 bytes, 5 code points) active=True
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
next frame: raw: "そかなたう" (15 bytes, 5 code points) active=True
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E


dalamud.log:
[INF] [FfxivImeBridge] Native Write: Chat Box focus out: raw: "そかなたう" (15 bytes, 5 code points) active=True
component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus in: raw: "そかなたう" (15 bytes, 5 code points) active=True
component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus out: raw: "そかなたう" (15 bytes, 5 code points) active=False
component cursor=4 sel=4..4; module (not targeting the Chat Box) cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (137,1353) 4×24 hidden; measured cursor x=137
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus in: raw: "そかなたう" (15 bytes, 5 code points) active=True
component cursor=5 sel=5..5; module cursor=5 len=5 sel=5..5 before/sel/after/input bytes=15/0/0/15
module strings: before="そかなたう" sel="" after="" input="そかなたう" evaluated="そかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (149,1353) 4×24; measured cursor x=149
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
[INF] [FfxivImeBridge] Native Write: Chat Box focus out: raw: "そかなたう" (15 bytes, 5 code points) active=True
component cursor=5 sel=5..5; module cursor=5 len=5 sel=5..5 before/sel/after/input bytes=15/0/0/15
module strings: before="そかなたう" sel="" after="" input="そかなたう" evaluated="そかなたう"
limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
text node (89,1355) 563×22; cursor node (149,1353) 4×24; measured cursor x=149
style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
- 2026-09-21 (human): tests done (logs above); the text survives the clicks
  with the checkbox unticked.
- 2026-09-21 (agent): cause found in the logs — the game's `InsertText`
  leaves the module's evaluated string as old + new, the notice copied it
  onto the component, and the next focus-in rebuilds the box from there.
  The notice now passes the raw string twice and the real code-point count;
  the bisect and the focus trace are removed. Host build clean, 291 tests
  pass. Round 5 script above.
- Round 5 (human) all steps as expected:
  Step 1 behaves as expected: 
  "かなた" Written: text is in the Chat Box; cursor at byte 12 as expected
before: raw: "そう" (6 bytes, 2 code points) active=True
        component cursor=1 sel=1..1; module cursor=1 len=2 sel=1..1 before/sel/after/input bytes=3/0/3/6
        module strings: before="そ" sel="" after="う" input="そう" evaluated="そう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=101
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
plan: cursorByteOffset=12 appendedAtEnd=False charsOver=0 bytesOver=0 expectedCursorIndex=4
after write: raw: "そかなたう" (15 bytes, 5 code points) active=True
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (101,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
next frame: raw: "そかなたう" (15 bytes, 5 code points) active=True
        component cursor=4 sel=4..4; module cursor=4 len=7 sel=4..4 before/sel/after/input bytes=12/0/3/15
        module strings: before="そかなた" sel="" after="う" input="そかなたう" evaluated="そうそかなたう"
        limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
        text node (89,1355) 563×22; cursor node (137,1353) 4×24; measured cursor x=137
        style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
- 2026-09-21 (human): round 5 all as expected.
- 2026-09-21 (agent): resolved. The Native Write is: cursor index →
  `InsertText` → `UpdateTextSelection` (raw string twice, real count, the
  written index) → cursor index. M2 ticked in the spec.
