# M2.7 — M2 in-game acceptance

Status: resolved
Type: task
Blocked by: 11, 12, 13, 14, 15, 16

The whole M2 path in one sitting, on a fresh game start, by the user, as
ticket 10 did for M1: the polish items the spec lists, together, on top of
the M1 script.

## Script

1. Fresh start, plugin loads, settings from the cog; Forwarding comes up per
   the startup setting; the flip line prints.
2. M1 steps 2–6 (ticket 10) once through, in AXIS, Indicator in the vanilla
   badge style.
3. Alt-tab mid-composition, back: box empty, nothing written (11).
4. Rebind the Toggle Key, use it, bind it back (12).
5. `/imebridge reconnect` mid-session; then kill fcitx5, restart it, click
   into the box (13).
6. HUD scale 80 %/120 %: preedit aligned, size follows (14, 15).
7. Mouse: select by click, page by wheel, right-click inert, preedit click
   inert (16).
8. Ten minutes of ordinary play with Forwarding on and the box unfocused;
   no stutter, `dalamud.log` free of errors.
9. Unload: hook gone; `busctl --user call org.fcitx.Fcitx5 /controller
   org.fcitx.Fcitx.Controller1 DebugInfo` shows no leftover context.

## Result

Paste what differed below. All as scripted → set `Status: resolved`, tick
M2 in the spec, and open M3.

## Comments

- 2026-09-21 (human): all steps pass as scripted. **Finding:** when a
  Candidate is written to the Chat Box, the cursor is drawn at the beginning
  of the written word; it is really at the end (typing continues there).
  The cursor should be drawn where it is, after the word.
- 2026-09-21 (agent): closed out from the run above; the M2 script holds.
  The finding is the drawn cursor after a Native Write — the index is right
  (ticket 09's readbacks), the cursor node is not redrawn from it. Cause and
  fix in **ticket 20**; M2 is ticked in the spec once 20 is confirmed
  in-game, and M3 planning follows.
- 2026-09-21 (agent): ticket 20 confirmed in-game the same day; M2 ticked
  in the spec. M3 (public release) is next to plan.
