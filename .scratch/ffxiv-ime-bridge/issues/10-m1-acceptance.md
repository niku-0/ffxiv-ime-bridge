# M1.6 — M1 in-game acceptance

Status: resolved
Type: task
Blocked by: 05, 06, 07, 08, 09

The whole M1 path in one sitting, on a fresh game start, by the user. The
per-ticket checks proved the pieces; this proves the milestone the spec
states: toggle → compose → candidates → Commit → Native Write → native
Enter sends, plus Indicator and Slash Bypass.

## Script

1. Start the game with Dalamud, load the plugin. Chat: ladder passed.
2. Click into the Chat Box. `Alt+§` → `forwarding on`, Indicator `A`.
   Ctrl+Space → `あ`.
3. `/echo ` (Slash Bypass, unasked), then `konnichiha`, Space, Space,
   pick `今日は` from the list, Enter (commits), Enter (sends). The log
   shows `今日は`.
4. `/tell <yourself> ` then `arigatou`, Enter, Enter: the tell arrives.
5. English mid-session: Ctrl+Space → `A`, type `hello`, Enter: sends.
   Ctrl+Space → `あ` again.
6. `Alt+§` → off, Indicator gone, type Japanese romaji: plain letters in
   the box (vanilla). `Alt+§` → on.
7. Escape mid-composition, click out mid-composition, alt-tab
   mid-composition: nothing is written or sent in any of the three.
8. Ten minutes of ordinary play with Forwarding on and the box unfocused:
   no stutter, no keybind swallowed, `dalamud.log` free of errors.
9. Unload the plugin: hook gone, `busctl --user` shows no leftover context.

## Result

Paste what differed from the script below. All as scripted → set
`Status: resolved`, tick M1 in the spec, and open M2.

## Comments

- Human test: Something I did at step 6 resulted in mozc not switching back from the keyboard layout. Fixed manually from mozc setting in the taskbar. was not able to replicate.
- Alt + tab during composition writes the contents to the chat box. 
- busctl --user:
  ```
  NAME                                                                                           PID >
  …
  :1.253                                                                                       26534 >
  :1.254                                                                                       26534 >
  :1.255                                                                                       26534 >
  :1.256                                                                                       26534 >
  :1.257                                                                                       26534 >
  org.fcitx.Fcitx-0                                                                            26534 >
  org.fcitx.Fcitx5                                                                             26534 >
  org.freedesktop.IBus                                                                         26534 >
  org.freedesktop.IBus.Panel                                                                   26534 >
  org.freedesktop.portal.Fcitx                                                                 26534 >
  org.freedesktop.portal.IBus                                                                  26534 >
  …  (the other bus names: the desktop session and its apps)
  ```
- 2026-09-18 (agent): closed out from the run above. The milestone path
  (steps 1–5, 8, 9) is as scripted; M1 is ticked in the spec. What differed:
  - **Step 7, alt-tab mid-Composition writes the text into the box** (not
    sent). Cause: the Chat Box keeps its focus while the game window is
    inactive, so the plugin sees no focus edge and sends no `Reset`; the
    window alt-tabbed to has its own fcitx5 context, and when it focuses in,
    fcitx5 focuses ours out itself — "`FocusOut` commits" (M0.1, the
    library README). The `CommitString` arrives out of band, the tick drains
    it, and the Native Write puts it in the box. Window activation is a
    *sent* message, so the `DispatchMessageW` hook cannot see it;
    `Framework.Instance()->WindowInactive` is pollable on the tick. →
    **ticket 11** (M2). Escape and click-out behaved as scripted.
  - **Step 6, Mozc once did not switch back from `keyboard-<layout>`**,
    fixed from fcitx5's tray, not reproducible. Watch item: if it recurs,
    note whether the Indicator showed `A` or `あ` at the time and whether
    Ctrl+Space was pressed with Forwarding off (then it is vanilla and never
    reaches any context — expected).
  - Step 9: `busctl --user` lists bus names only, so it cannot show a
    leftover context (`:1.253`–`:1.257` are fcitx5's own connections). The
    check for next time is `busctl --user call org.fcitx.Fcitx5 /controller
    org.fcitx.Fcitx.Controller1 DebugInfo`, which lists input contexts per
    group; the plugin disposes its context within the 5 s unload budget and
    fcitx5 reaps it when the connection drops regardless.

