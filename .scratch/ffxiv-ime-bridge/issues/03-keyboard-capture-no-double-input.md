# M0.3 — Keyboard capture with the native Chat Box focused

Status: resolved
Type: prototype
Blocked by: 02

Prove that while the native chat input has focus, the plugin can swallow
keystrokes so the game does not also insert them.

## Done when

- Detection: plugin knows when the ChatLog text input is focused
  (`AtkStage`/`RaptureAtkModule` focused-node, or the addon's input's
  `IsFocused`), and when the game UI is hidden.
- With a debug "swallow" flag on, typing letters into the focused Chat Box
  produces nothing in the native buffer, and `IKeyState`/ImGui sees them.
  With the flag off, behaviour is vanilla.
- Confirmed for `WM_KEYDOWN`, `WM_CHAR`, key repeat, and modifier combos;
  also that Enter/Escape pass through cleanly when *not* swallowed.
- Toggle key `Alt+§` (VK for `§` on a Nordic layout — check the scan code Wine
  reports) can be captured and is itself swallowed.
- Record which mechanism worked (ImGui `WantCaptureKeyboard`/
  `WantTextInput`, `IKeyState` clearing, or a WndProc-level hook) and
  whether the fallback (blur native, invisible ImGui widget) was needed.

## Answer

**A WndProc-level hook, one step upstream of Dalamud's, and it works
in-game** (confirmed 2026-09-18, trace in Comments: every step of the human
run behaved; no double input; the fallback was not needed).
Dalamud's own keyboard swallowing (`Win32InputHandler.ProcessWndProcW`) drops
`WM_KEYDOWN/UP`, `WM_SYSKEY*` and `WM_CHAR` only while ImGui's
`io.WantTextInput` is set — a per-frame, all-or-nothing flag that would also
eat Enter/Escape and cannot make the spec's synchronous per-key decision.
`IKeyState` is a view of the game's key array, downstream of the messages;
clearing it stops keybinds, not text insertion. `WndProcHookManager` is
internal to Dalamud. So the plugin hooks the game's `user32!DispatchMessageW`
import via `IGameInteropProvider.HookFromImport` — the same import Dalamud
hooks to find the game window, so Dalamud manages the hook's lifetime and
unload restores it. Chain: game → plugin → Dalamud → `DispatchMessageW` →
Dalamud WndProc (ImGui) → game WndProc. Keyboard input is posted, so
`WM_KEYDOWN`, the `WM_CHAR` `TranslateMessage` queued behind it, and
`WM_KEYUP` each arrive as one call on the main thread; returning 0 drops one.

Per done-when item:

