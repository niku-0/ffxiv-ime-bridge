# M1.2 — Key translation: Windows messages to fcitx5 key events

Status: resolved
Type: task
Blocked by: 04

Pure mapping from a `KeyMessage` to the `KeyEvent` fcitx5 wants, and the
classification the Gate needs (ADR-0002, spec "Keyboard capture"). No
Dalamud, no D-Bus: host-tested like `KeyboardGate`.

## Done when

- `KeyClass` of a `KeyMessage`: `Printing` (its keysym only exists at the
  `WM_CHAR`), `Fixed` (non-printing key or Ctrl/Alt chord with a fixed
  keysym: Enter, Escape, Tab, Backspace, Delete, arrows, Home/End/PgUp/PgDn,
  F1–F24, Space with Ctrl, letters with Ctrl/Alt…), `Modifier` (Shift,
  Ctrl, Alt, Win, Caps/Num/Scroll Lock alone), `DeadChar`.
- **AltGr is `Printing`, not `Fixed`.** A keydown with Ctrl *and* Alt held
  (or right Alt, `sc=0x38 ext`) is how a Nordic layout types `@{}[]\|~€`; its
  keysym is the character at its `WM_CHAR`/`WM_SYSCHAR`, with the modifier
  state as read. The M0.3 trace has no AltGr line, so record in Comments
  what Wine actually posts for AltGr+2 (which messages, which vk/sc, Sys or
  not) the first time it is seen — ticket 07's human run asks for it.
- `KeyEvent` from a `WM_CHAR`: keysym `KeySym.FromCodePoint`, keycode
  `KeyCode.FromWindowsScanCode(sc, extended)` from the char's lParam,
  modifier state from a `KeyStateReader` (Shift/Ctrl/Alt/Caps/Num) passed
  in, `isRelease=false`. The reader in the plugin wraps Win32 `GetKeyState`
  (message-synchronous, right inside the pump) — not Dalamud's `IKeyState`,
  which is the game's array downstream of the messages we drop.
- Surrogate halves (`WM_CHAR` with a UTF-16 surrogate): no Windows layout
  under Wine produces them, and a half cannot be asked or passed alone.
  Rule: log at Information and pass; no pairing logic.
- `KeyEvent` from a `Fixed` keydown: X keysym table for the non-printing
  keys (`XK_Return`, `XK_Escape`, `XK_BackSpace`, `XK_Tab`, `XK_Left`…,
  `XK_F1`…, `XK_space`), Latin keysym for Ctrl/Alt+letter derived from the
  vk (layout-independent for letters and digits), keycode from the scancode.
- `KeyEvent` for a release from its press (`AsRelease`), keeping keysym and
  keycode.
- `SkipsOutsideComposition(KeyMessage)`: the spec's list, false when Ctrl or
  Alt is held.
- Tests: every class has a case; Nordic-layout specifics from the M0.3 trace
  (`§` is `vk=0xDE sc=0x29`) and Enter/Escape/Backspace chars are covered;
  AltGr+2 classifies as `Printing`; a surrogate half is passed and logged.

## Notes

- Timestamps: fcitx5's `time` argument is optional in practice (README);
  use `Environment.TickCount` masked to `uint`.
- Nothing here decides swallow-or-pass; that is ticket 07.

## Comments
- 2026-09-18 implemented (host-tested, 98 cases in
  `tests/FfxivImeBridge.Tests/KeyTranslationTests.cs`): `Capture/KeyTranslation`
  (`KeyClass`, `Classify`, `FromChar`, `FromKey`, `SkipsOutsideComposition`,
  `Timestamp`), `Capture/Modifiers` (the snapshot, `ToKeyState`),
  `Capture/IKeyStateReader` + `Win32KeyStateReader` (`GetKeyState`, not wired
  until ticket 07), `KeyMessage.IsSurrogateHalf`; `KeySym` gains the
  Insert/F-key/keypad/lock keysyms and `VoidSymbol`. Notes for ticket 07:
  - `FromChar` returns `null` for a surrogate half; the Gate logs at
    Information and passes it (translation is pure, no logger here).
  - `FromKey` translates `Fixed` keydowns and `Modifier` presses/releases
    only. A `Printing` press comes from `FromChar`; every non-modifier
    release is the stored press's `AsRelease()` — never a fresh translation,
    because the modifiers may have changed (Ctrl+A, Ctrl up, A up would
    reclassify as `Printing`; review caught this, `FromKey` throws there).
    Control-character chars (Enter's U+000D, Ctrl+V's U+0016) translate but
    are never asked at: they go where their `Fixed` keydown went. A chord on
    a layout-specific key (Ctrl+§) has `KeySym.VoidSymbol` (expected to be
    declined; unverified).
  - AltGr is recognised as Ctrl+Alt in the read state (Windows), right Alt
    alone (`VK_RMENU`), or Ctrl in the state with the message's Alt context
    bit (a `WM_SYSKEYDOWN` while Ctrl is held). Which of these Wine actually
    posts for AltGr+2 is still unrecorded — ticket 07's human run step 2
    pastes the trace lines here. The state sent is Ctrl|Alt ("as read"),
    not X11's Mod5; whether Mozc takes `@`+Ctrl+Alt as text is also step 2's.
  - `KeyboardGate.IsTextKey` now delegates to `KeyTranslation.IsTextKey`
    (same set); ticket 07 removes the gate's rule.
- 2026-09-18 (ticket 07's run): what Wine posts for AltGr+2 on a Nordic layout —
  ```
  Pass    Fixed     Answered      asked   0.3ms KEYDOWN vk=0xE4 sc=0x38 ext  [ChatLog]
  Swallow Printing  Provisional   unasked       KEYDOWN vk=0x32 sc=0x03  [ChatLog]
  Pass    Printing  Answered      asked   0.4ms CHAR U+0040 '@' sc=0x03  [ChatLog]
  Pass    Printing  FollowsPress  unasked       KEYUP vk=0x32 sc=0x03  [ChatLog]
  Pass    Fixed     FollowsPress  unasked       KEYUP vk=0xE4 sc=0x38 ext  [ChatLog]
  ```
  AltGr itself is `vk=0xE4` (X11 `ISO_Level3_Shift`), not `VK_RMENU`, with
  the right-Alt scancode extended; the `2` is a plain `KEYDOWN`/`CHAR` (no
  `SYS`, no Alt context bit). `0xE4` is now `VirtualKey.WineAltGr`, a
  `Modifier` → `AltR`. The `2` classified `Printing` on the state as read,
  so `IsAltGr` held (Ctrl+Alt or right Alt down per `GetKeyState`). `@`
  reached the box on both `A` and `あ`; Mozc declined it with the Ctrl+Alt
  state, so `Modifiers.ToKeyState` needs no Mod5 amendment for now.
