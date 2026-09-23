# M3.4 — README

Status: resolved
Blocked by: 22, 23
Type: task

The repository's front page, and the only documentation most users will
read. Blocked on 22 and 23 because it quotes the install URL and the
plugin's own strings; it is written once those are final, so it cannot
drift from them.

## Contents

- **What it does and why.** Wine's translation of X11 composition into
  `WM_IME_*` — the Native Path — is broken for a game that draws its own
  chat box, so Japanese input produces mojibake. This plugin talks to
  fcitx5 over D-Bus and draws the composition itself.
- **Requirements.** Linux, fcitx5 on the session bus, XIVLauncher.Core with
  Dalamud at API 15. Windows: the plugin loads and does nothing.
- **No environment setup.** Say it plainly, and say why: the plugin bypasses
  the Native Path entirely, so `XMODIFIERS` and friends need no changes.
  Ticket 05 found fcitx5's own XIM/Wayland frontends never fought our
  Input Context. (The old M3 line said "incl. Native Path env setup";
  grilled 2026-09-22 and settled as *no setup*, which is what was actually
  measured.)
- **Install.** Add the `repo.json` raw URL as a custom repository in
  Dalamud, install "FFXIV IME Bridge".
- **Use.** The default Toggle Key is `Alt` + scancode `0x29` (`Alt+§` on a
  Nordic layout, ``Alt+` `` on US) and is matched by scancode, so it names
  a physical key on any layout; how to rebind from the settings window;
  Slash Bypass; what the Indicator's `あ`, `A` and `!` mean.
- **Troubleshooting.** `/imebridge probe`, `/imebridge reconnect`, the
  Degraded `!` state and what it means, where the debug window is.
- **Tested on.** wine-xiv-staging 10.8, KDE/Wayland, fcitx5 with Mozc. Say
  that other Wine and Proton builds are unverified and reports are welcome
  — the transport ladder exists precisely because rung 1 may not hold
  everywhere.
- **AI assistance.** One honest line: developed with AI assistance, and
  every behavioural claim in this repo was verified in-game. The tickets
  under `.scratch/` are that record. It costs nothing and pre-empts the
  accusation.
- **Licence.** MIT, `niku-0`.

## Acceptance

Someone who has never seen the repo can install the plugin and type a
Japanese sentence using only this file. Ticket 27 tests exactly that.

## Comments

**2026-09-23 — written.**

- **Mozc is a requirement, not just fcitx5.** The load-time ladder includes
  the `input-method` rung, which switches its test context to `mozc`; without
  Mozc in the group the plugin is Inert (`Ladder stopped at: input-method`).
  The README says so under Requirements and uses that line as the
  troubleshooting example.
- **First run needs fcitx5's own switch key.** The plugin's Input Context
  starts on the keyboard layout (`A`), so Use tells the reader to press
  Ctrl+Space (or their configured key) in the chat box to get `あ`.
- **Reporting a problem** asks for what `/imebridge probe` printed. That is
  only the one-line summary; ticket 25 replaces the line with the template
  and the **Copy ladder** button.
- **Unverified from the repo:** the Dalamud UI labels in Install
  (*Custom Plugin Repositories*, **Enabled**, **Save and Close**) are from
  memory of Dalamud's settings window. Ticket 27, step 4, is where they are
  checked; the `repo.json` URL answers only after ticket 23's first release.
- No screenshots until ticket 26.
