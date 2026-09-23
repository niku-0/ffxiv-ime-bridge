# M1.4 — Preedit and candidate list rendered at the Cursor

Status: resolved
Type: task
Blocked by: 07

Draw the Preedit inline over the Chat Box at the Cursor's screen position,
and fcitx5's candidates next to it. Spec "Rendering"; anchor per ticket 04;
README "Candidate text carries Mozc's annotation".

## Done when

### Preedit

- Anchor: `AtkComponentInputBase.CursorContainer` of the Chat Box
  (`ScreenX/Y`, height), read on Draw; if null, text node `ScreenX` +
  measured width as the fallback (known to drift when the text scrolls).
- Rendering: ImGui foreground draw list (no window, no focus), opaque
  background the width of the preedit text, then segments from the
  snapshot's `Preedit` with `TextFormat`: underline for `Underline`,
  highlighted background for `HighLight`, bold/strike as available. The
  preedit cursor (UTF-8 byte offset, README) drawn as a thin bar.
- Font: Dalamud's default font at the game's UI scale for M1 (AXIS via
  `IFontAtlas` and a size option are M2). Japanese glyphs must render —
  verify Dalamud's default atlas covers them, else load the game's font
  early.
- Hidden when the Chat Box is not focused, when the preedit is empty, or
  when the UI is hidden.
- Overlapping trailing native text mid-string is accepted (spec).
- No game memory is written; only the caret node is read.

### Candidate list

- Source: the snapshot's `UpdateClientSideUI` fields — candidates
  `(label, text)`, selected index, layout hint, `hasPrev`/`hasNext`,
  `auxUp`/`auxDown`.
- Placement: anchored to the Preedit; above it when the anchor is in the
  lower half of the screen, below otherwise; vertical for Mozc's hint,
  horizontal otherwise; clamped to the screen.
- Content: label + text as reported (annotations stay: `か [ひらがな]`),
  selected row highlighted, `▲`/`▼` paging marks from `hasPrev`/`hasNext`,
  aux text above/below when non-empty.
- Keyboard only in M1: Space/arrows/digits/Tab reach fcitx5 through the
  Gate already; nothing here handles keys. Mouse selection is M2.
- Hidden with the Preedit.

## In-game confirmation (human)

1. Forwarding on, `あ`, type `konnichiha`: `こんにちは` underlined at the
   caret, opaque over the box. Space: the segment highlights; Space again:
   the vertical list opens above the box with `1. 今日は` etc., the
   selected row highlighted; Space/arrows move the highlight; `▼` when
   there is a next page.
2. Type `a`, Left twice, `konnichiha`: the preedit starts at the caret
   mid-string and overlaps the trailing `a` (accepted).
3. Escape: everything disappears. Note any glyph boxes (missing font
   coverage) and the visual offset from the caret, then set
   `Status: resolved`.

## Notes

- `SetCursorRect` to fcitx5 is not needed while we draw everything
  ourselves; skip it.
- Host tests only for the segment → draw-op layout and the list placement
  (pure); the rest is the human check above.

## Comments

- 2026-09-18 (agent): implemented; awaiting the in-game confirmation above.
  Host-tested: 21 layout cases in
  `tests/FfxivImeBridge.Tests/CompositionLayoutTests.cs` (segment → draw
  ops, cursor bar, list placement above/below, vertical/horizontal, selected
  row, paging marks, aux lines, screen clamp).
  - `Rendering/CompositionLayout.Plan(state, anchor, screen, metrics)` is
    pure: `CompositionState` → ordered `Fill`/`Text`/`Line` ops with a
    `Paint` role, plus the preedit and candidate boxes.
    `Rendering/CompositionOverlay` paints them on the foreground list from
    `UiBuilder.Draw`, gated on Chat Box focus, a non-empty preedit and the
    UI not hidden. `ChatBoxAccess.CursorAnchor` reads `CursorContainer`
    (position, drawn height, accumulated scale), falling back to the text
    node + `GetTextDrawSize` of the prefix before the module/component
    cursor (code points, ticket 04).
  - Font: `ImGui.GetFont()` (Dalamud's default) at `GetFontSize() × scale`,
    scale = the cursor node's own `ScaleY` times its ancestors' — so the
    preedit follows the chat window's HUD scale. Coverage verified on the
    host by reading `Dalamud.Interface.GlyphRangesJapanese.GlyphRanges` out
    of the shipped `Dalamud.dll` (the ranges its default font is built
    with): 4105 ranges, 7325 code points — all hiragana and katakana,
    full-width Latin (Mozc's pending `ｋ`), `▲`/`▼`, and 6356 CJK
    ideographs including every kanji in Mozc's lists above and 鬱薔薇檸檬.
    That is the game's own font set, so anything the Chat Box can display
    after a Commit is drawable; no early load of the game's font is needed.
    Step 3's glyph-box check stays as the in-game confirmation of it.
  - Choices worth knowing: Mozc's prediction list (empty labels, selected
    -1, aux `[Tabキーで選択]`) is drawn while typing, as fcitx5's own panel
    does — so in step 1 a box is already open before the first Space, and
    the "list opens" moment is the numbered conversion list replacing it.
    The list opens above when the *preedit's* centre is below the screen's
    middle. `NotSet` layout is drawn horizontal. Aux text alone (no
    candidates) still opens the box. Bold is faux (drawn twice, 1 px apart).
    The preedit's vertical centre sits on the cursor node's centre. Aux
    lines are drawn as plain muted text; the `TextFormat` fcitx5 attaches to
    their segments is dropped (the ticket asks for formatting on the
    preedit only).
  - Not here: the overlay does not check `Forwarding`. Toggling Forwarding
    off mid-Composition leaves fcitx5's preedit standing (nothing resets the
    context on that edge) and it stays drawn; the game gets the keys
    meanwhile. Worth a `Reset()` on the off edge in ticket 10 if it bothers.
- Human test: Everything works. 1 visual problem, in japanese input mode text is offset few pixels lower than the native chat text.

- 2026-09-18 (human): in-game run passed — steps 1–3 as described:
  preedit underlined at the Cursor over the box, highlight on the first
  Space, the vertical list with the selected row and `▼` on the second,
  mid-string overlap as accepted, Escape clears everything. Resolved.
  - Finding: the Japanese preedit text is drawn a bit **lower than the
    vanilla chat text** on the same line. The preedit box is centred on
    the cursor node's height (`top = anchor.Y + (Height − lineHeight) / 2`)
    rather than sharing the native text's baseline; Dalamud's font also has
    its own ascent. Cosmetic in M1 (spec: pixel-perfect match not
    required); worth a vertical offset alongside the AXIS font and size
    option in M2, when the text node's baseline can be matched instead of
    the cursor node's centre.

