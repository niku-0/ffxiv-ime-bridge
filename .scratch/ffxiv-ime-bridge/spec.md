# FFXIV IME Bridge — Spec (settled 2026-09-17, M2 amendments 2026-09-18,
M3 amendments 2026-09-22)

Dalamud plugin. Purpose: live Japanese IME composition directly in FFXIV's
Chat Box on Linux, bypassing Wine/Proton's broken IME-message bridge.
Vocabulary: see `CONTEXT.md`. Architectural decisions: `docs/adr/`.

## Problem

Wine's translation of X11 input-method composition into Windows `WM_IME_*`
messages (the Native Path) is broken for apps like FFXIV that draw their own
chat box and candidate UI. Japanese input into the Chat Box produces
`???`/mojibake even though the same IME works in ordinary Linux apps.
Long-standing upstream issue; not something this project tries to fix.

## Target environment (verified)

- KDE on Wayland, fcitx5 with Mozc (Mozc configured with `jp` layout),
  a Nordic host keyboard layout.
- Session bus at `unix:path=/run/user/1000/bus`, fcitx5 owns
  `org.fcitx.Fcitx5`, `org.fcitx.Fcitx.InputMethod1` at
  `/org/freedesktop/portal/inputmethod` (introspected, matches the spec).
- Game via native XIVLauncher.Core, managed wine-xiv-staging 10.8,
  Dalamud 15.0.3.4 on .NET 10, client language English.
- Dev-plugin reference DLLs: `~/.xlcore/dalamud/Hooks/dev/`.
- .NET 10 SDK on the host.

## Decisions

### Scope and posture (from the original spec, unchanged)

- Chat Box only in v1. Other native text fields are out of scope.
- No copy-paste relay, no separate floating window. Composition renders in
  place, live, over the game's own UI surface.
- fcitx5 first. ibus is a possible later addition.
- Visual fidelity: same screen location, functionally clear. Pixel-perfect
  match to the native chat font/styling is not required.
- Public release is the goal; don't let it block a working version.
- Fail closed: if D-Bus/fcitx5 is unreachable the Chat Box behaves exactly
  as it does today.

### Committed text lives in the native Chat Box (ADR-0001)

The plugin **never sends chat**. On Commit it writes the committed text into
the game's own chat text input (`AtkComponentTextInput` on the ChatLog
addon) at the native cursor position; the user presses Enter and the game
sends. Channel selection, `/tell` targeting, input history and the length
limit are the game's. "Full channel support" is therefore not a milestone.

- Native Write at the native cursor index (read text + cursor, plan the
  splice, cursor index written after the text, the game's own `InsertText`,
  `SetText` of the unchanged text so the game redraws the cursor).
  Verified in-game (M0.4): the cursor index is in code points, live on both
  the component and the input module, and writable; `InsertText` splices at
  the cursor but does not advance it, hence the cursor write. `SetText`
  was the M0.4 primitive, but it leaves the input module's copy of the text
  stale and the game hands that copy back on focus loss, erasing the write
  (ticket 09) — so the game's splice is used. It splices at the module's
  before/after split — where the game's own keystrokes go — and rebuilds
  that split from the cursor index, so the index goes in first: written
  after, the split, the next keystroke and the drawn cursor all stayed at
  the start of the written text (ticket 20). The drawn cursor follows
  only the module's own `UpdateTextSelection` notice to the component, so
  that is called with the written index and the module's raw string for
  both of its strings (`SetText` would draw it at the end of the text; the
  module's evaluated string is old + new after the splice and the game
  rebuilds the box from it on focus-in; ticket 20). Append-at-end via `SetText`
  remains the fallback for an unusable cursor (and for a buffer holding
  auto-translate payloads until M1 decides how to count them).
- Overflow: if the committed text doesn't fit the game's limit, refuse the
  insert and warn by how much. Never truncate. The Chat Box's limit is
  **500 bytes** (`MaxByte`; `MaxChar` is 0), so the count is in bytes —
  ~166 Japanese characters. The refusal line states bytes over and the
  approximate character count (3 bytes per kana/kanji); ticket 09.

### Forwarding (the plugin's only mode)

