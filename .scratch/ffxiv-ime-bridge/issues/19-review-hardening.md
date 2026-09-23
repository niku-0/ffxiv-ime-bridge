# Review — low-priority hardening

Status: resolved
Type: task

The rest of the security/safety review of 2026-09-21 (the two privacy
leaks and the auto-start are ticket 18). None of these endangers the user;
each removes a dependency on behaviour outside the plugin or a rough edge.
Independent of one another; do them in the order listed.

## 1. Sanitise a Commit before the Native Write

`NativeWriter.Run` hands fcitx5's commit string straight to the game
(`src/FfxivImeBridge/NativeWrite/NativeWriter.cs:142-143`). fcitx5 can commit
any string: C0 controls, a newline, `U+0002` (the SeString payload start
byte). FFXIVClientStructs documents `InsertText`/`SetText` as applying the
game's input sanitisation, so this is belt and braces — but the splice
already refuses to touch a buffer holding `0x02` (`ChatBoxSplice.Plan`), and
it should not *introduce* one either.

Do: in `ChatBoxSplice.Plan` (pure, host-tested), drop code points below
`U+0020` and `U+007F` from the committed text before counting and splicing;
report the count dropped on the `SplicePlan` so the write report can say
`(n control characters dropped)`. Test: a commit of `"ab\nc"` splices
`"abc"`, the plan says 2 dropped; a plain commit is unchanged.

## 2. One unload budget instead of two

`SessionLifecycle.Dispose` (`src/FfxivImeBridge/Session/SessionLifecycle.cs:178`)
and `ProbeRunner.Dispose` (`src/FfxivImeBridge/ProbeRunner.cs:93`) each wait
up to 5 s synchronously on the framework thread, so an unload or reload with
fcitx5 hung can freeze the game for ~10 s. Both run inside `Plugin.Dispose`.

Do: one `Stopwatch` started at the top of `Plugin.Dispose`, the remaining
budget passed to both (`SessionLifecycle.Dispose(TimeSpan remaining)`,
`ProbeRunner.Dispose(TimeSpan remaining)`), 5 s in total. The
`SessionLifecycleTests` unload case gets a variant where the teardown hangs
and the budget is honoured.

## 3. No message hook on Windows

`KeyboardCapture`'s constructor hooks `DispatchMessageW` unconditionally
(`src/FfxivImeBridge/Capture/KeyboardCapture.cs:54`); on Windows proper the
manifest promises "loads and does nothing", yet the Toggle Key chord is
swallowed in the Chat Box with a "not reachable" toast, focus is read on
every keystroke, and the ladder is re-run on every Chat Box focus gain
(10 s apart) only to fail at the `environment` rung.

Do: in `Plugin`, when `!Util.IsWine()`, construct neither `KeyboardCapture`
nor start the `Bridge`; the settings window already shows the Windows note.
The command handler still opens the windows. Keep the ladder available
from `/imebridge probe` so a Windows user can see *why* (its first rung
says so).

## 4. Debug-window text is optional; say so

Not a change to code — a note for the release README (M3): the debug window's
copy buttons put Chat Box text on the clipboard by design; the transport
ladder's Information lines carry `DBUS_SESSION_BUS_ADDRESS`, `XDG_RUNTIME_DIR`
and `WINEPREFIX`, i.e. the local username, as Dalamud's own plugin-path lines
already do. Mention both under a "What ends up in `dalamud.log`" heading so a
user pasting a log knows.

## Done when

- 1–3 implemented with the tests named; 4 is a line in the M3 README ticket
  (add it there when that ticket exists).
- Host tests green; in-game: a normal commit unchanged, unload with fcitx5
  stopped (`pkill -STOP fcitx5`) returns within ~5 s.

Safety: item 1 only removes bytes from a Commit, never adds or sends
(ADR-0001); 2 and 3 change teardown and load, nothing about keys or text.

## Comments

