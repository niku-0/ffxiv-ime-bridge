# M2.2 — Settings window, persisted config, bindable Toggle Key

Status: resolved
Type: task
Blocked by: 11

The plugin gets a config file and a small settings window so a release user
never has to open the debug window. Everything the spec lists under "Config
(v1)" plus the Forwarding switch and the Windows explanation line (grilling
2026-09-18, Q3–Q6, Q12, Q14).
Spec: "Forwarding" (toggle key, persistence), "Resilience and errors"
(Windows), "Codebase shape" (commands, config). ADR-0004 (scancode).

## Done when

### Config

- `Configuration : IPluginConfiguration` (`Version`, saved through
  `IDalamudPluginInterface.SavePluginConfig`) holding: `ToggleKey`
  (modifiers Ctrl/Alt/Shift as flags + scancode; default Alt + `0x29`),
  `FontSizeOverride` (float, `0` = match the Chat Box; consumed by ticket
  14, stored here), `ShowIndicator` (default true), `Startup`
  (`RememberLast` default / `AlwaysOff` / `AlwaysOn`), `Forwarding` (the
  last value, written on every flip). A missing or older file loads the
  defaults; unknown fields are ignored.
- `ForwardingSession.Forwarding` is applied from config when the session
  opens: `RememberLast` → the saved value, `AlwaysOn` → on, `AlwaysOff` →
  off. Every flip (chord, command, checkbox) saves. The flip chat line
  `IME Bridge: forwarding on|off` stays, unconditionally.
- `Indicator.Visible` reads the config; `/imebridge indicator [on|off]`
  writes it (and saves).
- `KeyboardGate` takes the Toggle Key from config (modifier flags + scancode
  instead of the hard-coded `AltHeld && ScanCode == 0x29`): every listed
  modifier held, no other of the three held, scancode equal. At least one
  modifier is required; the settings UI enforces it and the gate treats a
  chord with none as unbound.

### Settings window

