# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues on a **public** repo. Use the `gh` CLI for all operations.

## Draft, then post

Anything posted is public at once, and GitHub keeps edit history, so a later fix does not unpublish it. Every piece of free text bound for GitHub (issue body, comment, closing comment, edited body, wayfinder map update) goes through a draft:

1. Write it to `.scratch/drafts/<slug>.md` (git-ignored). A skill producing several issues writes all its drafts first.
2. Stop and point the user at the draft(s). Post nothing until they say go.
3. Post with `--body-file .scratch/drafts/<slug>.md`, never `--body "..."`.
4. Delete the draft once posted.

Titles are public too: put each one at the top of its draft so it gets the same read.

Commands carrying no free text (view, list, label add/remove, assign) need no draft. The `gh` write commands are also `ask` rules in `.claude/settings.json` as a backstop.

## Pasting logs and output into a ticket

Sanitise at the source, as the text goes into the draft — there is no later clean-up pass:

- Drop the timestamps from log lines unless the ticket's reasoning needs the timing; line order and measured waits stay.
- A home directory becomes `/home/<user>/` (`Z:\home\<user>\` in Wine spellings); a checkout path becomes a generic one.
- Keep only the lines the ticket uses and mark the cut with `…` — a full `busctl --user` listing, for one, names every program on the desktop.
- No names, e-mail addresses or account details; chat text only when the ticket is about it.
- What has to go is replaced by a `<placeholder>`, never by a made-up value: an invented one contradicts the rest of the evidence.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body-file <draft>`
- **Read an issue**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body-file <draft>`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number>`, or with a closing comment posted from a draft first

Infer the repo from `git remote -v`; `gh` does this automatically when run inside a clone.

## Commits

Commits and tags are made with `TZ=UTC`.

## Local archive

`.scratch/ffxiv-ime-bridge/` holds the spec and tickets 01–28 from before the switch to GitHub. They are read-only history: cite them by path, don't add new tickets there.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number> --comments` and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments` then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE` (drop `OWNER`/`MEMBER`/`COLLABORATOR`).
- **Comment / label / close**: `gh pr comment --body-file <draft>`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either: resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Draft it, get the go-ahead, then create a GitHub issue (see "Draft, then post").

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets. Every body and comment below goes through a draft.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map --body-file <draft>`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (`gh api` on the sub-issues endpoint). Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies**, the canonical, UI-visible representation. Add an edge with `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker's numeric **database id** (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`, _not_ the `#number` or `node_id`). GitHub reports `issue_dependencies_summary.blocked_by` (open blockers only, the live gate). Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list), drop any with an open blocker (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`, the session's first write.
- **Resolve**: `gh issue comment <n> --body-file <draft>` with the answer, then `gh issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far with `gh issue edit <map> --body-file <draft>`.
