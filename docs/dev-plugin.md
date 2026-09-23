# Building and loading the dev plugin

## Build

```
dotnet build src/FfxivImeBridge            # → src/FfxivImeBridge/bin/Debug/FfxivImeBridge.dll
dotnet build src/FfxivImeBridge -c Release # + bin/Release/FfxivImeBridge/latest.zip (DalamudPackager)
```

The plugin project builds on `Dalamud.NET.Sdk`, which references Dalamud's
assemblies from `$DALAMUD_HOME`, else `~/.xlcore/dalamud/Hooks/dev/`
(XIVLauncher.Core), and fails the build if that folder is missing. Nothing
from there is copied to the output; `FfxivImeBridge.Fcitx.dll` and
`Tmds.DBus.Protocol.dll` are, because Dalamud resolves a plugin's
dependencies from its own folder. The tests copy `Dalamud.dll` from the same
place.

`FfxivImeBridge.json` is the manifest. The packager bundled with the SDK
copies it to the output and fills in `InternalName`, `AssemblyVersion` (from
`<Version>` in the csproj) and `DalamudApiLevel` (15, which must equal the
running Dalamud's major version). Dalamud no longer replaces the installed
manifest with the `repo.json` entry, so the two must agree. The release
workflow guarantees it by making `repo.json` from the manifest.

## Release

Bump `<Version>` in `src/FfxivImeBridge/FfxivImeBridge.csproj`, commit, tag
`v<Version>`, push both. `.github/workflows/release.yml` then:

1. fails unless the tag is `v` + `<Version>`;
2. installs the release branch of Dalamud, runs `dotnet test` (the fcitx5
   tests skip without a session bus) and builds Release;
3. creates the GitHub release with `FfxivImeBridge.zip` attached, noting the
   Dalamud version it was built against;
4. commits `repo.json` to `main`: the manifest inside that zip, plus
   `DownloadLinkInstall`/`DownloadLinkUpdate` pinned to the asset.

Pull afterwards: `main` has moved. `repo.json` is never edited by hand. If
the run fails after creating the release, delete the release (not the tag)
and re-run the job.

## Load it as a dev plugin (XIVLauncher.Core, Dalamud 15)

Dalamud only scans the locations listed in `DevPluginLoadLocations` in
`~/.xlcore/dalamudConfig.json`. Add one from inside the game rather than by
editing the file — Dalamud rewrites the file when it saves settings:

1. `/xlsettings` → **Experimental** → *Dev Plugin Locations*: add the Windows
   spelling of the DLL path, e.g.
   `Z:\home\<user>\ffxiv-ime-bridge\src\FfxivImeBridge\bin\Debug\FfxivImeBridge.dll`,
   then *Save and Close*.
2. `/xlplugins` → **Dev Tools** → *Installed Dev Plugins* → enable
   **FFXIV IME Bridge**. It loads immediately and runs the transport ladder.
3. `/imebridge debug` (alias `/ime debug`) opens the debug window: the
   **Transport** tab has the ladder, `/imebridge probe` runs it again, and
   **Copy ladder** puts it on the clipboard for a bug report, the home
   directory shortened to `~` (Wine hides `HOME` from the game and passes it
   as `WINEHOMEDIR`; `ProbeReport.ToShareableText`). Bare
   `/imebridge` opens the settings window (below). Every step is also
   written to `~/.xlcore/logs/dalamud.log` as `Transport ladder: …`, and the
   one-line result is printed to chat.

## Forwarding session and Indicator (M1.1)

When the transport ladder passes, the plugin opens a live connection of its
own (the ladder's was a throwaway; `/imebridge probe` still uses one) and
creates one fcitx5 Input Context for the life of the plugin. Its focus
follows the Chat Box: focus gained → `FocusIn`, focus lost → `Reset` then
`FocusOut` (Mozc would otherwise commit on focus-out). "Focused" means the
box has the game's text-input focus **and the game window is active**: an
alt-tab leaves the box focused as far as the game is concerned, but the
window you switch to has its own fcitx5 context and fcitx5 focuses ours out
for it — Mozc then commits, and the text would land in the box (ticket 11).
So the tick polls `Framework.WindowInactive` (window activation is a *sent*
message the pump hook never sees) and treats the window going inactive as a
focus loss and coming back as a gain; the debug window's focus line shows
`windowActive=`. The context starts on
your keyboard layout, as any new fcitx5 client does; switch it with your
usual fcitx5 hotkey (Ctrl+Space) — the plugin never forces Mozc. The state
is kept per context, so it survives Escape and clicking back in. When the
ladder holds, chat says `IME Bridge: fcitx5 reachable, forwarding on|off`;
if it fails, `IME Bridge: fcitx5 not reachable, disabled` and the toggle
only shows a toast (see Reconnect below).

- **Forwarding** is the user's switch: the Toggle Key — by default `Alt` +
  the key left of `1` (`§` on a Nordic layout, `` ` `` on US; matched by scancode
  `0x29`, rebindable in the settings window) — while the Chat Box is
  focused, `/imebridge toggle` (`on`/`off` optional), or the Forwarding
  checkbox in the settings or debug window. Every flip prints `IME Bridge:
  forwarding on|off` and saves the value; what it starts as on load is the
  config's startup behaviour (see Settings).
- **Degraded**: Forwarding with nothing behind it — the gate passes
  everything, the Indicator shows `!`, your Forwarding choice is kept
  underneath. Three causes, each one chat line on entry, and a worse cause
  on top of a lesser one upgrades silently: fcitx5 *not answering* (three
  timeouts, ticket 07) lifts on the next Chat Box focus gain; fcitx5
  *leaving the bus* (`NameOwnerChanged`) lifts on the next focus gain after
  it is back, with a new context; the *connection itself dying*
  (`IME Bridge: lost the connection to fcitx5, forwarding degraded`;
  `FcitxConnection.Disconnected`, Tmds' `DisconnectedAsync`) lifts only by
  a Reconnect, since nothing on a dead connection can be recreated.
- **Inert**: the ladder failed, on load or on a Reconnect, and nothing is
  hooked into; the Chat Box is vanilla and the toggle only toasts. Unlike
  Degraded there is no Forwarding choice underneath. Left by a Reconnect.
- **Reconnect** (M2.3): tear down whatever is left of the session and the
  connection (Reset, FocusOut, destroy the context, drop the connection —
  each bounded by the unload budget, each failure only logged) and climb
  the ladder again exactly as on load, the startup behaviour re-applied
  from the config. A Composition in flight is simply gone. Automatic on a
  Chat Box focus gain while Inert or the connection is dead, at most once
  per 10 s (load and manual attempts count), silent unless it succeeds:
  `IME Bridge: fcitx5 reachable, forwarding on|off`. By hand, whatever the
  clock says, via `/imebridge reconnect` or the settings window's
  **Reconnect**, which also report failure (the ladder's line, then `not
  reachable, disabled`). One attempt at a time; a request while one runs
  is ignored (a toast). `/imebridge probe` stays a diagnostic: throwaway
  connection, no session. The state machine is `Session/SessionLifecycle.cs`
  behind the `ISessionSource` seam (`LadderSessionSource` in `Bridge.cs` is
  the real ladder), host-tested in `SessionLifecycleTests`; the connection's
  death in `FcitxConnection.Disconnected`, host-tested through a relay
  socket the test pulls away (`DisconnectTests`).
- **Indicator**: one glyph drawn where the game's own input-mode badge sits
  — in the gap the channel label (`Say`, `AddonChatLog.CurrentChannelTextNode`)
  reserves for it: the label's text begins with a full-width space and a
  space, and the glyph goes there, 5 px (× HUD scale) left of the text
  input's text edge and centred on the label — while the box is focused
  and Forwarding is on —
  `あ` for Mozc, `A` for a keyboard layout, the input method's initial
  otherwise, `!` while Degraded. It follows the `CurrentIM` signal fcitx5
  sends after every `FocusIn`. Drawn in AXIS at the *label's* font size
  (14 pt on the reference client; never the chat text's size or the
  override) and in the label's own colours, its edge colour as a one-pixel
  outline (`!` keeps its warning colour); a second handle on the overlay's
  atlas (M2.4 below). That is the **Plain glyph** style; the settings
  window's **Badge** style (`IndicatorStyle` in the config) draws the
  Windows client's badge itself: `Assets/badge-hiragana.png`, a 22×23 px
  crop of that client's `あ` badge from a screenshot (the crop's four
  corner pixels made transparent), as an ImGui image at its own size
  ÷ 1.44 (the crop read too large in-game, twice by a fifth; `BadgeScale`)
  × HUD scale through
  `ITextureProvider.GetFromFile` (`GetWrapOrDefault` each frame; until it
  has loaded the plain glyph draws instead). Any other glyph (`A`, an
  initial, `!`) goes over `Assets/badge-frame.png` — the same crop with the
  `あ` painted out in the body's cream — centred in the frame's cream by
  its ink (the font's own glyph bounds, `ImFont.FindGlyph`) in the label's
  edge gold, its edges softened (four faint half-pixel copies under it).
  The PNGs are copied next to the DLL at build. Without a label it sits
  left of the text node at the overlay's size in the Preedit's ink. Hidden
  with the game UI like the overlay. The badge is not borrowed from the
  game: ticket 14's node dump found no badge node in the ChatLog addon
  (the Windows client's IME layer paints it outside the Atk tree), so
  there is nothing to reuse and nothing of the game's to hide behind;
  hence the shipped crop.
  `/imebridge indicator [on|off]` (or the settings window) hides it for a
  vanilla look; the choice is saved.
- Until ticket 07 forwards keys, Ctrl+Space cannot reach the context, and a
  switch from a host terminal lands on the terminal's own context. To test
  the `あ` glyph: `/imebridge im` (optionally a name; default `mozc`) waits
  3 s, click back into the Chat Box, and the focused context is switched.
- The Keyboard tab's first line is the session state (forwarding, degraded,
  gate, input method, glyph); **Copy trace** puts it at the top of the text.
  Session events go to `dalamud.log` as `Session: …`.

## Settings and the config file (M2.2)

The cog on the plugin's `/xlplugins` entry, bare `/imebridge`, `/imebridge
config` and Dalamud's "open" action all open the settings window; the debug
window is `/imebridge debug` and ships in every build. Every control writes
`~/.xlcore/pluginConfigs/FfxivImeBridge.json` at once (Dalamud's
`SavePluginConfig`; enums by name so the file reads at a glance). A missing
or older file loads the defaults, unknown fields are ignored.

- **Forwarding**: the same switch as the chord and the command; disabled
  with a note while the ladder is still climbing or the plugin is Inert, a
  note while the connection is dead. **Reconnect** under it (see the
  Forwarding section); disabled while one runs.
- **Toggle Key**: shown as modifiers + what the current layout prints on the
  scancode (`Alt+§`; `Alt+F5`; `Alt+sc 0x29` for a key nothing names), three
  modifier checkboxes and **Press a key**. The button puts the message hook
  into capture: the next non-modifier keydown, wherever focus is, becomes
  the key (with the modifiers held at it, else the checkboxes' — at least
  one is required, and unchecking the last is refused), Escape cancels, and
  the captured key is swallowed whole (keydown, char, release; trace rule
  `Captured`). Stored as `ChordModifiers` flags + set-1 scancode (ADR-0004;
  an extended key keeps its `E0` prefix, so Home is `0xE047` and Numpad 7
  `0x47`); a chord with no modifier is unbound and matches nothing. The gate reads the
  chord from the config on every message, so a change applies at once.
- **Preedit font size**: "Match chat box" (stored as `0`) or a slider in
  screen pixels; the overlay's one AXIS handle is built at it (M2.4).
- **Show Indicator**: the glyph before the channel name, and its style:
  plain glyph or the Windows client's badge.
- **On load, forwarding is**: as it was last (default) / always off / always
  on — applied to the session as it opens, before the gate sees a key, so
  the first chat line after `Session: open` says which.
- **Windows** (not Wine, `Util.IsWine()`): the plugin loads and does nothing
  — no `KeyboardCapture`, so the message pump is not hooked, and no
  `bridge.Start()`, so no session is opened (ticket 19). Both windows still
  open from the command and the cog: the settings window and the debug
  window's Keyboard tab show the one line saying the plugin only works on
  Linux, and `/imebridge probe` still climbs the ladder into the Transport
  tab, whose first rung says why it stops.

Every line the user reads is in `Strings.cs`. Host tests: `ConfigurationTests`
(defaults, the round-trip through Dalamud's serializer settings, older and
unknown fields, startup → initial Forwarding) and the Toggle Key and capture
cases in `KeyboardGateTests`.

## Keyboard capture and the Gate (M0.3, M1.3)

On load the plugin hooks the game's `user32!DispatchMessageW` import (the
same import Dalamud hooks to reach the game window) and sees every keyboard
message before Dalamud, ImGui and the game do. With Forwarding off it changes
nothing. With it on (and not Degraded), every key typed into the focused Chat
Box goes through the Gate, which asks fcitx5 and blocks the game thread on
the answer, 50 ms at most (ADR-0002): handled ⇒ swallowed, declined ⇒ passed,
no answer ⇒ swallowed. The game sees a whole keystroke or none of it.

- A printing key's keysym only exists at its `WM_CHAR`, so its keydown is
  swallowed *provisionally* and fcitx5 is asked at the char; the char's
  verdict is the press's, and its release follows. Non-printing keys and
  Ctrl/Alt chords are asked at their keydown. AltGr chords print and are
  treated as printing keys.
- Outside a Composition, Enter, Escape, Tab, Backspace, Delete, arrows,
  Home/End/PgUp/PgDn and F-keys pass without asking (trace `Skipped`);
  inside one everything is asked. Ctrl+Space and other chords are always
  asked, so fcitx5's own trigger key works inside Forwarding.
- Modifier presses and every release are *sent* fire-and-forget, never asked;
  modifiers always pass. Every auto-repeat is a fresh press.
- **Slash Bypass**: a `/` typed into an empty Chat Box with no Composition
  passes unasked (trace `Bypass`) and so does the command word up to and
  including the space that ends it; Enter, Escape or the box reading empty
  end it too. A `/` mid-text is ordinary text.
- Three timeouts in a row (no reply between) put Forwarding into Degraded
  (`IME Bridge: fcitx5 is not answering, forwarding degraded`, Indicator `!`);
  the next Chat Box focus gain lifts it without a new context. A late reply
  still changes fcitx5's state and may commit; the framework tick picks that
  up.
- After a waited reply the hook takes the state snapshot and drains the
  commit queue into the Native Write on the game thread, before the next
  message (see below).
- The toggle chord is itself swallowed and is checked before anything is
  asked: every listed modifier held, none of the other of Ctrl/Alt/Shift,
  same scancode. `ForwardKey` from fcitx5 is logged and ignored.
- The Keyboard tab shows the focus readout (`IsTextInputActive`, the addon
  owning the active text input, UI hidden), how many messages were asked
  with the longest and mean wait, and a trace of the last 200 messages the
  gate acted on: verdict, key class, the rule that decided it (`Provisional`,
  `Answered`, `TimedOut`, `Skipped`, `Bypass`, `FollowsPress`, `Modifier`,
  …), the wait in ms if asked, and the message. Keys typed while Forwarding
  is off, or outside the Chat Box, are **not** recorded — the trace goes on
  the clipboard and into the log, and a `WM_CHAR` names the character typed,
  so mail, the FC board and market search stay out of it (ticket 18); a
  keydown the gate passes untouched is `Inactive`, and so are its char and
  its release. `Messages seen`/`swallowed` still count every keyboard
  message, traced or not (mouse messages are counted only when traced, as
  before). Mouse messages over the overlay are in the same trace (M2.6) with
  their own rules — `Candidate`, `Page`, `Preedit`, `CandidateBox`,
  `Outside`, `FollowsPress` — and the point in client pixels, so a wrong
  hit-test is visible; clicks while nothing is shown are not traced.
  **Copy trace** puts the readout and the whole trace on the clipboard as
  text (ImGui text cannot be selected). The same lines also go to
  `dalamud.log` at Debug level as `Keyboard capture: …`, so lower the log
  level in `/xlsettings` if you want them there too.
- To test: open the debug window, click into the chat box (not the ImGui
  window), turn Forwarding on, type. On `A` (direct input) letters appear as
  before, each keydown `Swallow Provisional` and each char `Pass Answered`;
  on `あ` the letters are swallowed and the trace shows waits of ~10 ms.

## Preedit and candidates (M1.4)

While the Chat Box is focused and the session's composition snapshot has a
preedit, the plugin draws it on the ImGui foreground list (no window, no
focus, local player only), inline at the Cursor: the input's cursor node
(`CursorContainer`, ticket 04) is the anchor, the text node's left edge plus
the drawn width of the text before the cursor the fallback. The game's own
AXIS face at the Chat Box text's size (or the config override; M2.4 below),
so the text follows the chat window's font size and HUD scale.

- Preedit: an opaque ground the width of the text, then each segment with the
  flags fcitx5 sent — underline, a filled highlight for the segment being
  converted, faux bold, strike — and a 1 px bar at fcitx5's cursor (a UTF-8
  byte offset, mapped to a char index). Mid-string it overlaps the trailing
  native text; accepted in v1.
- Candidate list: a box anchored to the preedit — above it when the Cursor is
  in the lower half of the screen, below otherwise, clamped to the screen. A
  column for fcitx5's vertical hint (Mozc), a row otherwise. Rows are label +
  text as reported (`2. か [ひらがな]`: Mozc's annotation stays), the selected
  row filled; `▲`/`▼` for `hasPrev`/`hasNext` (a footer line in a column, end
  cells in a row); aux text above and below when non-empty. Mozc's prediction
  list (empty labels, no selection, `[Tabキーで選択]` above) shows while
  typing, as in fcitx5's own panel. Keys reach fcitx5 through the Gate;
  nothing here handles them.
- Mouse (M2.6, ADR-0003): the overlay is a draw list, so the `DispatchMessageW`
  hook also sees button and wheel messages (never `WM_MOUSEMOVE`) and hands
  them to `Capture/MouseGate.cs`, which hit-tests the plan the overlay kept
  from the last frame (`CompositionOverlay.LastPlan`; the plan carries a
  `HitRegion` per candidate row/cell and per `▲`/`▼`, plus the two boxes).
  While the Gate is active and a Composition is shown: over the candidate
  box every press, its release and the wheel are Swallowed — a left press on
  a row calls `SelectCandidate(i)`, on `▲`/`▼` or a wheel notch up/down
  `PrevPage`/`NextPage` (fire-and-forget; the `UpdateClientSideUI` it causes
  arrives out of band and the tick renders it), other buttons and the padding
  do nothing; over the preedit Swallowed and inert; anywhere else passed
  untouched, so a click elsewhere in the Chat Box moves the native Cursor
  and the preedit re-anchors next frame. A release goes where its button's
  press went, wherever it is released; a double-click's second press is a
  press. No hover state: the selected row is fcitx5's. Host-tested in
  `MouseGateTests` and the layout tests; the trace shows the decisions.
- Hidden with an empty preedit, an unfocused Chat Box or a hidden UI. Nothing
  is written to the game: only the cursor node (or text node) is read. No
  `SetCursorRect` is sent, since nothing of fcitx5's is on screen.
- The Keyboard tab's second line is the snapshot the overlay draws (segments
  with their flags, cursor, candidates, selection, layout, paging, aux), also
  at the top of **Copy trace** — to tell a wrong drawing from a wrong snapshot.
- Layout is pure (`Rendering/CompositionLayout.cs`: snapshot + Cursor anchor →
  draw ops) and host-tested; the drawing itself is the in-game check in
  ticket 08.
- The Preedit's baseline is the Chat Box text's (M2.5): the text node's top
  plus AXIS's ascent at the node's size (`PushedFont.AscentAt`), minus the
  overlay font's own ascent for the line's top; the Chat Box tab's `preedit:`
  line shows the tops and the baseline so a remaining offset can be read off.

## Native Write (M1.5)

A Commit from fcitx5 is written into the game's own chat input
(`AddonChatLog.TextInput`, an `AtkComponentTextInput`) at the Cursor, with
the Cursor left after it: read the text and the cursor index (code points,
the input module's while the Chat Box is its target, else the component's —
ticket 04), plan the splice, write the cursor index after the text, let the
game's own `InsertText` splice, then `UpdateTextSelection` on the
component with that index so it draws the cursor there. `InsertText` rather
than `SetText` for the splice because only the game's splice refreshes the
input module's copy of the text — the before/after split its own keystrokes
edit at, and what the game hands back to the component when the box loses
focus; a `SetText` write vanished on clicking out of the box (ticket 09).
The splice lands at that split and then rebuilds the split from the cursor
index, which is why the index is written *before* it: written after, the
index moved but the split stayed where the splice began, and so did the
next keystroke (ticket 20). The drawn cursor follows none of that: only the
module's own `UpdateTextSelection` notice to the component moves the cursor
node to the index (`SetText` moves it to the end of the text). The notice
carries the module's *raw* input string for both of its strings and the
raw code-point count: the splice leaves the module's evaluated string and
`TextLength` as the old text plus the new, the component copies whatever it
is handed into its evaluated string, and the game rebuilds the box from
that on the next focus-in (ticket 20). The Chat Box tab's readout shows the
module's strings as text for exactly this kind of question.
`SetText` alone remains for a cursor that cannot be trusted
(append-at-end). Nothing is ever sent (ADR-0001): the text sits in the box
until you press Enter yourself.

- Two drains, both on the game thread: the message hook writes a Commit
  right after the waited `ProcessKeyEvent` that produced it (so a later
  passed key cannot land first), and the framework tick writes one that
  arrived out of band (a reply after its timeout, fcitx5 committing on its
  own).
- A Commit is sanitised before it is counted or spliced: code points below
  `U+0020` and `U+007F` are dropped (ticket 19). fcitx5 can commit any
  string, `InsertText` sanitises on its own account, and the splice already
  refuses a buffer holding `0x02` — the SeString payload start byte — so it
  must not introduce one either. `SplicePlan.Committed` is the text as it
  goes in and `ControlsDropped` the count, which the Information line carries
  as `text N bytes / M code points (K control characters dropped)`.
- Overflow is decided before anything is written: the Chat Box's limit is
  500 bytes (`MaxByte`, the handler value first), and a Commit that would
  push past it is refused with `IME Bridge: the committed text does not fit:
  N bytes (about M characters) over the limit of 500 bytes` — M at 3 bytes
  per kana or kanji, rounded up. Nothing is truncated; the refused text is
  in `dalamud.log` at Debug level.
- Focus is not required: a Commit that lands after the Chat Box lost focus
  is still written if the box exists. Only a missing box loses the text, to
  the log (`Native Write: failed …`).
- The **Chat Box** tab in the debug window shows the live readout — raw text,
  both cursor fields (the component's own and the input module's), the
  module's text-before/after-selection byte lengths, every limit the game
  states, the text node and cursor node screen boxes, the measured cursor
  x and the input's style (font size, the game's IME and candidate colours,
  which ink the Preedit gets) — and the last Native Write's report (before / right after the write /
  two frames later, with whether the text and cursor held). **Copy readout**
  puts both on the clipboard as text, **Copy report** the report alone.
  **Show cursor markers** draws a red line at the input's cursor node and a
  blue one at text node + measured width so both can be compared with the
  real caret. **Dump input nodes** (M2.4) writes the ChatLog addon's whole
  node tree, one line per node, to the clipboard, to `dalamud.log` at Debug
  level as `Node dump: …` and to the tab: id, type, screen box, visibility,
  alpha, flags, timeline label; a text node's string, font type/size, colours and
  edge/glow flags; an image or nine-grid node's part, UV rectangle, asset
  and texture path (`UldAsset → AtkTexture.Resource → TexFileResourceHandle
  → FileName`); a component's type. The text input's component, text and
  cursor nodes and the channel label are marked `<<`. Reads only.
- Every write also goes to `dalamud.log` as `Native Write: …`: at
  Information the outcome, where it went, the byte and code-point counts
  before and after, the cursor indices and whether the text held at the next
  frame — and at Debug the whole report, which is the only place the
  committed text and the Chat Box contents appear (ticket 18: `dalamud.log`
  is a file users upload to support channels). A refused Overflow and a
  failed write are one Warning with the reason and the counts, and the text
  itself at Debug. The tab's report and **Copy report** keep the full text:
  that is the user asking for it.

## Font and the Indicator's place (M2.4)

The Preedit, the candidate list and the Indicator draw with AXIS handles
from `Rendering/OverlayFont.cs`: an `IFontAtlas` of the plugin's own
(`IUiBuilder.CreateFontAtlas`, async rebuild, *not* global scaled, so a
requested pixel size is the size on screen) with one `NewGameFontHandle`
slot for the overlay and one for the Indicator (the channel label's size).
The overlay's size is
`Rendering/OverlayFontSize.cs` (pure, host-tested): the config override as
screen pixels when set, else the Chat Box text node's `FontSize` — the
game's pt unit, where AXIS_12 draws 16 px lines at 100 % — × 4/3 (the rule
Dalamud's `GameFontStyle` applies too) × the cursor node's accumulated
scale, rounded to whole pixels, never under 8. A new size builds a second
handle asynchronously while the current one keeps drawing, scaled by ImGui
to the wanted size, and takes over once it is available (one build at a
time, so a dragged slider costs one rebuild per landing); only the first
build ever has nothing but ImGui's current font to fall back on.
The Preedit's ink is the game's own IME colour (`ComponentTextData.IMEColor`,
a `ByteColor` on the text input's ULD data, the addon's handler value first
when set — there is no such `ConfigOption`) when it reads on our dark ground
(alpha ≥ 50 %, brightest channel ≥ 0x60; `NativeWrite/ChatBoxStyle.cs`,
host-tested), else white as before; candidates stay white. On the client
this was developed on the IME colour is a light green, `#FF45FF7E` as the
readout prints it (R low byte: 126, 255, 69), and the candidate colour a
gold. The Chat Box readout shows both colours and which ink is in use.

### Reload loop

Rebuild, then in `/xlplugins` → *Installed Dev Plugins* use the plugin's
**Reload**. Turning on *Automatic reloading* in the plugin's dev settings
(`DevPluginSettings.AutomaticReloading`) makes Dalamud watch the DLL and
reload on every build instead. On unload the plugin cancels a running
ladder, focuses out and destroys its live input context and drops the
connection, all within 5 s *in total*: `Plugin.Dispose` starts one stopwatch
and hands the session's teardown and then the ladder what is left of the
budget (ticket 19 — each used to wait 5 s of its own, so an unload with
fcitx5 hung froze the game for ten). If that is cut short, fcitx5 reaps the
context when the connection drops.

### Without the game

The same ladder runs as a console app, on the host or under Wine with the
Windows .NET runtime Dalamud ships:

```
dotnet run --project tools/FfxivImeBridge.TransportProbe

WINEPREFIX=/tmp/imebridge-prefix WINEDLLOVERRIDES="mscoree=b;mshtml=d" \
  ~/.xlcore/compatibilitytool/wine/wine-xiv-*/bin/wine \
  ~/.xlcore/runtime/dotnet.exe \
  tools/FfxivImeBridge.TransportProbe/bin/Debug/net10.0/FfxivImeBridge.TransportProbe.dll
```

Use a throwaway `WINEPREFIX`, not the game's. `mscoree=b` matters: with the
builtin disabled, Wine refuses to map IL-only assemblies ("Module not
found" for `System.Runtime.dll`).
