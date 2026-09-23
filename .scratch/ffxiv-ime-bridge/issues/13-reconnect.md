# M2.3 — Reconnect: out of Inert and out of a dead bus connection

Status: resolved
Type: task
Blocked by: 12

Today a ladder failure on load leaves the plugin Inert until reload, and a
bus connection that dies mid-session (dbus-broker restart, socket error) is
never noticed — fcitx5 *leaving* the bus is handled (Degraded, context
recreated), the socket under it going away is not. Both get one way out:
Reconnect (`CONTEXT.md`), by hand or automatically (grilling 2026-09-18,
Q2, Q11).
Spec: "Resilience and errors", "Codebase shape" (`reconnect` command).

## Done when

- **Library:** `FcitxConnection` exposes the connection's death
  (`Tmds.DBus.Protocol`'s disconnect notification — verify the exact API,
  `Connection.DisconnectedAsync()` or equivalent) as an event raised on the
  reader thread, like `FcitxAvailabilityChanged`. Host-test by closing the
  connection from the test.
- `DegradedCause.ConnectionLost`: entered when the connection dies; the
  Indicator shows `!`, the gate passes everything, one chat line
  (`IME Bridge: lost the connection to fcitx5, forwarding degraded`), the
  Forwarding choice kept. Unlike `BusLost` nothing is recreated on the old
  connection: only a Reconnect lifts it.
- `Bridge.Reconnect()`: tear down the session and connection (the disposal
  path, bounded like unload), then `Start()` again — the same ladder and
  `OpenAsync` as load. Forwarding is re-applied from config as ticket 12
  does on open. Serialised: a Reconnect while one is running is ignored.
  A composition in flight is simply gone (the snapshot clears with the
  session).
- Automatic: on a Chat Box focus gain while Inert or `ConnectionLost`, a
  Reconnect starts if none ran in the last 10 s. Automatic attempts are
  **silent** — the ladder's per-run chat line and the "not reachable,
  disabled" line are suppressed for them (log only). Success prints
  `IME Bridge: fcitx5 reachable, forwarding on|off`.
- Manual: `/imebridge reconnect` and a **Reconnect** button in the settings
  window run it regardless of the 10 s and report both outcomes in chat
  (the existing ladder line on failure).
- `/imebridge probe` stays as the diagnostic (throwaway connection, debug
  window); it does not open a session.
- `docs/dev-plugin.md`: Reconnect in the Forwarding section, the Inert/
  Degraded/Reconnect trio spelled out.
- Host tests for the state machine: connection death → `ConnectionLost`,
  focus gain → one attempt, a second focus gain inside 10 s → none, after
  → one; Inert → the same; a manual Reconnect inside 10 s → runs; success
  restores Forwarding from config.

## In-game confirmation (human)

1. Start the game with fcitx5 stopped (`fcitx5 -r` after, or kill it before
   launch): load line says not reachable. Start fcitx5. Click into the
   Chat Box: within a tick chat says `fcitx5 reachable, forwarding …`;
   composing works.
2. With the session live, `/imebridge reconnect`: the ladder line, then
   `reachable`; Forwarding as before; composing works.
3. Kill fcitx5 (`pkill fcitx5`): Degraded as before (`!`, bus-lost line).
   Restart it and click into the box: context recreated (unchanged from
   M1). This is `BusLost`, not Reconnect — confirm no ladder line appears.
4. If you can restart the session bus safely (`systemctl --user restart
   dbus-broker` may log you out — skip if unsure): `!` and the lost-
   connection line; click into the box after it is back: `reachable`.
   Otherwise note "step 4 skipped".
5. Set `Status: resolved`.

Safety: a Reconnect performs exactly the load-time steps; no game memory
is touched; nothing is sent (ADR-0001).

## Comments
- 2026-09-19 (agent): implemented; awaiting the in-game confirmation above.
  Library: `FcitxConnection.Disconnected` (`Action<Exception>`), from
  Tmds' `DBusConnection.DisconnectedAsync()` (verified: returns the reason,
  null for our own dispose, unsupported only on auto-connect connections,
  which we do not use); never raised for our own `Dispose`. Host-tested in
  `DisconnectTests` by putting a relay unix socket of the test's own between
  the connection and the real bus and pulling it away — the bus is never
  restarted. Session: `IInputContextFactory.ConnectionLost` →
  `DegradedCause.ConnectionLost` (worst cause; upgrades silently over
  BusLost/Timeouts, `!`, gate off, choice kept, the ticket's chat line);
  focus edges tell a dead connection nothing, availability changes on it
  mean nothing, and disposing skips the context. `OpenAsync` takes the
  initial Forwarding (no chat line: not a flip). The state machine is the
  new Dalamud-free `Session/SessionLifecycle.cs` behind an `ISessionSource`
  seam — `Bridge` is now the Dalamud adapter around it, and
  `LadderSessionSource` (in `Bridge.cs`) is the load-time steps:
  `ProbeRunner.RunAsync(silent)` (joins a run in flight; chat line
  suppressed when silent), connect, watch, open. Reconnect = tear down
  (bounded, failures logged) then climb again with the startup behaviour
  re-applied; serialised (a request while one runs returns false → toast);
  automatic on a Chat Box focus gain while Inert or ConnectionLost if no
  attempt started in the last 10 s (load and manual count); success prints
  `fcitx5 reachable, forwarding on|off` on every open, load included (this
  replaces the `forwarding on` line ticket 12's open printed through the
  setter). `/imebridge reconnect`, a **Reconnect** button in the settings
  window (disabled while one runs) and a note while the connection is dead.
  Unload order is now Bridge before ladder, so a climb a manual Reconnect
  is waiting on is cancelled quietly. Host tests: `SessionLifecycleTests`
  (failed load → Inert; focus gain → one silent attempt, inside 10 s none,
  after → one; success restores Forwarding from config; a live session's
  focus gain → nothing; connection death → reconnect with the old transport
  disposed and the dead context left alone; manual inside 10 s → runs,
  tears the live session down, reports both ways; serialisation; startup
  re-applied; dispose), `ForwardingSessionTests` (ConnectionLost),
  `DisconnectTests`; 249 in the plugin suite, 19 in the library's.
  `docs/dev-plugin.md`: the Degraded/Inert/Reconnect trio and the button.
- 2026-09-19 (human): in-game run passed — `/imebridge reconnect` with a
  live session gives the ladder line then `reachable` with Forwarding as
  before and composing works (step 2); killing fcitx5 is `BusLost` as in M1,
  the context recreated on the next focus gain with no ladder line
  (step 3). Step 1 (load with fcitx5 stopped → `not reachable`; start
  fcitx5, click into the Chat Box → `fcitx5 reachable, forwarding …`,
  composing works) passed on a second attempt — the first had not set the
  Inert state up. Step 4 (session bus restart) skipped. Resolved.