- **Forwarding** = keystrokes typed into the focused Chat Box go to the
  fcitx5 Input Context instead of the game. It is independent of fcitx5's
  own active/direct state; Ctrl+Space etc. are forwarded like any other key
  and fcitx5's state machine keeps working inside Forwarding.
- Toggle Key: configurable modifiers + scancode (a physical key, so one
  default serves every layout): default `Alt` + scancode `0x29` — `Alt+§`
  on a Nordic layout, `Alt+\`` on US. At least one modifier.
  Only acts while the Chat Box has focus; the chord itself is swallowed
  from the game. Bound from the settings window by pressing the key
  (ticket 12, verified in-game 2026-09-19; an extended key keeps its `E0`
  prefix, so Home and Numpad 7 are different chords). ADR-0004.
- Persistence: the Forwarding value is saved on every flip. Startup
  behaviour (config): **remember last** (default), **always off**,
  **always on**; applied when the session opens. Every flip prints
  `IME Bridge: forwarding on|off`, unconditionally. (Ticket 12, verified
  in-game 2026-09-19.)
- Slash Bypass: when the Chat Box is empty and no Composition is active, a
  leading `/` and the command word up to the first space are passed to the
  game untouched (so `/tell Name` then Japanese works without toggling).
  Decided at the `/` char; bypass ends at space, Enter, Escape or the box
  becoming empty again. A `/` mid-text is ordinary text. Only the command
  word: what follows it is Forwarded like anything else.
- Losing Chat Box focus mid-Composition cancels it: `Reset()` then
  `FocusOut()` (Mozc would otherwise commit on focus-out — verified M0.1).
  Committed text stays in the native buffer.
  The game *window* going inactive (alt-tab) with the Chat Box focused is
  a focus loss too, and the window coming back with the box still focused
  a focus gain (ticket 11, verified in-game 2026-09-19; found in M1
  acceptance: fcitx5 otherwise focuses our context out itself when the
  other window's context focuses in, and Mozc commits). Our `Reset` wins
  that race, so no Commit-dropping fallback exists.

### Keyboard capture

- Keep the native Chat Box focused while composing. The plugin's own hook on
  the game's `DispatchMessageW` import drops keyboard messages before Dalamud
  and the game (M0.3: no double input, fallback not needed). Dalamud's own
  ImGui capture is per-frame and all-or-nothing, so it is not used for this.
- The Gate decides per key **synchronously** (ADR-0002): the hook blocks
  the game thread on `ProcessKeyEvent`, 50 ms cap (~10 ms measured
  in-game). Unhandled keys pass through to the native input, as with
  XIM/Wayland frontends; a **timed-out key is swallowed** — never double
  input. Three consecutive timeouts, or fcitx5 leaving the bus, make
  Forwarding Degraded (gate passes everything, Indicator `!`, one chat
  line) until the next Chat Box focus gain retries; any reply resets the
  count. Enter with no Composition reaches the game and sends; Enter
  inside a Composition is consumed by fcitx5 and never sends.
- Only presses of non-modifier keys are waited on. Modifier presses and
  every release are *sent* fire-and-forget (fcitx5 never "handles"
  releases). The *verdict* is another matter: modifier presses and their
  releases always pass; any other release goes where its press finally
  went — for a printing key that is the verdict its `WM_CHAR` got, so the
  game sees the whole keystroke or none of it. `FocusIn`/`Reset`/`FocusOut`
  are fire-and-forget too; D-Bus delivers in order on one connection.
- Outside a Composition, Enter, Escape, Tab, Backspace, Delete, arrows,
  Home/End/PgUp/PgDn and F-keys pass **without asking** unless Ctrl or Alt
  is held (Ctrl+Space etc. are always asked). Inside a Composition
  everything is asked. Every auto-repeat is a fresh press.
- Every key event carries **both** a keysym and an X keycode (M0.1 found
  Mozc composes nothing with keycode 0). A printing key's keysym is the
  layout-translated character, which only exists at its `WM_CHAR`: so
  while Forwarding the keydown of a printing key is swallowed provisionally
  and fcitx5 is asked at the `WM_CHAR` (`KeySym.FromCodePoint`, keycode
  from the char's own scancode via `KeyCode.FromWindowsScanCode`). A
  provisional keydown whose char never comes is simply gone.
  Non-printing keys and Ctrl/Alt chords have fixed keysyms and are asked at
  keydown — except AltGr chords (Ctrl+Alt together, or right Alt), which
  print (`@{}[]\|~€` on a Nordic layout) and are asked at their `WM_CHAR` like
  any printing key. `WM_DEADCHAR` is swallowed silently (the composed
  character follows). `ForwardKey` is ignored and logged in M1.
- **Verified in-game (ticket 07, 2026-09-18):** the Chat Box inserts a
  `WM_CHAR` whose `WM_KEYDOWN` it never saw, so every declined printing key
  reaches it as its char alone. AltGr arrives from Wine as `vk=0xE4`
  (`ISO_Level3_Shift`), handled as a right-Alt modifier.
- When the wait returns, `State` and any commit are already in (signals
  precede the reply): the hook reads the snapshot, does the Native Write
  for a commit right there on the game thread, and Draw renders the
  snapshot. Events raised on the D-Bus reader thread are only for
  out-of-band changes and are queued to the framework tick.

### Rendering

- Preedit: ImGui, inline at the native cursor's screen position — the
  input's own caret node (`CursorContainer.ScreenX/Y`, exact and correct
  under horizontal scrolling; M0.4) — per-segment
  formatting from fcitx5 (underline; highlighted active segment), opaque
  background. If the cursor is mid-string the preedit overlaps trailing
  native text — accepted in v1.
- Preedit baseline on the Chat Box text's baseline (M2; M1 centred it in
  the cursor node and sat slightly low).
