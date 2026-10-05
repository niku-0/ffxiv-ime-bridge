## Agent skills

### Issue tracker

Issues live as GitHub issues on the public repo; every body and comment is drafted locally and posted only after review. Pre-switch tickets are archived under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles use their default names as GitHub labels (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Releases

Releasing a version or writing its changelog: see `docs/agents/release.md`.

### Code review

`/code-review`'s Standards and Spec sub-agents are spawned as
`subagent_type: code-reviewer` (`.claude/agents/code-reviewer.md`:
Opus 5.5, medium effort).
