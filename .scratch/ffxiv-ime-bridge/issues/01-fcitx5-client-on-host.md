# M0.1 — fcitx5 D-Bus client library, proven on the host

Status: resolved
Type: task
Blocked by: 00

Build `src/FfxivImeBridge.Fcitx/` (plain `net10.0`, no Dalamud reference)
on top of `Tmds.DBus.Protocol`, and a host-runnable test in `tests/` that
talks to the real running fcitx5.

## Done when

- Connects to `DBUS_SESSION_BUS_ADDRESS`, calls
  `org.fcitx.Fcitx.InputMethod1.CreateInputContext` on
  `/org/freedesktop/portal/inputmethod` with `a(ss)` args (at least
  `("program","ffxiv-ime-bridge")`), gets an object path back.
- `SetCapability` includes `ClientSideInputPanel`; `FocusIn`; sends
  `ProcessKeyEvent` for keysym `k` (0x6b) with keycode 0, state 0; observes
  `UpdateClientSideUI` carrying a non-empty preedit when Mozc is the active
  IM (test switches to Mozc via `SetCurrentIM`/`org.fcitx.Fcitx.Controller1`
  or asserts and skips if not active).
- Sends Enter keysym (0xff0d) and observes `CommitString`.
- `FocusOut` and `DestroyIC` on dispose; no leaked contexts
  (`busctl --user` introspection shows the path gone).
- Exposes a typed model: `Preedit` (segments + format flags + cursor),
  `Candidate` (label, text), selection index, layout hint, paging flags,
  `CurrentIM` name; `KeyEvent` in/out; `Handled` result from
  `ProcessKeyEvent`.
- Signal name/signature for `UpdateClientSideUI` and `CommitString` are
  taken from live introspection of the created context, not from memory,
  and recorded in the library's README.

## Out of scope

Anything Wine/Dalamud related. This ticket proves the protocol only.

## Comments

## Answer

Done in `src/FfxivImeBridge.Fcitx/` with 7 live tests in
`tests/FfxivImeBridge.Fcitx.Tests/` (all green against fcitx5 5.1.22 +
fcitx5-mozc 3.34). Protocol facts are recorded in the library README.
Findings that change the plugin design:

1. **Keycodes are mandatory.** keysym-only key events (`keycode = 0`) are
   swallowed by Mozc but compose nothing. The plugin must derive X keycodes
   from the Windows scancode in `WM_KEYDOWN` lParam (`KeyCode.FromWindowsScanCode`),
   not from the character alone.
2. **Preedit arrives via `UpdateFormattedPreedit`**, candidates/aux via
   `UpdateClientSideUI`; both merged into one `CompositionState`.
3. **`FocusOut` commits (Mozc behaviour).** "Focus loss cancels" needs
   `Reset()` before `FocusOut()`. Spec updated.
4. **Input-method state is per context** and a new context starts on the
   keyboard layout, not Mozc. The Indicator (`あ`/`A`) is therefore about
   *our* context's IM, and Ctrl+Space forwarded through the context switches
   only our context — which is exactly what Forwarding wanted.
5. Candidate texts include Mozc annotations; committed text is the bare
   word. Renderer shows them as-is.

## Comments

- 2026-09-17: `Tmds.DBus.Protocol` 0.21.x has a known vulnerability advisory
  (NU1903); pinned to 0.95.1. Its API is the `DBusConnection` /
  `Notification<T>` generation, not the older `Connection` one.