- Candidate list: anchored to the preedit; above it when the anchor is in
  the lower half of the screen (the Chat Box usually is), below otherwise;
  orientation from fcitx5's layout hint (Mozc: vertical); labels and paging
  as fcitx5 reports them.
- Mouse (M2): the overlay is a draw list, so clicks are handled in the
  message hook by hit-testing the last frame's rects, and the game never
  sees them — which is what "without disturbing native focus" required.
  Over the candidate box every button and the wheel are Swallowed: left
  click selects a row, click on `▲`/`▼` or the wheel pages. A click on the
  Preedit is Swallowed and does nothing. A click anywhere else passes
  untouched (in the Chat Box it moves the Cursor and the Preedit follows).
  No hover state: the selected row is fcitx5's. ADR-0003.
- Font: the game's AXIS font via Dalamud `IFontAtlas`; one handle for
  Preedit, candidates and Indicator. Size: the Chat Box text node's own by
  default, a config override otherwise.
- Indicator: single glyph where the game's own input-mode badge sits (left
  of the channel name), in that badge's style if the game's node can be
  found (ticket 14), while Forwarding is on; reflecting *our context's*
  `CurrentIM` (`あ` Mozc, `A` direct; fcitx5 keeps IM state per context and
  a new context starts on the keyboard layout — M0.1), `!` while Degraded.
  Can be hidden (vanilla look). ImGui-only: visible to the local player only.

### Native Path coexistence

fcitx5's XIM/Wayland frontends also see the Wine window and could fight our
Input Context for focus. **Not observed** (ticket 05, in-game 2026-09-18):
with the Chat Box focused and Forwarding on, the Indicator held `あ` through
clicks elsewhere in the game window and alt-tab, and fcitx5's own trigger
key never reached any context from the game. No `FocusIn` re-issue and no
`XMODIFIERS=` advice; revisit only if a later ticket sees the glyph revert.

### Transport and fallback ladder

The plugin is Windows .NET inside Wine; the session bus is a Unix socket.
1. AF_UNIX via Wine's `ws2_32` — works on wine-xiv 10.8 (M0.2). The bus
   address needs no translation; `AUTH EXTERNAL` must present the Linux
   uid, not the Windows SID.
2. If that fails: a tiny native Linux helper process (spawned via
   `wine start /unix`) bridging localhost TCP ↔ session D-Bus. A separate
   *process*, not a separate window.
3. Only then: rethink.

D-Bus library: `Tmds.DBus.Protocol` (low-level, no reflection proxies).

### Resilience and errors

Three states, `CONTEXT.md`: **Degraded** (Forwarding kept, nothing behind
it), **Inert** (no session at all), and **Reconnect** (the way out of Inert
and of a dead connection).

