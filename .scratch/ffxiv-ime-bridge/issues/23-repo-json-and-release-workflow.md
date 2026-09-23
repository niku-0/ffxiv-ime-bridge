# M3.3 — repo.json and the release workflow

Status: ready-for-agent
Blocked by: 21, 22
Type: task

Distribution is the project's own third-party repository, as the spec has
always said: a `repo.json` at the repo root, served over its
`raw.githubusercontent.com` URL, which users paste into Dalamud's custom
repository box.

## repo.json

A plain JSON array of one plugin object. `Name`, `Author`, `Description`
and `Punchline` are mandatory; `AssemblyVersion`, `InternalName` and
`DalamudApiLevel` come from the build and must match the manifest inside
the zip, because API 15 stopped overwriting the in-zip manifest at install
time. Also set `RepoUrl`, `ApplicableVersion: "any"`, `Tags`,
`AcceptsFeedback: false` (ticket 25: reports go to GitHub issues, not
through goatcorp's feedback path), and `DownloadLinkInstall`/`Update`
pinned to the tag's release asset.

No `IconUrl`, no `ImageUrls` until ticket 26. No testing track: it needs a
Dalamud Experimental opt-in and there is no userbase to stage for yet.

**Pinned links, not `releases/latest/download/`.** The argument for
`latest` is that `repo.json` then never needs editing — but it does:
`AssemblyVersion` must be bumped every release or Dalamud never offers the
update, silently, while the zip itself is fine. Since the file is edited
anyway, a pinned URL costs one line in the same edit and makes every
release's artifact unambiguous forever.

## Workflow

`.github/workflows/release.yml`, on tag push `v*`, with
`permissions: contents: write` (it creates the release and pushes to
`main`):

1. **Tag matches the csproj.** Fail unless `${GITHUB_REF_NAME#v}` equals
   the plugin csproj's `<Version>`. `repo.json` gets its version from the
   tag and the in-zip manifest gets it from the csproj; API 15 no longer
   reconciles them at install time, so a mismatch would ship silently.
2. `Blooym/setup-dalamud@v1` with `branch: release` — it installs Dalamud
   and sets `DALAMUD_HOME`, which the build and the tests' `DalamudLibPath`
   (ticket 22) honour.
3. `dotnet test`. Both suites run; `FcitxFactAttribute` self-skips when
   `DBUS_SESSION_BUS_ADDRESS` is unset, so a runner with no fcitx5 is green
   rather than red.
4. `dotnet build -c Release`, attach the zip to the GitHub release. Write
   the Dalamud version it was built against (e.g. `Dalamud.dll`'s file
   version under `DALAMUD_HOME`) into the release notes.
5. Write `AssemblyVersion` and the pinned `DownloadLink*` into `repo.json`
   from the tag, and commit that back to `main`. One job writes both, so
   they cannot drift. The Actions bot commits in UTC.

This is not speculative machinery. A hand-built zip is built against
whatever Dalamud happened to be sitting in the local xlcore directory that
day, and nobody — including the author — can say afterwards what that was.
`branch: release` alone doesn't fix that, since it is also "whatever was
current that day"; step 4's release note is what makes it answerable.

Releasing is then: bump `<Version>`, commit, tag `v<Version>`, push both.

## Acceptance

- A `v0.1.0` tag produces a release with the zip attached and the Dalamud
  version in its notes.
- A tag that disagrees with `<Version>` fails the job before anything is
  built.
- `repo.json` on `main` afterwards states `0.1.0.0` and a link to that
  release's asset, matching the manifest inside the zip.
- The repository is still private here, so check the file and the asset
  with `gh` (authenticated). The anonymous fetch through the raw URL — the
  one Dalamud makes — is ticket 27's, after the flip to public. (GitHub's
  CDN may serve a stale copy briefly after a push — community knowledge,
  not confirmed by goatcorp, so allow for it before concluding something is
  broken.)

## Comments
