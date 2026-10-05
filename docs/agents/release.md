# Releases

The `Changelog` in `src/FfxivImeBridge/FfxivImeBridge.json` is the release's only changelog. Dalamud's plugin installer shows it to players, and the release workflow uses it as the GitHub release notes. The release steps and what the workflow checks are in `docs/dev-plugin.md`, "Release".

## Steps

1. **Collect.** Run `git log --format='%h %s%n%b' $(git describe --tags --abbrev=0)..HEAD`, and run `gh issue view <n>` for every issue the commits close. Done when every commit is either covered by a changelog line or deliberately left out under the rules below.
2. **Write.** Replace the whole `Changelog` value. Each release gets a fresh list, because the workflow refuses a changelog identical to the previous release's. Done when `tools/check-changelog.sh` passes.
3. **Review.** Show the user the changelog and stop. The changelog becomes public when the tag is pushed, so tag nothing until they say go.
4. **Release.** Bump `<Version>`, commit, tag and push as `docs/dev-plugin.md` describes. Commits and tags use `TZ=UTC` (`docs/agents/issue-tracker.md`, "Commits").

## Writing the changelog

The reader is a player deciding whether to update.

- **Player-facing only.** Give one line to each change a player can see in the game, the chat or the installer. A refactor that changes behaviour gets a line describing the visible effect. Refactors, tests, comments, docs and tracker work get no line.
- **Describe the new behaviour** in the present tense: "Holding the Toggle Key flips Forwarding once, instead of on every key repeat."
- **Use the player's words.** Name things with the `CONTEXT.md` terms a player meets in the settings window and the plugin's chat messages (Indicator, Toggle Key, Forwarding, Candidate).
- **Plain text.** Write one `- ` bullet per line, joined with `\n` in the JSON string. The installer draws the text raw with ImGui's `TextWrapped` while GitHub renders Markdown, so `- ` bullets are the only markup that reads well in both. Leave out issue numbers, which mean nothing in the installer.
- **Order:** new things first, then fixes, then removals.
- **Public text.** Clean it the same way as an issue body (`docs/agents/issue-tracker.md`, "Pasting logs and output into a ticket").

`jq -r '.[0].Changelog' repo.json` prints the last released changelog, which is a good example of the style.
