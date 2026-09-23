# M3.2 — Move to Dalamud.NET.Sdk

Status: ready-for-agent
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
