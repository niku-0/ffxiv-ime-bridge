# FfxivImeBridge.Fcitx

A Dalamud-free .NET client for fcitx5's D-Bus frontend. The plugin uses it to
run a Japanese composition without going through Wine's IME path; the tests in
`tests/FfxivImeBridge.Fcitx.Tests` run it against the real fcitx5 on the
developer's session bus.

Everything below was verified live against **fcitx5 5.1.22 + fcitx5-mozc
3.34** on 2026-09-17, not taken from documentation.

## Protocol

| What | Value |
| --- | --- |
| Bus name | `org.fcitx.Fcitx5` |
| Factory | `/org/freedesktop/portal/inputmethod`, `org.fcitx.Fcitx.InputMethod1` |
| `CreateInputContext(a(ss)) → (o ay)` | args are `(key, value)` pairs, at least `("program", name)`; returns the context path and a 16-byte uuid |
| Context interface | `org.fcitx.Fcitx.InputContext1` at `/org/freedesktop/portal/inputcontext/N` |
| Controller | `/controller`, `org.fcitx.Fcitx.Controller1`: `CurrentInputMethod() → s`, `SetCurrentIM(s)` |
| Presence | the bus's `NameOwnerChanged(sss)` with `arg0=org.fcitx.Fcitx5`, surfaced as `FcitxConnection.FcitxAvailabilityChanged` (`WatchAvailabilityAsync`) |

Context methods used: `SetCapability(t)`, `FocusIn()`, `FocusOut()`,
`ProcessKeyEvent(uuubu) → b` (keysym, keycode, state, isRelease, time),
`Reset()`, `SelectCandidate(i)`, `NextPage()`, `PrevPage()`,
`SetCursorRect(iiii)`, `DestroyIC()`.

Signals consumed (signatures asserted by
`Creates_a_context_whose_interface_matches_what_the_library_assumes`):

| Signal | Signature | Meaning |
| --- | --- | --- |
| `UpdateFormattedPreedit` | `a(si) i` | preedit segments `(text, TextFormat)` and cursor as a **UTF-8 byte offset** |
| `UpdateClientSideUI` | `a(si) i a(si) a(si) a(ss) i i b b` | panel preedit + cursor (empty for us, see below), auxUp, auxDown, candidates `(label, text)`, selected index, layout hint, hasPrev, hasNext |
| `CommitString` | `s` | text to insert |
| `CurrentIM` | `s s s` | name, unique name, language code |
| `ForwardKey` | `u u b` | keysym, state, isRelease: fcitx5 wants the key delivered to the app |
| `DeleteSurroundingText` | `i u` | offset, size |

Signals are emitted *before* the method reply that caused them, on the same
connection, so `State` is already updated when `await ProcessKeyEventAsync`
returns.

## Things that are easy to get wrong

- **Every method call needs `MessageFlags.NoAutoStart`** (`MethodCall.Flags`,
  passed at every `WriteMethodCallHeader`). fcitx5 ships a D-Bus activation
  file, so a call addressed to `org.fcitx.Fcitx5` while nobody owns the name
  has `dbus-daemon` exec `fcitx5` — a client that keeps talking after fcitx5
  has left the bus (a `DestroyIC` on unload, the release of a forwarded key)
  starts it again on the user's desktop. With the flag the bus answers
  `org.freedesktop.DBus.Error.NameHasNoOwner` instead.
- **Keycodes are required.** With `keycode = 0` Mozc reports the key as
  handled but composes nothing. X keycodes are evdev codes + 8; for the main
  block evdev codes equal PC set-1 scancodes (`KeyCode.FromWindowsScanCode`).
- **Preedit comes from `UpdateFormattedPreedit`**, not `UpdateClientSideUI`,
  once the `Preedit`/`FormattedPreedit` capabilities are set. Without them the
  panel preedit in `UpdateClientSideUI` is used instead, but then Mozc mixes
  its hint text (`[Tabキーで選択]`) into it. `CapabilityFlags.ClientDrawsComposition`
  is the right set.
