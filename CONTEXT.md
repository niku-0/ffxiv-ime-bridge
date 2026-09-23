# FFXIV IME Bridge

Live Japanese IME composition inside FFXIV's chat box on Linux, by talking to
fcitx5 directly instead of through Wine's broken IME bridge.

## Language

**Chat Box**:
The game's own native chat text input (the ChatLog addon's text field). The
only text field in scope for v1.
_Avoid_: chat window, chat log (that's the message history, not the input)

**Input Context**:
A per-client session with fcitx5, created over D-Bus, that owns focus, key
processing and composition state for this plugin.
_Avoid_: IC, IME session, connection

**Composition**:
The in-progress state between the first forwarded keystroke and a commit or
cancel, during which fcitx5 owns the typed keys.
_Avoid_: conversion, typing session

**Preedit**:
The not-yet-committed text fcitx5 asks us to display while a composition is
active, with per-segment formatting.
_Avoid_: composition string, pre-edit, buffer

**Candidate**:
One of the alternative conversions fcitx5 offers for the current preedit
segment.
_Avoid_: suggestion, option, completion

**Commit**:
The moment fcitx5 hands over final text; the preedit is emptied and the
committed text becomes ordinary chat-box content.
_Avoid_: confirm, accept, insert

**Native Write**:
The plugin placing committed text into the Chat Box at the Cursor, leaving
the Cursor after it. The only thing the plugin ever does with a Commit;
sending stays the user's Enter.
_Avoid_: insert, paste, send, SetText

**Cursor**:
The Chat Box's text insertion point: a character index into its text and a
point on screen. A Native Write lands at it and the Preedit anchors to it.
Not the mouse pointer.
_Avoid_: caret, insertion point, text position

**Forwarding**:
The plugin-level state in which keystrokes typed into the focused Chat Box are
routed to the Input Context instead of the game. Independent of fcitx5's own
active/inactive state, which keeps working inside Forwarding.
_Avoid_: IME mode, Japanese mode, enabled

**Degraded**:
Forwarding with nothing behind it: fcitx5 is gone or not answering, so keys
pass to the game untouched while the user's Forwarding choice is kept for
when it returns. Shown as `!` by the Indicator.
_Avoid_: disconnected, offline, paused, fallback mode

**Inert**:
The plugin with no session at all: the Transport Ladder failed, so nothing is
hooked into and the Chat Box is vanilla. Unlike Degraded there is no
Forwarding choice underneath; the toggle only shows a toast. Left by a
Reconnect.
_Avoid_: disabled, dead, unavailable

**Reconnect**:
Tearing down whatever is left of the session and climbing the Transport
Ladder again, as on load — the way out of Inert and out of a dead bus
connection. Not the recreation of an Input Context after fcitx5 returns to
a live bus, which Degraded handles by itself.
_Avoid_: retry, reload, restart

**Toggle Key**:
The modifier-plus-key chord that flips Forwarding while the Chat Box has
focus. Matched by scancode, so it names a physical key, not a character;
Swallowed from the game.
_Avoid_: hotkey, keybind, shortcut

**Indicator**:
The single-glyph badge drawn where the game's own input-mode badge sits —
left of the channel name at the Chat Box's edge — while Forwarding is on,
showing fcitx5's current input method (`あ` for Mozc, `A` for direct). Can be
hidden for a vanilla look.
_Avoid_: status icon, mode label, overlay

**Slash Bypass**:
The rule that a leading `/` and the command word after it are never Forwarded
when the Chat Box is empty, so game commands stay typeable without leaving
Forwarding.
_Avoid_: command mode, literal mode

**Gate**:
The per-keystroke, synchronous decision, made before the game sees the key,
between Swallowing it and passing it through. Forwarding is the state; the
Gate is the verdict on each key inside it (Enter with no Composition passes).
_Avoid_: filter, interceptor, key handler

**Swallow**:
Dropping an input event so the game never receives it. For a keystroke that
is the press, the character it produced, and the release: a key's release
goes the way its press finally went; every auto-repeat is decided afresh.
For a mouse click on the candidate list it is the press and its release.
_Avoid_: block, eat, drop, consume, intercept

**Overflow**:
Committed text that would push the Chat Box past the game's length limit. It
is refused, never truncated, and the user is told by how much.
_Avoid_: truncation, excess

**Transport Ladder**:
The ordered ways of reaching the session bus from inside Wine, tried top to
bottom on load until one works; each way is a Rung. Not the diagnostic steps
that follow once a Rung holds.
_Avoid_: fallback chain, connection strategy, probe

**Native Path**:
Wine's XIM → `WM_IME_*` route that the game already (unsuccessfully) uses.
Never used by this plugin, only kept out of the way.
_Avoid_: legacy path, Wine IME