- Detection: `RaptureAtkModule.Instance()->AtkModule.IsTextInputActive()`,
  then `TextInput.TargetTextInputEventInterface->GetOwnerNode()` →
  component node → `AtkComponentInputBase.OwnerAddon->NameString` (the
  component's type is checked before the cast). **Resolves in-game**:
  `ChatLog` for the Chat Box, `PcSearchDetail` and `InputString` for the
  other fields tried, `-` once focus is gone — so `ChatBoxFocused` now
  requires `ChatLog` (the "unresolved = Chat Box" prototype fallback is
  removed). Note Enter's `KEYUP` already arrives with focus gone (`[-]`);
  the press/release pairing covers it. UI hidden: `IGameGui.GameUiHidden`,
  but the game refuses to hide the UI while a text field is focused, so in
  practice it is never true while this matters; kept as a cheap guard.
- Swallow flag: `KeyboardGate` (pure, 25 host tests in
  `tests/FfxivImeBridge.Tests`). On + Chat Box focused: text keys (letters
  and, beyond the ticket's wording, digits, space, OEM punctuation, numpad —
  everything fcitx5 will want) are swallowed as press + chars + release;
  Enter, Escape, Tab, Backspace, arrows, F-keys and modifier keys pass. Off:
  nothing is touched. Ctrl/Shift+letter: the letter is swallowed, the
  modifier passes.
- "`IKeyState`/ImGui sees them": **deliberately not.** The hook sits
  upstream of both, so a swallowed key reaches neither the game's key array
  nor ImGui; the plugin itself sees every key in its own hook, which is what
  M1 needs. If a downstream consumer ever needs them, the hook can forward
  to `io.AddKeyEvent` — not built.
- Repeat: `WM_KEYDOWN` with bit 30 keeps the verdict of the first press
  (whatever the flag or focus does meanwhile); its `WM_CHAR` follows it. A
  release is swallowed iff its press was, on the same rule.
- Toggle: `Alt` + scancode `0x29` (the key left of `1`; `§` on a Nordic layout,
  `` ` `` on US). **Wine reports § as `vk=0xDE` (VK_OEM_7), `sc=0x29`**,
  not Windows' VK_OEM_5 — matching by scancode was the right call. The
  `WM_SYSKEYDOWN` toggles, its `WM_SYSCHAR` and the key-up are swallowed
  (also when Alt is released first and the key-up is a plain `WM_KEYUP`);
  holding it does not re-toggle; outside the Chat Box the chord passes
  untouched. Alt itself (`vk=0x12 sc=0x38`) reaches the game as with any
  chord and did nothing visible.
- Confirmed in the trace: press/`WM_CHAR`/release all swallowed; auto-repeat
  (bit 30) swallowed per repeat with its char; `Shift+A` and `Ctrl+V`
  swallow the letter and pass the modifier; Enter and Escape (and their
  chars) pass and behave; with the flag off everything is vanilla.
- Fallback (blur native, invisible ImGui widget): **not needed.** The Chat
  Box is fed by the posted messages alone.

Code: `src/FfxivImeBridge/Capture/` (`KeyMessage`, `KeyboardGate`,
`MessagePumpHook`, `ChatBoxFocus`, `KeyboardCapture`), Keyboard tab in
`DebugWindow`, `/imebridge swallow [on|off]`. How to test:
`docs/dev-plugin.md`, "Keyboard capture (M0.3)".

### In-game confirmation (done 2026-09-18)

1. Rebuild (`dotnet build src/FfxivImeBridge`), reload the dev plugin.
2. `/imebridge debug` → **Keyboard** tab. Click into the chat box. Note the
   `Focus:` line — does `owner=` say `ChatLog`, or `?` (`?` = the owner
   walk failed; the prototype then treats *any* native text field as the
   Chat Box, and detection is not done). Also check `dalamud.log` has no
   `called off the framework thread` warning.
3. With swallow **off**: type `abc`, Enter (sends `abc` — pick a channel
   where that is fine, e.g. `/echo`), then `abc` again and Escape. Vanilla
   behaviour expected; every trace line `Pass`.
4. Swallow **on** (`/imebridge swallow`, then also try `Alt+§`): type
   `abc`, hold `a` for repeat, `Shift+A`, `Ctrl+V`. Expected: nothing
   appears in the chat box; the trace shows `Swallow` for each `KEYDOWN`,
   `CHAR` and `KEYUP`. Then Enter with an empty box (should not send
   anything odd) and Escape (should unfocus). Type text with swallow off,
   turn swallow on, press Enter: the text should send.
5. Alt+§: does the trace show `SYSKEYDOWN vk=0x?? sc=0x29 alt` and a
   `Toggle`, and does chat print `IME Bridge: swallow on`? If `sc` is not
   `0x29`, note what it is. Does the bare Alt that still reaches the game
   do anything visible (menu focus, keybind)?
6. Type into another native text field (e.g. the search box in the
   inventory, or a `/tell` name prompt) with swallow on: are letters
   swallowed there? (They should not be if `owner=` resolves.)
7. Hide the UI (Scroll Lock) with swallow on and the chat box focused, type
   a letter: `uiHidden=True` in the readout and nothing swallowed.
8. **Copy trace** on the Keyboard tab puts the readout and trace on the
   clipboard; paste it into the Comments below; if all of 3–7 behave, set
   `Status: resolved`. (All did; step 7 is not testable because the game
   will not hide the UI while a text field is focused.)

Safety notes for that run: the plugin only reads game memory that is
Dalamud's public ClientStructs surface (focus state) and drops posted
keyboard messages while the flag is on; it does not send chat (Enter is
never synthesised; ADR-0001 stands). Unloading the plugin removes the hook.

## Comments

- 2026-09-18: `KeyboardGate`'s text-key rule is an M0.3 stand-in for fcitx5's
  per-key answer; in M1 `ProcessKeyEvent` replaces `IsTextKey` and the
  pairing rules (chars follow their press, release follows its press) stay.
- 2026-09-18: "swallow"/"gate"/"pass" join "transport ladder"/"rung" as code
  terms not in `CONTEXT.md`; candidate for `/domain-modeling` with them.
  → Done 2026-09-18: **Gate**, **Swallow** and **Transport Ladder** (with
  Rung) are in `CONTEXT.md`. "Pass" is defined inside Gate rather than as
  its own term. Drift to fix when touched: `TransportProbe` calls its
  diagnostic steps "rungs" too; the glossary reserves Rung for a transport.
- 2026-09-18 human notes: All behavior worked as expectd, but note that: 
  1: ALT key is sc=0x38 alt when chat not focused, only reads as 0x29 alt and toggles swallow when chat log focused.
  2: can't hide ui while chat focused, tried ' key, home key, mouse 5 keybinds.
- 2026-09-18 debug key trace:
  swallow=on seen=169 swallowed=60
  focus: textInputActive=True owner=ChatLog uiHidden=False → chatBoxFocused=True
  Pass    KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Pass    CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Pass    KEYDOWN vk=0x42 sc=0x30  [ChatLog]
  Pass    CHAR U+0062 'b' sc=0x30  [ChatLog]
  Pass    KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYUP vk=0x42 sc=0x30  [ChatLog]
  Pass    KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Pass    CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Pass    KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Pass    KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
  Pass    CHAR U+000D sc=0x1C  [ChatLog]
  Pass    KEYUP vk=0x0D sc=0x1C  [-]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Pass    CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Pass    KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYDOWN vk=0x42 sc=0x30  [ChatLog]
  Pass    CHAR U+0062 'b' sc=0x30  [ChatLog]
  Pass    KEYUP vk=0x42 sc=0x30  [ChatLog]
  Pass    KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Pass    CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Pass    KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Pass    KEYDOWN vk=0x1B sc=0x01  [ChatLog]
  Pass    CHAR U+001B sc=0x01  [ChatLog]
  Pass    KEYUP vk=0x1B sc=0x01  [-]
  Pass    KEYDOWN vk=0x08 sc=0x0E  [ChatLog]
  Pass    CHAR U+0008 sc=0x0E  [ChatLog]
  Pass    KEYUP vk=0x08 sc=0x0E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x42 sc=0x30  [ChatLog]
  Swallow CHAR U+0062 'b' sc=0x30  [ChatLog]
  Swallow KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Swallow CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Swallow KEYUP vk=0x42 sc=0x30  [ChatLog]
  Swallow KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E repeat  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYDOWN vk=0x10 sc=0x2A  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow CHAR U+0041 'A' sc=0x1E  [ChatLog]
  Swallow KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYUP vk=0x10 sc=0x2A  [ChatLog]
  Pass    KEYDOWN vk=0x11 sc=0x1D  [ChatLog]
  Swallow KEYDOWN vk=0x56 sc=0x2F  [ChatLog]
  Swallow CHAR U+0016 sc=0x2F  [ChatLog]
  Swallow KEYUP vk=0x56 sc=0x2F  [ChatLog]
  Pass    KEYUP vk=0x11 sc=0x1D  [ChatLog]
  Pass    KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
  Pass    CHAR U+000D sc=0x1C  [ChatLog]
  Pass    KEYUP vk=0x0D sc=0x1C  [-]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Pass    CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Pass    KEYDOWN vk=0x44 sc=0x20  [ChatLog]
  Pass    CHAR U+0064 'd' sc=0x20  [ChatLog]
  Pass    KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYUP vk=0x44 sc=0x20  [ChatLog]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Pass    CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Pass    KEYDOWN vk=0x44 sc=0x20  [ChatLog]
  Pass    CHAR U+0064 'd' sc=0x20  [ChatLog]
  Pass    KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYUP vk=0x44 sc=0x20  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x44 sc=0x20  [ChatLog]
  Swallow CHAR U+0064 'd' sc=0x20  [ChatLog]
  Swallow KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow KEYUP vk=0x44 sc=0x20  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYDOWN vk=0x44 sc=0x20  [ChatLog]
  Swallow CHAR U+0064 'd' sc=0x20  [ChatLog]
  Swallow KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Swallow KEYUP vk=0x44 sc=0x20  [ChatLog]
  Swallow KEYDOWN vk=0x41 sc=0x1E  [ChatLog]
  Swallow CHAR U+0061 'a' sc=0x1E  [ChatLog]
  Swallow KEYUP vk=0x41 sc=0x1E  [ChatLog]
  Pass    KEYDOWN vk=0x10 sc=0x2A  [ChatLog]
  Pass    KEYDOWN vk=0x37 sc=0x08  [ChatLog]
  Pass    CHAR U+002F '/' sc=0x08  [ChatLog]
  Pass    KEYUP vk=0x37 sc=0x08  [ChatLog]
  Pass    KEYUP vk=0x10 sc=0x2A  [ChatLog]
  Pass    KEYDOWN vk=0x45 sc=0x12  [ChatLog]
  Pass    CHAR U+0065 'e' sc=0x12  [ChatLog]
  Pass    KEYUP vk=0x45 sc=0x12  [ChatLog]
  Pass    KEYDOWN vk=0x43 sc=0x2E  [ChatLog]
  Pass    CHAR U+0063 'c' sc=0x2E  [ChatLog]
  Pass    KEYDOWN vk=0x48 sc=0x23  [ChatLog]
  Pass    CHAR U+0068 'h' sc=0x23  [ChatLog]
  Pass    KEYUP vk=0x43 sc=0x2E  [ChatLog]
  Pass    KEYDOWN vk=0x4F sc=0x18  [ChatLog]
  Pass    CHAR U+006F 'o' sc=0x18  [ChatLog]
  Pass    KEYUP vk=0x48 sc=0x23  [ChatLog]
  Pass    KEYDOWN vk=0x20 sc=0x39  [ChatLog]
  Pass    CHAR U+0020 ' ' sc=0x39  [ChatLog]
  Pass    KEYUP vk=0x4F sc=0x18  [ChatLog]
  Pass    KEYUP vk=0x20 sc=0x39  [ChatLog]
  Pass    KEYDOWN vk=0x0D sc=0x1C  [ChatLog]
  Pass    CHAR U+000D sc=0x1C  [ChatLog]
  Pass    KEYUP vk=0x0D sc=0x1C  [-]
  Pass    KEYDOWN vk=0x4F sc=0x18  [-]
  Pass    CHAR U+006F 'o' sc=0x18  [-]
  Pass    KEYUP vk=0x4F sc=0x18  [-]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [PcSearchDetail]
  Pass    CHAR U+0061 'a' sc=0x1E  [PcSearchDetail]
  Pass    KEYUP vk=0x41 sc=0x1E  [PcSearchDetail]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [PcSearchDetail]
  Pass    CHAR U+0061 'a' sc=0x1E  [PcSearchDetail]
  Pass    KEYUP vk=0x41 sc=0x1E  [PcSearchDetail]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [PcSearchDetail]
  Pass    CHAR U+0061 'a' sc=0x1E  [PcSearchDetail]
  Pass    KEYUP vk=0x41 sc=0x1E  [PcSearchDetail]
  Pass    KEYDOWN vk=0x43 sc=0x2E  [-]
  Pass    CHAR U+0063 'c' sc=0x2E  [-]
  Pass    KEYUP vk=0x43 sc=0x2E  [-]
  Pass    KEYDOWN vk=0x20 sc=0x39  [InputString]
  Pass    CHAR U+0020 ' ' sc=0x39  [InputString]
  Pass    KEYUP vk=0x20 sc=0x39  [InputString]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [InputString]
  Pass    CHAR U+0061 'a' sc=0x1E  [InputString]
  Pass    KEYUP vk=0x41 sc=0x1E  [InputString]
  Pass    KEYDOWN vk=0x41 sc=0x1E  [InputString]
  Pass    CHAR U+0061 'a' sc=0x1E  [InputString]
  Pass    KEYUP vk=0x41 sc=0x1E  [InputString]
  Pass    SYSKEYDOWN vk=0x12 sc=0x38 alt  [-]
  Pass    SYSKEYDOWN vk=0xDE sc=0x29 alt  [-]
  Pass    SYSCHAR U+00A7 '§' sc=0x29 alt  [-]
  Pass    SYSKEYUP vk=0xDE sc=0x29 alt  [-]
  Pass    KEYUP vk=0x12 sc=0x38  [-]
  Pass    SYSKEYDOWN vk=0x12 sc=0x38 alt  [-]
  Pass    SYSKEYDOWN vk=0xDE sc=0x29 alt  [-]
  Pass    SYSCHAR U+00A7 '§' sc=0x29 alt  [-]
  Pass    SYSKEYUP vk=0xDE sc=0x29 alt  [-]
  Pass    KEYUP vk=0x12 sc=0x38  [-]
  Pass    SYSKEYDOWN vk=0x12 sc=0x38 alt  [ChatLog]
  Toggle  SYSKEYDOWN vk=0xDE sc=0x29 alt  [ChatLog]
  Swallow SYSCHAR U+00A7 '§' sc=0x29 alt  [ChatLog]
  Swallow SYSKEYUP vk=0xDE sc=0x29 alt  [ChatLog]
  Pass    KEYUP vk=0x12 sc=0x38  [ChatLog]
  Pass    SYSKEYDOWN vk=0x12 sc=0x38 alt  [ChatLog]
  Toggle  SYSKEYDOWN vk=0xDE sc=0x29 alt  [ChatLog]
  Swallow SYSCHAR U+00A7 '§' sc=0x29 alt  [ChatLog]
  Swallow SYSKEYUP vk=0xDE sc=0x29 alt  [ChatLog]
  Pass    KEYUP vk=0x12 sc=0x38  [ChatLog]
  Pass    SYSKEYDOWN vk=0x12 sc=0x38 alt  [ChatLog]
  Toggle  SYSKEYDOWN vk=0xDE sc=0x29 alt  [ChatLog]
  Swallow SYSCHAR U+00A7 '§' sc=0x29 alt  [ChatLog]
  Pass    KEYUP vk=0x12 sc=0x38  [ChatLog]
  Swallow KEYUP vk=0xDE sc=0x29  [ChatLog]
- 2026-09-18: closed out. On the human notes: `sc=0x38` is the Alt key itself
  (`vk=0x12`); § is `sc=0x29` in every line and the toggle only acts while
  the Chat Box is focused, by design. UI-hidden is untestable while typing,
  as noted. `ChatBoxFocused` tightened to require `owner=ChatLog`. Unblocks
  nothing new (04 was already unblocked by 02); M1 can build on this hook.