- fcitx5 disappearing (`NameOwnerChanged`) or not answering (ADR-0002):
  Forwarding is Degraded — behaves as off, Indicator shows `!` — while the
  user's Forwarding choice is kept underneath; Input Context is recreated
  automatically on return and Degraded lifts on the next Chat Box focus gain.
- The bus connection itself dying: Degraded as well, but only a
  Reconnect lifts it (ticket 13; `FcitxConnection.Disconnected`,
  host-tested through a relay socket).
- Unreachable on load (Linux): one chat line "IME Bridge: fcitx5 not
  reachable, disabled" and a toast if the toggle is pressed; the plugin is
  Inert.
- Reconnect: tear down and rerun the ladder, as on load. Automatic on
  a Chat Box focus gain while Inert or the connection is dead, at most once
  per 10 s, silent except one success line `IME Bridge: fcitx5 reachable,
  forwarding on|off` (which every successful open prints, load included);
  by hand via `/imebridge reconnect` or the settings window, which also
  report failure. The Forwarding choice survives. (Ticket 13, verified
  in-game 2026-09-19 for the by-hand path and the automatic path out of
  Inert; a bus restart was not tried.)
- Windows: plugin loads, does nothing, settings window explains.

### Codebase shape

- `src/FfxivImeBridge.Fcitx/` — Dalamud-free `net10.0` library: connect,
  create Input Context, `ProcessKeyEvent`, `FocusIn/Out`, `SetCapability`
  (`ClientSideInputPanel`), parse `UpdateClientSideUI`/`CommitString`,
  track `CurrentIM`, reconnect. Tested **on the host** against the real
  fcitx5 in `tests/`.
- `src/FfxivImeBridge/` — the Dalamud plugin: key mapping, native-buffer
  glue, ImGui rendering, config, commands.
- Commands: `/imebridge` (bare and `config` open settings; `toggle`,
  `indicator`, `debug`, `reconnect`, `probe`), alias `/ime`. The cog on
  Dalamud's plugin page opens settings. The debug window ships in every
  build (a bug report from an unknown Wine build needs its trace).
- Config (v1): Toggle Key, font size (match Chat Box / override), show
  Indicator, startup behaviour, last Forwarding value.
- Strings in one place, English only in v1.
- License MIT. Distribution: own third-party `repo.json` first; official
  repo later if there's demand. `.scratch/` is committed, `ref/` excepted
  (M3; see below).

### Release, identity and distribution (M3, grilled 2026-09-22)

- **Identity.** Everything public carries the handle `niku-0` —
  `LICENSE`, `<Authors>`, the plugin manifest and the commit identity.
  Development before `0.1.0` happened in a private repository; this one
  starts at a single `Initial commit`.
- **Pasted logs and paths are sanitised as they are pasted** into a
  ticket (`docs/agents/issue-tracker.md`); commits are made in UTC.
- **AI assistance stays visible**, stated once in the README. The ticket
  prose gives it away regardless, and every behavioural claim in it was
  verified in-game.
- **Version `0.1.0`**, tag `v0.1.0`, no testing track. One Wine build, one
  distribution, one IME, one layout are verified; the version number says
  so. The ladder's second rung has never been exercised in anger.
- **Packaging**: `Dalamud.NET.Sdk/15.0.0` replaces the deprecated
  `DalamudPackager` reference, after the user's M2 text pass and gated on a
  verified Linux build. API 15 no longer overwrites the in-zip manifest from
  `repo.json`, so the two must agree by hand.
- **Releases are built by CI** on a tag (`Blooym/setup-dalamud` sets
  `DALAMUD_HOME`), with `AssemblyVersion` and a tag-pinned `DownloadLink`
  written by the same job. The job fails if the tag and the csproj
  `<Version>` disagree, and records the Dalamud version it built against in
  the release notes. A locally built zip is built against whatever Dalamud
  sat in the xlcore directory that day; nobody could say afterwards what
  that was.
- **Goes public before the acceptance install.** Dalamud cannot fetch
  `repo.json` or a release asset from a private repository, so the
  repository is private until the M3 acceptance run and flips to public
  inside it, after the pre-publication check and before the install.
