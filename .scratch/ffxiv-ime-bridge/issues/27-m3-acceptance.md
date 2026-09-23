# M3.7 — M3 acceptance: a stranger can install this

Status: resolved
Blocked by: 21, 22, 23, 24, 25
Type: task

As tickets 10 and 17 did for M1 and M2, in one sitting, by the user. The
question is no longer "does it compose" — M2 settled that — but "can
someone who is not the author install this and use it".

## Script

1. **Pre-publication check.** `git log` shows one author, the handle.
   Nothing personal in the tree; `ref/` absent.
2. **Flip the GitHub repository to public.** It has to happen here: Dalamud
   cannot fetch `repo.json` or a release asset from a private repository.
   Then `curl` the raw `repo.json` URL and the asset link in it, logged out,
   and get the file and the zip.
3. **Remove the dev plugin** from Dalamud entirely — dev-plugin path gone,
   game restarted, `/xlplugins` shows nothing from this project.
4. **Install as a user would**: add the `repo.json` raw URL as a custom
   repository, find "FFXIV IME Bridge" in the installer, install it. No
   dev-mode anything.
5. **The version shown in the installer matches the tag** (`0.1.0`).
6. **Use it, from the README alone**: toggle, compose, pick a candidate,
   commit, press Enter, the message sends. Following the README's own
   words, not memory — anything that reads wrong is a README bug, and this
   is the only chance to catch it with fresh eyes.
7. **Settings open from the cog**; the Toggle Key rebinds.
8. **Uninstall from the installer.** The hook is gone, the game is
   unaffected, no leftover Input Context (`busctl --user call
   org.fcitx.Fcitx5 /controller org.fcitx.Fcitx.Controller1 DebugInfo`),
   and the config file is the only thing left behind.

Explicitly **not** in scope: a second machine, a fresh wineprefix, another
distribution. There is one machine and one Wine build; a synthetic prefix
would test a setup nobody runs. The honest form of that coverage gap is the
`0.1.0` version and the README's "tested on" line, and the first real
report will say more than a manufactured environment could.

## Result

Paste what differed below. All as scripted → set `Status: resolved` and
tick M3 in the spec. The repository is already public from step 2; a
failure after that is fixed in the open, like any other bug.

## Comments

- 2026-09-23 (human): all steps pass. **Step 6 finding:** the README's
  Install steps did not match the installer: the menus are reachable as
  Dalamud Settings / Dalamud Plugins, not only by command, the URL is added
  with the **+** button, and the button at the bottom is **Save**, not
  *Save and Close*. README corrected.
- 2026-09-23 (agent): closed out from the run above; the README fix is
  wording only, so M3 is ticked in the spec. Checked after the flip: the
  repository reports `PUBLIC`, and the raw `repo.json` URL and the
  `v0.1.0` asset link both return 200 to an anonymous `curl`.
