# M3.5 — Issue template and support posture

Status: ready-for-agent
Blocked by: 21
Type: task

Where bug reports land, and what they are allowed to ask for.

`AcceptsFeedback: false` in the manifest and `repo.json`: Dalamud's
in-client feedback routes through goatcorp's infrastructure, which is meant
for the official repository. Reports come to GitHub issues instead, where
they can be answered.

## The privacy constraint

Ticket 18 moved chat drafts out of `dalamud.log` at default level and
stopped the keyboard trace recording keys the gate never acted on. An issue
template that says "attach your `dalamud.log`" hands all of that care
straight back: the log is a file users upload without reading, and a `/tell`
draft in it is the user's private message.

## What exists today, and why it isn't enough (plan review 2026-09-23)

- `/imebridge probe` prints only the ladder's one-line summary to chat
  (`ProbeRunner.cs:57`). The per-rung steps — which rung failed, and with
  what — are shown in the debug window only, with no way to copy them.
- The debug window's **Copy report** button (`DebugWindow.cs:177`) is the
  last *Native Write* report, not the ladder. `WriteReport.ToString()`
  carries the committed text in quotes and the whole Chat Box contents
  before and after the write: exactly the `/tell` draft ticket 18 took out
  of the log. `WriteReport.Summary` is the text-free form.

So a report filled in as first planned couldn't diagnose a failed ladder,
and would carry the user's draft.

## Do

- **A copy button for the ladder.** In the debug window's transport ladder
  section, a **Copy ladder** button that copies the summary and every step
  (name, outcome, detail). Replace the user's home directory with `~` in
  the copied text: the environment step reports `WINEPREFIX`, whose path
  holds the user's login name. Keep the formatting in a pure function next
  to `ProbeReport` and test it, including the `~` substitution.
- **`.github/ISSUE_TEMPLATE/bug_report.md`** asking for, in order:
  - the ladder via **Copy ladder** — what actually matters for the common
    failure;
  - distribution, Wine/Proton build, desktop session, IME and layout;
  - what was typed and what appeared, in their words, not in a log.

  It does not ask for the Native Write **Copy report**. If a maintainer
  needs it for a write problem, they ask, and say it contains the chat text
  and should be pasted only after reading. Only if all of that is not
  enough does a maintainer ask for a log, and then for a specific line
  range at a stated level — never the whole file by default.
- A short `feature_request.md`.
- A line in the README's troubleshooting section pointing at the template
  and the **Copy ladder** button.
- Set `AcceptsFeedback: false` in both the manifest and `repo.json`.

## Acceptance

- The template never asks for `dalamud.log` or the Native Write report
  unprompted.
- A report filled in exactly as asked names the failing rung and its
  detail, which is enough to diagnose a failed transport ladder.
- The copied ladder contains no absolute home path; `dotnet test` covers
  the formatting.

## Comments
