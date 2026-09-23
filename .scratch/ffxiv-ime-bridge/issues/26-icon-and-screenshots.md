# M3.6 — Plugin icon and screenshots

Status: ready-for-human
Type: task

Not a 0.1.0 blocker. The plugin ships without images until these exist; the
user is making them.

## What is needed

- **`icon.png`**: square, between 64x64 and 512x512 (goatcorp's submission
  spec). The icon sketch among the unpublished reference captures is 48x46
  — below the floor, and a sketch rather than an icon, so it cannot be
  scaled into one.
- **Up to five screenshots**, 730x380 the practical target. The in-game
  screenshot among the unpublished reference captures (1427x183) shows the
  plugin working and is the obvious basis for the first one.

## Then

Host them in the public repository and add `IconUrl` and `ImageUrls` to
`repo.json` and the manifest, keeping the two in step as ticket 23 requires.
Nothing else changes; the plugin does not read these files.

The Square Enix UI-asset extracts in the unpublished reference captures
(the `uld`/`idl` ones from ticket 14) stay unpublished — their research is
done and lives in prose.

## Comments

Moved to GitHub issue #1.
