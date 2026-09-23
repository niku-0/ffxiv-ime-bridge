# M2.5 — Preedit baseline matches the vanilla chat text

Status: resolved
Type: task
Blocked by: 14

Ticket 08's in-game note: the preedit sits slightly lower than the chat
text it continues. With the AXIS font in (ticket 14) the glyph metrics
match, so what is left is the vertical anchor (grilling 2026-09-18, Q1).
Spec: "Rendering" (preedit inline at the native cursor).

## Done when

- `CompositionLayout.LayoutPreedit` places the text so its baseline sits on
  the Chat Box text's baseline: derive the offset from the text node
  (`ScreenY`, height, font size, the ascent AXIS reports at that size)
  rather than centring the line in the cursor node. The candidate box keeps
  hanging off the preedit box as now; auto above/below unchanged.
- The Chat Box debug tab shows the numbers (text node top, cursor node top,
  the preedit's top and baseline) so a remaining offset can be read, not
  guessed.
- Host tests: the layout test for the preedit's top updated to the baseline
  rule, with the metrics faked.
- `docs/dev-plugin.md`: one line in the rendering section.

## In-game confirmation (human)

1. `ab`, then compose `こんにちは` (no Enter): the preedit's glyphs sit on
   the same line as `ab`, not below. Screenshot to `.scratch/ffxiv-ime-
   bridge/ref/` if it is still off, with the debug tab's numbers in
   Comments.
2. HUD scale 80 % and 120 %: still aligned.
3. Set `Status: resolved`.

Safety: reads node memory only.

## Comments

- 2026-09-21 (agent): implemented; over to the in-game confirmation.
  - Rule (`CompositionLayout.LayoutPreedit`): `top = textNode.ScreenY +
    chatAscent − ourAscent`, i.e. the Preedit's baseline (top + our ascent)
    lands on the chat text's, taken as the text node's top plus the ascent
    AXIS has at the node's size. The text node draws its text top-left
    (`align=0` in the dump), so the line's top is the node's top, not
    centred in its 22 px; the cursor node (2 px above the text node in the
    dump, 24 px tall) no longer positions it vertically. With no size
    override at a whole-pixel size (100 %) both ascents are the same number
    and the Preedit's top is the text node's top exactly — 2 px higher than
    M1's centring at the dump's numbers, which matches "slightly lower". At
    a fractional HUD scale the handle is built at a whole pixel (80 % → 13
    px) while the game draws 12.8 px, so the tops differ by a fraction of a
    pixel on purpose: the baselines are what coincide.
  - Ascent: `PushedFont.AscentAt(px)` = the handle's `ImFont.Ascent × px /
    FontSize`; for a game font handle Dalamud sets `Ascent` from the fdt's
    header (`GamePrebakedFontHandle.PatchFontMetricsIfNecessary`) and the
    glyphs' `Y0` from the fdt's `CurrentOffsetY`, so ImGui places the glyphs
    below the line's top as the game does. The chat text's size is unrounded
    (`OverlayFontSize.ChatTextPx`).
  - Not verified on the host: that the game adds no offset of its own to the
    text node's top. If the glyphs still sit off, the debug tab's `preedit:`
    line has the numbers: `text node top`, `cursor node top`, `preedit top`,
    `baseline`, both ascents. The fix would be a constant in
    `LayoutPreedit`'s one line.
  - Wiring: `CursorAnchor` gained `TextTop`, `TextMetrics` gained `Ascent`
    and `ChatAscent`; `CompositionOverlay.LastPreedit` feeds the debug
    tab (its constructor takes the overlay). Tests: the layout test for the
    top updated, one added for the override case, one for `ChatTextPx`.

- 2026-09-21 (human): in-game confirmation passed — `ab` + `こんにちは` on
  one line, still aligned at HUD scale 80 % and 120 %. Resolved.