- 2026-09-22 (agent): 1–3 implemented; `dotnet test` green (332). Item 4 is
  carried forward — the M3 README ticket does not exist yet — with its line
  drafted at the bottom of this comment, ready to paste when it does.

  1. **Sanitised Commit.** `ChatBoxSplice.Plan` drops code points below
     `U+0020` and `U+007F` before it counts or splices anything, and the plan
     carries both the text as it goes in (`SplicePlan.Committed`) and the
     count (`ControlsDropped`). The sanitised text is what is written, not
     just what is counted: `NativeWriter.Run` handed `InsertText` its own
     `text` argument, so filtering inside the plan alone would have counted
     one string and written another. `WriteReport.Committed` is that text,
     and `Where` and the next-frame `Verdict` now measure against it.
     The Information line says
     `text 15 bytes / 5 code points (2 control characters dropped)`; the
     `plan:` line of the full report gained `controlsDropped=`. The plural
     helper is `Overflow.Count`, which already existed for the refusal.
     `ChatBoxSpliceTests` covers a commit with one control and with three
     (`U+0002` among them), a `"\r\n"` commit that leaves nothing, a plain
     commit with a surrogate pair kept whole, and a commit that only fits
     because its controls were dropped first; `WriteReportTests` covers the
     summary's wording in singular and plural.

     Correction to the ticket: `"ab\nc"` holds one control character, not two,
     so its plan says 1 dropped. The theory covers a two-control case as well
     so the plural is exercised.

  2. **One unload budget.** `Plugin.Dispose` starts one `Stopwatch` at the
     top and passes what is left of `SessionLifecycle.Grace` (5 s) to
     `Bridge.Dispose(TimeSpan)` → `SessionLifecycle.Dispose(TimeSpan)` and
     then to `ProbeRunner.Dispose(TimeSpan)`. `ProbeRunner`'s own 5 s
     `UnloadGrace` is gone; the parameterless `Dispose()` on all three
     remains, spending the whole budget, for `IDisposable` and the tests.
     `SessionLifecycle.Grace` is now the one constant and is documented as
     the whole unload's, borrowed by a Reconnect's teardown as its bound.
     `SessionLifecycleTests` gained the hanging-teardown variant:
     `FakeInputContext.BlockDisposal` holds `DisposeAsync` open, the unload
     is given 200 ms, and it returns inside it with the context still
     undisposed and the "unload budget spent" line logged.

  3. **No message hook on Windows.** `Plugin` constructs `KeyboardCapture`
     and calls `bridge.Start()` only under `Util.IsWine()`. `capture` is
     therefore nullable, which replaced `SettingsWindow`'s separate
     `windowsNotWine` flag — the two said the same thing — and the debug
     window's Keyboard tab shows the same Windows line plus a pointer to the
     Transport tab. Everything else is untouched: the commands still open
     both windows, and `/imebridge probe` still climbs the ladder.

  `docs/dev-plugin.md` updated in three places: the Native Write section on
  the sanitised Commit, the reload loop on the single 5 s budget, and the
  settings section's Windows bullet.

  Left for the human (this ticket's own in-game checks, which is why it is
  `ready-for-human`): a normal Japanese commit goes in unchanged, and an
  unload with `pkill -STOP fcitx5` returns within ~5 s rather than ~10.

  For the M3 README ticket, under a "What ends up in `dalamud.log`" heading:

  > The debug window's **Copy readout**, **Copy report** and **Copy trace**
  > buttons put Chat Box text — including whatever you are drafting — on the
  > clipboard, by design: they exist for pasting into a bug report. The
  > transport ladder's Information lines carry `DBUS_SESSION_BUS_ADDRESS`,
  > `XDG_RUNTIME_DIR` and `WINEPREFIX`, which contain your local username, as
  > Dalamud's own plugin-path lines already do. Read a log through before
  > uploading it anywhere.

- 2026-09-22 (human): in-game confirmation passed — a normal commit goes into
  the Chat Box unchanged, and an unload with `pkill -STOP fcitx5` returns
  within ~5 s. Items 1–3 resolved; item 4 waits for the M3 README ticket,
  with its line drafted above.