- **Support**: `AcceptsFeedback: false`; reports come to GitHub issues under
  a template that asks for the transport ladder's steps (a copy button in
  the debug window, home path shortened to `~`), never for `dalamud.log`
  unprompted — which is what ticket 18 bought. The debug window's Native
  Write report carries the Chat Box text, so the template asks for it only
  on request and warns about what's in it.
- **No environment setup in the README.** Ticket 05 found fcitx5's own
  frontends never fought our Input Context, so the README says no `XMODIFIERS`
  change is needed and why. This replaces the original M3 line's "incl.
  Native Path env setup".
- **Images** (icon, screenshots) are the user's to make and do not block
  `0.1.0`; the Square Enix UI-asset extracts in `ref/` are not published.

## Milestones

0. **M0 — Feasibility**, in order, each a separate ticket:
   1. ~~fcitx5 client library proven on the host~~ — done, see
      `src/FfxivImeBridge.Fcitx/README.md` for the protocol facts.
   2. ~~Dev plugin reaches the session bus from inside Wine, creates an Input
      Context, gets `UpdateClientSideUI` back.~~ — done, rung 1 (AF_UNIX via
      `ws2_32`) confirmed in-game 2026-09-18 with the Linux uid supplied for
      `AUTH EXTERNAL`. See ticket 02.
   3. ~~Keyboard capture: native Chat Box focused, keys swallowed, no double
      input.~~ — done, `DispatchMessageW` import hook confirmed in-game
      2026-09-18; Chat Box focus via the active text input's owner addon;
      § is `vk=0xDE sc=0x29` under Wine. See ticket 03.
   4. ~~`SetText`/cursor index on the ChatLog text input work as assumed.~~
      — done, confirmed in-game 2026-09-18: code-point cursor on
      `AddonChatLog.TextInput`, `SetText` + cursor write holds, 500-byte
      limit refused before the write, caret node is the preedit anchor.
      See ticket 04.
1. ~~**M1 — Minimal working composition**: toggle → compose → candidates →
   Commit → Native Write → native Enter sends. Indicator, Slash Bypass.~~
   — done, accepted in-game 2026-09-18 (ticket 10). Tickets 05 (session +
   Indicator), 06 (key translation), 07 (Gate), 08 (Preedit + candidates),
   09 (Native Write), 10 (in-game acceptance). Carried into M2: alt-tab
   mid-Composition commits (ticket 11).
2. ~~**M2 — Polish** (planned 2026-09-18, grilled): 11 alt-tab cancels the
   Composition; 12 settings window, persisted config, bindable Toggle Key;
   13 Reconnect; 14 AXIS font and the Indicator in the game's badge style;
   15 preedit baseline; 16 mouse selection; 17 in-game acceptance.~~ —
   done, accepted in-game 2026-09-21 (ticket 17); its one finding, the
   Cursor left at the start of the written text after a Native Write,
   fixed and confirmed the same day (ticket 20, five in-game rounds: the
   splice edits at the module's split, only `UpdateTextSelection` draws
   the cursor, and its strings must be the raw text).
3. **M3 — Public release** (planned 2026-09-22, grilled): 21 the public
   repository; 22 `Dalamud.NET.Sdk`; 23 `repo.json` and the release
   workflow; 24 README; 25 issue template and support posture; 26 icon and
   screenshots (human, not a blocker); 27 acceptance — a stranger installs
   it from the published repository. Third-party repo conventions confirmed
   against goatcorp sources 2026-09-22; see ticket 23. Plan reviewed
   2026-09-23 (going public inside 27, a copyable ladder report). Done when
   27 passes; the repository flips to public partway through 27.
4. **M4 — Stretch**: ibus; fields beyond chat; Japanese UI strings.

## Key risks

- AF_UNIX from Wine (M0.2) — works on wine-xiv 10.8 in the game (ticket
  02); other Wine/Proton builds unverified.
  Fallback would still be the helper process (Tmds accepts an arbitrary
  stream via `SetupResult.ConnectionStream`).
- Native buffer write/cursor semantics (M0.4) — verified for plain text
  (ticket 04); buffers holding auto-translate payloads are untested.
- fcitx5 protocol drift and FFXIVClientStructs drift (the ChatLog addon
  layout) — expect upkeep.