- **Input-method state is per context** (fcitx5 default `ShareInputState=No`).
  `Controller1.CurrentInputMethod` describes the *focused* context and is empty
  when none is; `SetCurrentIM` only takes effect on a focused context. A new
  context starts on the group's default (the keyboard layout), so a client
  that wants Mozc has to switch after `FocusIn` or let the user do it.
- **`CurrentIM` follows `FocusIn`.** With `GetIMInfoOnFocus` (in
  `ClientDrawsComposition`) fcitx5 emits `CurrentIM` after every `FocusIn`, so
  a client learns the context's input method without calling the controller.
- **`FocusOut` commits.** Mozc commits the pending composition on focus loss
  whether or not `ClientUnfocusCommit` is set. Call `Reset()` first to discard
  it instead.
- **Key releases are never "handled"** (`ProcessKeyEvent` returns false), so a
  client that swallowed a press must decide about its release itself.
- **Candidate text carries Mozc's annotation** (`か [ひらがな]`,
  `（火） [[全] 火曜日]`); only the word is committed. Labels are `1. `, `2. `
  … for the conversion list and empty for the prediction list, where
  `SelectedCandidate` is -1 until the user starts cycling.
- Mozc's first Space converts (highlighted segment, no list); the second opens
  the candidate list. `HasNextPage` reports paging; `NextPage()` also works.
- Pending romaji shows full-width (`ｋ`, underlined) and the cursor byte offset
  counts UTF-8 bytes (`か` → 3).

## Reaching the bus from inside Wine

Verified 2026-09-17 with the transport probe (`tools/FfxivImeBridge.TransportProbe`)
under wine-xiv-staging 10.8 and the Windows .NET 10 runtime Dalamud ships
(`~/.xlcore/runtime`), in a scratch prefix:

- `DBUS_SESSION_BUS_ADDRESS` and `XDG_RUNTIME_DIR` are visible to the Windows
  process; Wine imports the Unix environment and XIVLauncher.Core passes it
  through (checked in `/proc/<ffxiv_dx11 pid>/environ`).
- `new Socket(AddressFamily.Unix, Stream, Unspecified)` and `connect` work
  through Wine's `ws2_32`, with **either** spelling of the path:
  `/run/user/1000/bus` or `Z:\run\user\1000\bus`. No translation needed;
  the address can be handed to Tmds as-is.
- `AUTH EXTERNAL` must carry the **Linux uid** (`1000`). Tmds.DBus.Protocol's
  Windows default is the SID (`S-1-5-21-…`), which dbus-broker rejects
  (checked on the host: a mismatched identity is `REJECTED`, and
  `ANONYMOUS` is not accepted either). `FcitxConnectionOptions.ExternalUserId`
  is that uid; `SessionBusAddress.Parse` derives it from `/run/user/<uid>/`.
- Turn fd passing off (`SupportsFdPassing = false`); nothing here needs it.
- `ntdll!wine_get_version` is hidden by the xiv build (wine-staging's export
  hiding), so Wine is detected from `WINEPREFIX`/`WINELOADERNOEXEC` instead.
- With that, `CreateInputContext` → `FocusIn` → `SetCurrentIM(mozc)` →
  `ProcessKeyEvent('k')` yields the preedit `ｋ` and two `UpdateClientSideUI`
  signals inside Wine exactly as on the host. Rung 1 of the spec's ladder
  holds; the helper process is not needed.

`Diagnostics.TransportProbe` runs this ladder step by step and names the
first failing rung; the dev plugin runs it on load.

## Running the tests

They need a session bus with fcitx5 and Mozc, and they focus a context of
their own for ~200 ms per test (your terminal's IME focus comes back when you
click it). Skipped automatically when the bus or fcitx5 is missing.

```
dotnet test
```
