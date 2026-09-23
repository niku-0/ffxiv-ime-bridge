# M3.2 — Move to Dalamud.NET.Sdk

Status: resolved
Blocked by: 21
Type: task

`src/FfxivImeBridge/FfxivImeBridge.csproj` builds on `Microsoft.NET.Sdk`
with `<PackageReference Include="DalamudPackager" Version="15.0.0" />`.
goatcorp documents that reference as **deprecated and set to be removed**;
the current path is `<Project Sdk="Dalamud.NET.Sdk/15.0.0">`, which bundles
a matching packager and resolves the Dalamud reference assemblies itself.
The SamplePlugin on SDK 15 carries no `DalamudLibPath` and no `HintPath`
references at all.

**Do not start until the user says the M2 plugin text is final.** The
migration touches `Punchline` and `Description`, and they are exactly the
strings being hand-edited. Touch them once, already final.

## Risk, and the gate

`Directory.Build.props` points `DalamudLibPath` at
`$(HOME)/.xlcore/dalamud/Hooks/dev/` because the build is on Linux against
XIVLauncher.Core. Whether the SDK honours `DALAMUD_HOME` on such a box is
**not confirmed** by goatcorp's own sources — the SDK README and the
migration guide are both silent, and only a community summary says it does.

So: try it, and let the build answer. If `dotnet build -c Release` resolves
the references, keep the migration. If it does not, and setting
`DALAMUD_HOME` to the xlcore dev directory does not rescue it, revert the
csproj wholesale and reopen this ticket with what the build said. A
five-minute experiment with an unambiguous answer, not a leap.

## Do

- `<Project Sdk="Dalamud.NET.Sdk/15.0.0">`; drop the `DalamudPackager`
  `PackageReference` and the five `<Reference>`/`<HintPath>` items in the
  plugin csproj.
- **Keep a `DalamudLibPath` the tests can use.**
  `tests/FfxivImeBridge.Tests/FfxivImeBridge.Tests.csproj` is a plain
  `Microsoft.NET.Sdk` project and copies `Dalamud.dll` and
  `Newtonsoft.Json.dll` from `$(DalamudLibPath)`. Move the block out of the
  root `Directory.Build.props` into `tests/Directory.Build.props` (or the
  test csproj), `DALAMUD_HOME` first so CI resolves it the same way — don't
  just delete it.
- **Check where the manifest fields live before moving any.** It is not
  confirmed that SDK 15 wants `Punchline`, `Tags` and friends as MSBuild
  properties; as remembered, the SDK 15 SamplePlugin still keeps them in its
  `.json` next to the csproj. Read the SamplePlugin's csproj and json at the
  SDK 15 tag and follow it: fields it keeps in the json stay in
  `FfxivImeBridge.json`. Either way the manifest must end up with `Author`
  `niku-0`, `Name`, `Punchline`, `Description`, `Tags`, `RepoUrl`.
  `AssemblyVersion`, `InternalName` and `DalamudApiLevel` are filled by the
  packager and must not be written by hand.
- `<Version>0.1.0</Version>` (currently `0.0.1`). It is the one hand-edited
  version: ticket 23's workflow checks the tag against it. The packager
  emits a four-part `AssemblyVersion` (`0.1.0.0`) unless `VersionComponents`
  says otherwise.
- **API 15 changed this:** Dalamud no longer overwrites the in-zip manifest
  with the `repo.json` entry at install time. The manifest and `repo.json`
  must now agree by hand — ticket 23 owns keeping them in step.
- Keep `InternalName` as `FfxivImeBridge`. It is the plugin's identity key
  across repositories and is effectively immutable once published.

## Acceptance

- `dotnet build -c Release` produces the plugin DLL and a zip whose root
  holds the DLL, its dependencies and `FfxivImeBridge.json` flat — not
  nested under a folder.
- `dotnet test` still passes both suites, with `DALAMUD_HOME` set and
  with it unset.
- The generated manifest's `Author` is the handle and its `DalamudApiLevel`
  is 15.

## Comments

**2026-09-23 — done.**

- **`DALAMUD_HOME`:** the SDK 15 `Sdk.props` settles it. On Linux it
  defaults to `$(HOME)/.xlcore/dalamud/Hooks/dev/`, and a set
  `DALAMUD_HOME` overrides that. `DALAMUD_HOME=/nonexistent` fails the
  build with `Dalamud installation not found at /nonexistent/`, so the
  variable really is read.
- **Manifest fields:** they stay in `FfxivImeBridge.json`.
  - When the SamplePlugin first moved to SDK 15 (`5bde722`), its json
    held `Author`, `Name`, `Punchline`, `Description`,
    `ApplicableVersion` and `Tags`. A later commit (`586b87d`) moved them
    into csproj properties.
  - The packager supports both. Its `auto` mode reads the json first and
    ignores the manifest properties in the csproj, so the csproj
    `<Description>` stays assembly metadata only.
  - The user confirmed on 2026-09-23 that the M2 text is final (it may
    still change) and asked which is better. The json is kept on that
    recommendation.
  - Reasons: the prose is easier to edit as a json string than as an
    XML property, where line breaks and indentation are taken literally.
    It also has the same shape as `repo.json`, which it must match by
    hand.
  - `Punchline` and `Description` are unchanged.
- **Json changes:** `InternalName` and `DalamudApiLevel` are removed from
  the json. The packager sets `InternalName` from the assembly, and its
  `Manifest.DalamudApiLevel` defaults to 15. `RepoUrl` is added.
- **Csproj:** the csproj drops the properties the SDK already sets (TFM,
  x64, unsafe code, copy-local, reference assembly, output path).
  `packages.lock.json` is committed because the SDK turns on
  `RestorePackagesWithLockFile`.
- **Tests:** `DalamudLibPath` now lives in the test csproj, its only
  user. It copies the SDK's own per-OS lookup, where a set
  `DALAMUD_HOME` always wins, so the tests find the same Dalamud as the
  plugin.
- **Checks:** `dotnet build -c Release` gives 0 warnings.
  - `latest.zip` holds the DLLs, `deps.json` and `FfxivImeBridge.json` at
    its root (plus the plugin's own `Assets/`).
  - The manifest has `Author: niku-0`, `DalamudApiLevel: 15` and
    `AssemblyVersion: 0.1.0.0`.
  - `dotnet test` passes 311 + 21 tests with `DALAMUD_HOME` set and
    with it unset.
- **For ticket 25:** the generated manifest still has
  `"AcceptsFeedback": true`.
