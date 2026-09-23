# M0.2 — Reach the session bus from inside Wine (dev plugin)

Status: resolved
Type: prototype
Blocked by: 01

Minimal Dalamud dev plugin referencing `FfxivImeBridge.Fcitx`. On load it
tries the transport ladder and logs the result to Dalamud's log and to a
`/imebridge debug` window.

## Done when

- Step 1: `Tmds.DBus.Protocol` connects over AF_UNIX to
  `Z:\run\user\1000\bus` (translate `DBUS_SESSION_BUS_ADDRESS`; note whether
  the env var is even visible inside the Wine process — XIVLauncher may
  scrub it). Result recorded: works / fails, with the exact exception.
- If step 1 fails: record precisely where (socket creation, connect, or
  EXTERNAL auth) before touching the fallback.
- On success: `CreateInputContext` → `ProcessKeyEvent('k')` →
  `UpdateClientSideUI` received inside the game. Log the preedit text.
- Dalamud dev-plugin load path documented (`installedPlugins`/dev plugin
  location, `DalamudPackager` output, reload loop).

## Outcome to record

Which rung of the ladder works. If it's the helper process, open a new
ticket for it and add an ADR — that decision meets all three ADR criteria.

## Answer

**Rung 1 works, in the game.** Confirmed 2026-09-18 inside `ffxiv_dx11.exe`
under Dalamud (log in Comments, every rung `Passed`, preedit `ｋ`). The
game-prefix run matches the scratch-prefix run exactly except for the
reported Windows version (6.1.7601 vs 10.0.19045 — prefix setting, no
effect). First verified 2026-09-17 outside the game, under the same
wine-xiv-staging 10.8 build and the Windows .NET 10 runtime Dalamud ships,
in a scratch prefix (`docs/dev-plugin.md`, "Without the game"):

```
[Passed ] platform: Microsoft Windows 10.0.19045; .NET 10.0.0; Wine, version export hidden (WINEPREFIX=…/scratchpad/wineprefix)
[Passed ] environment: DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus; XDG_RUNTIME_DIR=/run/user/1000
[Passed ] address: path=/run/user/1000/bus wine-path=Z:\run\user\1000\bus uid=1000
[Passed ] socket: AF_UNIX stream socket created (handle 404)
[Passed ] connect: connected to /run/user/1000/bus
[Passed ] authenticate: AUTH EXTERNAL as uid 1000 accepted
[Passed ] fcitx5: InputMethod1.Version = 1
[Passed ] input-context: /org/freedesktop/portal/inputcontext/125
[Passed ] focus: focused; input method keyboard-<layout>
[Passed ] input-method: keyboard-<layout> → mozc
[Passed ] key: ProcessKeyEvent(k) handled=True
[Passed ] preedit: ｋ
[Passed ] client-side-ui: UpdateClientSideUI received 2×
Session bus reachable directly; fcitx5 composes.
```

Per done-when item:

- Env var: `DBUS_SESSION_BUS_ADDRESS` is in `ffxiv_dx11.exe`'s environment
  (`/proc/<pid>/environ` of the live game) and visible to the Windows
  process under Wine. XIVLauncher.Core does not scrub it.
- Socket creation, connect and EXTERNAL auth all succeed; both path
  spellings connect, so no translation is needed. The one thing that
  *would* have failed is auth: Tmds sends the Windows SID by default, which
  the bus rejects; `FcitxConnectionOptions.ExternalUserId` supplies the
  Linux uid (test `The_bus_rejects_an_external_auth_uid_that_is_not_the_peer_uid`).
- `CreateInputContext` → `ProcessKeyEvent('k')` → preedit `ｋ` received
  (via `UpdateFormattedPreedit`, as M0.1 found) and two `UpdateClientSideUI`
  signals counted, inside Wine. The rungs from `input-method` on are about
  fcitx5's setup, not transport: a missing Mozc fails there with a message
  saying the transport rungs passed.
- Dev-plugin load path: `docs/dev-plugin.md`.

Helper process (rung 2): **not needed**; no new ticket, no ADR.

Code: `FfxivImeBridge.Fcitx.Diagnostics.TransportProbe` (+ `SessionBusAddress`,
`FcitxConnectionOptions`), `tools/FfxivImeBridge.TransportProbe`, and the
dev plugin `src/FfxivImeBridge` (`/imebridge debug`, `/imebridge probe`).

### In-game confirmation (done 2026-09-18)

Dalamud was broken by the 2026-09-17 game patch; once it worked again:

1. Launch with Dalamud, load the dev plugin as in `docs/dev-plugin.md`.
2. Watch chat for `IME Bridge: Session bus reachable directly; fcitx5 composes.`
   or open `/imebridge debug`.
3. Paste the `Transport ladder:` lines from `~/.xlcore/logs/dalamud.log`
   into the Comments below and set `Status: resolved` (there is no
   `map.md` for this effort, so nothing else to update). No rung differed
   between the game prefix and the scratch prefix.

Safety notes for that run: the plugin only opens a Unix socket to the
session bus, talks to fcitx5, and draws an ImGui window; it does not hook
game functions, read game memory or send chat (`IChatGui.Print` is a local
client-side line, ADR-0001 stands).

## Comments

- 2026-09-17: `wine_get_version` is hidden in the xiv build, so the probe
  reports Wine from `WINEPREFIX`/`WINELOADERNOEXEC`. Wine's AF_UNIX support
  is only verified on this build; plain upstream Wine/Proton builds are not,
  which is why the plugin keeps failing closed with the ladder in the log.
- 2026-09-17 review: "transport ladder", "rung" and "probe" are used in code
  and docs but not defined in `CONTEXT.md`; candidate for `/domain-modeling`.
- 2026-09-18 HUMAN STEP RESULTS:
  [INF] [FfxivImeBridge] Transport ladder: starting
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] platform: Microsoft Windows 6.1.7601 Service Pack 1; .NET 10.0.0; Wine, version export hidden (WINEPREFIX=/home/<user>/.xlcore/wineprefix)
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] environment: DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus; XDG_RUNTIME_DIR=/run/user/1000
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] address: path=/run/user/1000/bus wine-path=Z:\run\user\1000\bus uid=1000
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] socket: AF_UNIX stream socket created (handle 1508)
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] connect: connected to /run/user/1000/bus
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] authenticate: AUTH EXTERNAL as uid 1000 accepted
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] fcitx5: InputMethod1.Version = 1
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] input-context: /org/freedesktop/portal/inputcontext/135
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] focus: focused; input method keyboard-<layout> → mozc
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] key: ProcessKeyEvent(k) handled=True
  [INF] [FfxivImeBridge] Transport ladder: [Passed ] preedit: ｋ
  [INF] [FfxivImeBridge] Transport ladder: Session bus reachable directly; fcitx5 composes.
- 2026-09-18 human note: Dalamud found 1 validadion issue, The plugin does not register a config UI callback. IF you have a settings window or  section, please consider registering UiBuilder.OpenConfigUi to open it.
- 2026-09-18: closed out. Validation issue addressed: `UiBuilder.OpenConfigUi`
  now opens the same debug window as `OpenMainUi` (there are no settings;
  the cog button just gets the ladder). Unblocks 03 and 04.