- A `Window` of its own (`SettingsWindow`, WindowSystem), opened by
  `UiBuilder.OpenConfigUi` (the cog on Dalamud's plugin page), bare
  `/imebridge` and `/imebridge config`. `/imebridge debug` opens the debug
  window, which stays in every build; `OpenMainUi` opens settings too.
- Controls, top to bottom: Forwarding checkbox (disabled with a note while
  Inert or still connecting); Toggle Key — the current chord shown as
  modifiers + the character the layout produces for the scancode (`Alt+§`,
  falling back to `Alt+sc 0x29`), three modifier checkboxes and a
  **Press a key** button that captures the next non-modifier `WM_KEYDOWN`
  from the message hook (Escape cancels; the captured key is swallowed);
  Font size — "Match chat box" checkbox, slider when unchecked; Show
  Indicator checkbox; Startup behaviour radio (remember last / always off /
  always on). A **Reconnect** button arrives with ticket 13.
- Windows (not Wine — `Dalamud.Utility.Util.IsWine()`): the window shows
  one line saying the plugin only works on Linux via fcitx5 and does
  nothing here; the controls are hidden.
- Command help text updated; strings stay in one place.

### Docs and tests

- `docs/dev-plugin.md`: a Settings section; the "(persistence is M2)" and
  "(config is M2)" notes go.
- Host tests: config defaults and round-trip; `Startup` → initial
  `Forwarding` for all three; the gate's chord matching with each modifier
  set, a chord with the wrong extra modifier held, and an unbound chord;
  capture mode returns the pressed key and Escape cancels.

## In-game confirmation (human)

1. Cog on `/xlplugins` → the settings window opens; `/imebridge` and
   `/imebridge config` open it; `/imebridge debug` still opens the debug
   window.
2. Toggle Key: Press a key, press `Ctrl+J` → shows `Ctrl+J`; in the Chat
   Box `Ctrl+J` flips Forwarding and `Alt+§` no longer does. Set it back.
3. Forwarding on, `/xlplugins` → disable and re-enable the plugin with
   startup = remember last: forwarding on after the session opens (chat
   line). Always off → off. Always on with the saved value off → on.
4. Show Indicator off → no glyph while composing; on → back.
5. On the file: `~/.xlcore/pluginConfigs/FfxivImeBridge.json` holds the
   values above.
6. Set `Status: resolved`.

Safety: no new game-memory access; the capture mode swallows one keydown.

## Comments
- 2026-09-19 (agent): implemented; awaiting the in-game confirmation above.
  `Configuration : IPluginConfiguration` (`Configuration.cs`) is a plain
  object with the five fields; `ConfigStore` loads it through
  `GetPluginConfig` (a missing, unreadable or older file → the defaults,
  `Configuration.Upgrade`) and saves through `SavePluginConfig`, logging a
  failed save instead of breaking the change. Enums are written by name
  (`"Modifiers": "Alt"`, `"Startup": "RememberLast"`) so step 5 reads at a
  glance. The Toggle Key is `ToggleKey` (`ChordModifiers` flags + scancode,
  `Capture/ToggleKey.cs`): `Matches` wants every listed modifier, none of
  the other three, same scancode; no modifier = unbound = matches nothing.
  The gate reads it from the config on every message. "Press a key" is a
  gate capture mode (`BeginCapture`): the next non-modifier keydown, wherever
  focus is, is reported with the modifiers held at it and swallowed whole
  with its char and release (trace rule `Captured`); Escape reports null.
  The window keeps the checkboxes' modifiers when none were held and
  refuses to uncheck the last one. `Bridge` applies `InitialForwarding` to
  the session before publishing it and saves on every flip through
  `SetForwarding` (chord, command, both checkboxes); `Indicator.Visible`
  reads `ShowIndicator`, `/imebridge indicator` writes it. `SettingsWindow`
  is opened by `OpenConfigUi`, `OpenMainUi`, bare `/imebridge`, `config`
  and any unknown word; `debug` opens the debug window, whose Indicator
  checkbox moved to settings. On Windows proper (`!Util.IsWine()`) the
  window shows only the one line. Every user-facing line is in
  `Strings.cs`. Host tests: `ConfigurationTests` (defaults, round-trip
  through Dalamud's Newtonsoft settings, unknown/missing fields, older file,
  startup → initial Forwarding, readable file) and `KeyboardGateTests`
  (config chord, all seven modifier sets, extra modifier held, unbound,
  chord description, capture with/without modifiers, unfocused capture,
  Escape, the bound chord during capture, external cancel, an extended
  key vs its numpad twin); 238 in the plugin suite, 17 in the library's. The tests copy `Dalamud.dll` and
  `Newtonsoft.Json.dll` from `$(DalamudLibPath)` (now in
  `Directory.Build.props`) so the config type loads on the host.
  `docs/dev-plugin.md` gains a Settings section. Not done: the Reconnect
  button (ticket 13); the font size is stored, not drawn (ticket 14).
  Code review (Standards + Spec) turned up three things, all taken: the
  Inert note in the settings window said "disabled" (a glossary-avoided
  word; now "the plugin is inert"); a capture with no modifier held onto an
  already-unbound chord stayed unbound (now falls back to the default's
  Alt); and an extended key shared its 8-bit scancode with its numpad twin
  (Home = Numpad 7), so the stored scancode is now the full set-1 one with
  the `E0` prefix (`0xE047`), which also lets `GetKeyNameTextW` name it.
- 2026-09-19 (human): in-game run passed — the cog, `/imebridge` and
  `/imebridge config` open settings and `/imebridge debug` the debug window;
  rebinding to `Ctrl+J` by pressing it works and `Alt+§` stops flipping;
  the three startup behaviours restore as saved; the Indicator hides and
  shows; `~/.xlcore/pluginConfigs/FfxivImeBridge.json` holds the values.
  Resolved.
