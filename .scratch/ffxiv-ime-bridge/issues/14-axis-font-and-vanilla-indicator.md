# M2.4 — AXIS font for the overlay, and the Indicator in the game's own style

Status: resolved
Type: task
Blocked by: 12

The Preedit, the candidate list and the Indicator switch from Dalamud's
default font to the game's AXIS face at the Chat Box's own size, and the
Indicator moves to where the game's input-mode badge sits, in its style
(grilling 2026-09-18, Q8, Q15, Q16).
Spec: "Rendering" (font, Indicator).

Reference: `.scratch/ffxiv-ime-bridge/ref/icon.png` (the badge, close-up:
a golden `あ` on a pale rounded square with a warm glow, about one line
high) and `ref/icon-with-chat-box.png` (in context: the badge sits at the
top-left of the input frame, immediately left of the `Say` channel label,
above the text line; the text line itself starts at the speech-bubble
icon's right edge). Both are from the Windows client (found online),
so whether the badge node exists and is visible under Wine is unknown:
the dump records its visibility and what drives it, and if it is visible
ours hides behind it rather than fights it.

## Done when

### Font

- One `IFontHandle` from `IFontAtlas` (`NewGameFontHandle`, AXIS family)
  owned by a small `OverlayFont` service, rebuilt asynchronously when the
  size changes. Size: by default the Chat Box text node's font size
  (`AtkTextNode.FontSize`, times the accumulated scale the overlay already
  computes — verify the unit against what the game draws), else
  `FontSizeOverride` from config. Preedit, candidates and Indicator use
  this one handle; `CompositionOverlay`'s `ImGui.GetFont()` and the
  Indicator's `CalcTextSize` go.
- Preedit text colour: the game's IME text colour if it is readable
  (`ConfigOption.IMEColor`, verify it is a UI colour and what it contains),
  else the current white. Note in the ticket which it was.

### Indicator, vanilla style

- **Step one is a dump, human-run:** the debug window's Chat Box tab gets a
  **Dump input nodes** button that walks the ChatLog addon's node tree
  around the text input — the input component and its siblings up to the
  addon root — and logs, per node: id, type, position, size, visibility,
  text (for text nodes: the string, font size, colours, edge/glow), for
  image/nine-grid nodes the texture path and part UVs
  (`UldManager` assets → `AtkTexture.Resource->TexFileResourceHandle`).
  The user runs it with the Chat Box focused on `Say` and pastes the lines
  about the badge (the small `あ`/`A` in a white box with a golden glow,
  left of the channel name) into Comments.
- Then, from the dump: if the badge is a text node over a nine-grid/image
  part, the Indicator draws our glyph at that node's screen position and
  size with the same colours (dark-yellow glyph, white box, glow) using the
  game's texture part through `ITextureProvider.GetFromGame` for the box;
  if it is one baked texture per mode, draw that part. If the game's own
  badge node is visible at the same time (unlikely on Linux, where the
  Native Path never activates), hide ours behind it rather than fight it.
  The `!` Degraded glyph keeps its warning colour in the same box.
- Fallback, written down if taken: no such node found → the AXIS glyph at
  the current position, coloured with the game's IME colour. Spend nothing
  more on it.
- Note for M4, not this ticket: whether the dump shows the game drawing its
  own candidate rows (`ListItemA.tex` looks like row backgrounds).

### Docs and tests

- `docs/dev-plugin.md`: the rendering section's "AXIS and a size option
  are M2" and the Indicator paragraph updated; the dump button documented.
- Host tests: font-size selection (node size vs override, scale applied);
  layout tests unchanged (metrics are injected).

## In-game confirmation (human)

1. Dump input nodes with the Chat Box focused; paste the badge's lines into
   Comments. (Agent then finishes the Indicator part.)
2. Compose: preedit and candidates in AXIS, same size as the chat text at
   HUD scale 100 %; change the chat window's HUD scale — the overlay
   follows. Set an override of 24: bigger; back to match.
3. Indicator: `あ`/`A` sits where the game's badge sits, in its style; `!`
   while Degraded (kill fcitx5).
4. Set `Status: resolved`.

Safety: reads node memory only; the dump writes nothing; the glow/box are
drawn by ImGui, never by editing game nodes.

## Comments
- 2026-09-19 (agent): font part and the dump implemented; the Indicator's
  badge style waits on the dump (step 1 above is yours, then it comes back
  to the agent). Font: `Rendering/OverlayFont.cs` owns an atlas of the
  plugin's own (`IUiBuilder.CreateFontAtlas`, async, *not* global scaled —
  the shared `UiBuilder.FontAtlas` would multiply every size by Dalamud's
  global scale) with one `NewGameFontHandle(GameFontStyle(Axis, px))`
  drawing and, on a size change, a second building asynchronously that
  takes over once available (the current one keeps drawing, scaled by
  ImGui, meanwhile; one build at a time). Size:
  `Rendering/OverlayFontSize.cs`, pure — override as screen px when > 0,
  else `AtkTextNode.FontSize × 4/3 × accumulated scale`, whole px, ≥ 8.
  **Unit, as far as the host can verify:** `FontSize` is a byte holding the
  fdt's pt value (the chat default is 12 → `AXIS_12.fdt`), and Dalamud's
  own `GameFontStyle` converts with `SizePx = SizePt × 4/3` (AXIS_12 =
  16 px lines); step 2's "same size as the chat text" is the in-game check
  — if it is off by a constant factor, `PixelsPerPoint` is the one number
  to change. Preedit colour: **there is no `ConfigOption.IMEColor`**; the
  game keeps it as a `ByteColor` (RGBA, R low byte) on the text input's ULD
  data, `AtkUldComponentDataTextInput.IMEColor`, next to a `CandidateColor`
  (byte order from the struct itself: `R`/`G`/`B`/`A` overlay `RGBA` at
  offsets 0–3, so R is the low byte of the `uint`),
  and as `AtkValue`s in the component's `HandlerValues` (the addon's say,
  taken first when set, as with MaxByte). `NativeWrite/ChatBoxStyle.cs`
  reads both (plus font size/type and the text node's accumulated scale)
  and the Preedit takes the game's colour when alpha ≥ 50 % and the
  brightest channel ≥ 0x60 (our ground is near-black), else white; the
  candidates keep white. What the colour actually is on this client shows
  in the Chat Box readout's new `style:` line — paste it with the dump.
  The layout got a `Paint.Preedit` for the preedit's text/lines (four
  layout-test assertions changed paint name, nothing else). Indicator: now
  AXIS at the overlay's size, and moved to where the badge sits per the
  reference — immediately left of the channel label
  (`AddonChatLog.CurrentChannelTextNode`), vertically centred on it; the
  text node's left edge is the fallback, and it now also hides with the
  game UI (`GameUiHidden`), as the overlay always did. Colours unchanged
  (white/warning with a shadow) until the dump says what the badge is. Dump: **Dump input
  nodes** on the Chat Box tab walks the whole ChatLog tree from the root
  (components expanded, visited set, 800-line cap) and writes it to the
  clipboard, `dalamud.log` (`Node dump: …`) and the tab; the input's
  component/text/cursor nodes and the channel label are marked `<<`. Per
  node: id, type, screen box, `IsVisible`, alpha, `NodeFlags`, timeline
  label/frame; text: string, font type/size, text/edge/background colours,
  `TextFlags` (Edge/Glare/Emboss), line spacing; image/nine-grid: part id,
  UV rect, asset id, texture type, icon id, texture path; component: type
  and node count. Host tests: `OverlayFontSizeTests` (pt→px, scale,
  override, default, minimum), `ChatBoxStyleTests` (handler over ULD,
  readable → ink, unreadable → null); 260 in the plugin suite.
  `docs/dev-plugin.md`: rendering, Indicator, settings and Chat Box tab
  paragraphs, new "Font and the Indicator's place (M2.4)" section.
  **Your turn:** step 1 — focus the Chat Box on `Say`, click **Dump input
  nodes**, paste the lines around the channel label (the `<< channel label`
  line, its siblings, and any image/nine-grid/text nodes just left of it)
  plus the readout's `style:` line here; and, if you like, steps 2–3 as
  they stand (font size, HUD scale, override 24; the glyph left of `Say`).
- dump:
  [INF] [FfxivImeBridge] Node dump: ChatLog addon at (22,969) scale=1 visible=True; text input owner node id=5, channel label node id=4
  [INF] [FfxivImeBridge] Node dump: #1 res at (22,969) 600×459 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, Focusable, EmitsEvents
  [INF] [FfxivImeBridge] Node dump:   #16 collision at (22,969) 600×459 visible=yes alpha=255 flags=Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable, EmitsEvents
  [INF] [FfxivImeBridge] Node dump:   #15 collision at (22,1386) 518×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
  [INF] [FfxivImeBridge] Node dump:   #14 ninegrid at (22,969) 600×381 visible=no alpha=127 flags=AnchorTop, AnchorLeft, AnchorRight, EmitsEvents part=12 offsets(t/b/l/r)=20/20/20/20 blend=0 uv=(84,0) 42×42 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  [INF] [FfxivImeBridge] Node dump:   #11 res at (456,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, EmitsEvents timeline(label=101 frame=0)
  [INF] [FfxivImeBridge] Node dump:     #13 1003 at (456,1386) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
  [INF] [FfxivImeBridge] Node dump:     #12 1002 at (456,1386) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0)
  [INF] [FfxivImeBridge] Node dump:   #10 1004 at (420,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
  [INF] [FfxivImeBridge] Node dump:   #9 1001 at (384,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
  [INF] [FfxivImeBridge] Node dump:   #8 image at (353,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents part=15 wrap=1 imageFlags=FlipH uv=(1,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  [INF] [FfxivImeBridge] Node dump:   #70004 1011 at (45,1386) 26×28 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, Enabled, EmitsEvents timeline(label=1 frame=0)
  [INF] [FfxivImeBridge] Node dump:   #70003 1011 at (353,1386) 39×42 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, Enabled, EmitsEvents timeline(label=1 frame=0)
  [INF] [FfxivImeBridge] Node dump:   #70002 1011 at (262,1386) 91.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
  [INF] [FfxivImeBridge] Node dump:   #70001 1011 at (168,1386) 94.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
  [INF] [FfxivImeBridge] Node dump:   #7 1011 at (56,1386) 112.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=2.3)
  [INF] [FfxivImeBridge] Node dump:   #6 image at (17,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents part=6 wrap=1 imageFlags=0 uv=(0,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  [INF] [FfxivImeBridge] Node dump:   #5 1012 at (56,1358) 562×28 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, Focusable, Droppable, EmitsEvents timeline(label=0 frame=0.7) << text input component
  [INF] [FfxivImeBridge] Node dump:   #4 text at (61,1337) 441×31.5 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents text="　 Say" font=Axis/14pt align=3 colour=#FFFFFFFF edge=#FF0C6A8E background=#00000000 textFlags=Glare lineSpacing=14 << channel label
  [INF] [FfxivImeBridge] Node dump:   #3 1008 at (22,1344) 174×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
  [INF] [FfxivImeBridge] Node dump:   #2 1005 at (586,979) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
- 2026-09-19 (agent): the dump stopped at the addon's top level — no
  component was expanded (the text input `#5` shows no children, its text
  and cursor nodes are unmarked), because a component node's `Type` is
  `1000 + its ULD component index` (`1012`, `1008`, `1011`, …), not FCS's
  `NodeType.Component` (10000). Fixed (`IsComponent`: `Type ≥ 1000`;
  printed as `component[12]` with the component's type); **please rerun
  the dump** and paste it again, this time with the readout's `style:` line
  too. What this one already says: the channel label `#4` is a direct child
  of the root at (61,1337) 441×31.5, text `"　 Say"` — a full-width space
  and a space *reserved in the label itself* for the badge — AXIS 14 pt,
  white with a golden edge (`#8E6A0C`, Glare), line spacing 14; the input
  frame `#5` (`component[12]`, 562×28 at (56,1358)) sits under it, and
  `#3` (`component[8]`, 174×42 at (22,1344)) spans the label's left end,
  which is where the badge should be if it is a node of this addon. The
  candidates for the badge are therefore inside `#3`, inside `#5`, or in
  another addon; the rerun decides. Fallback if none: the AXIS glyph at
  the label's leading space, in the label's own colours (white, golden
  edge), which is what the reference shows at that spot.
- Human test dump after fixes:
  ChatLog addon at (22,969) scale=1 visible=True; text input owner node id=5, channel label node id=4
  #1 res at (22,969) 600×459 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, Focusable, EmitsEvents
    #16 collision at (22,969) 600×459 visible=yes alpha=255 flags=Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable, EmitsEvents
    #15 collision at (22,1386) 518×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #14 ninegrid at (22,969) 600×381 visible=no alpha=127 flags=AnchorTop, AnchorLeft, AnchorRight, EmitsEvents part=12 offsets(t/b/l/r)=20/20/20/20 blend=0 uv=(84,0) 42×42 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #11 res at (456,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, EmitsEvents timeline(label=101 frame=0)
      #13 component[3] at (456,1386) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=1
        #2 image at (456,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=24 wrap=2 imageFlags=0 uv=(168,56) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
      #12 component[2] at (456,1386) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=1
        #2 image at (456,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=11 wrap=2 imageFlags=0 uv=(140,28) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
    #10 component[4] at (420,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=1
      #2 image at (420,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=0 wrap=1 imageFlags=0 uv=(0,0) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
    #9 component[1] at (384,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=1
      #2 image at (384,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=8 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
    #8 image at (353,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents part=15 wrap=1 imageFlags=FlipH uv=(1,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #70004 component[11] at (45,1386) 26×28 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, Enabled, EmitsEvents timeline(label=1 frame=0) component=Tab nodes=5
      #6 collision at (45,1388) 26×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #70003 component[11] at (353,1386) 39×42 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, Enabled, EmitsEvents timeline(label=1 frame=0) component=Tab nodes=5
      #6 collision at (353,1389) 26×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #70002 component[11] at (262,1386) 91.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Tab nodes=5
      #6 collision at (262,1389) 61×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #70001 component[11] at (168,1386) 94.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Tab nodes=5
      #6 collision at (168,1389) 63×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #7 component[11] at (56,1386) 112.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=2.3) component=Tab nodes=5
      #6 collision at (56,1389) 75×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #6 image at (17,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents part=6 wrap=1 imageFlags=0 uv=(0,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #5 component[12] at (56,1358) 562×28 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, Focusable, Droppable, EmitsEvents timeline(label=0 frame=0.7) component=TextInput nodes=16 << text input component
      #17 ninegrid at (56,1358) 562×28 visible=yes alpha=153 flags=AnchorTop, AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, EmitsEvents part=17 offsets(t/b/l/r)=13/13/13/13 blend=0 uv=(128,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #4 text at (61,1337) 441×31.5 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents text="　 Say" font=Axis/14pt align=3 colour=#FFFFFFFF edge=#FF0C6A8E background=#00000000 textFlags=Glare lineSpacing=14 << channel label
    #3 component[8] at (22,1344) 174×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents component=DropDownList nodes=2
      #3 component[9] at (40,1113) 133×172 visible=no alpha=255 flags=AnchorTop, AnchorLeft, HasCollision timeline(label=0 frame=0.3) component=List nodes=27
        #3 ninegrid at (40,1113) 133×172 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, HasCollision, Focusable, EmitsEvents part=0 offsets(t/b/l/r)=10/12/10/10 blend=0 uv=(0,0) 32×32 asset=3 textureType=Resource icon=4294967295 path="ui/uld/ListB_hr1.tex"
    #2 component[5] at (586,979) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=3
      #4 collision at (590,983) 20×20 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
      
- Human copy readout:
  raw: "" (0 bytes, 0 code points) active=True
  component cursor=0 sel=0..0; module cursor=0 len=0 sel=0..0 before/sel/after/input bytes=0/0/0/0
  limits: GetInputMaxLength=500 uld maxChar=0 maxByte=500 maxLine=0 maxWidth=0 handler maxChar=0 maxByte=500
  text node (68,1363) 538×22 hidden; cursor node (68,1361) 4×24; measured cursor x=68
  style: font size=12pt type=0 scale=1 ime colour uld=#FF45FF7E handler=#FF45FF7E candidate colour=#FF50C0FF; preedit ink=game #FF45FF7E
- 2026-09-19 (agent): closer — components expand, but only the first
  top-level node of each is listed (`#5` says `nodes=16` and shows `#17`
  alone; `#3` → one List child → one nine-grid of 27): a component's
  `UldManager.RootNode` heads a *sibling chain* of its top-level nodes
  rather than parenting them. Fixed (the walk follows the root's siblings
  both ways); **one more rerun, please** — the badge must be among `#5`'s
  other fifteen nodes or `#3`'s. The readout settles the colour question:
  `IMEColor` on this client is `#FF45FF7E` = (126, 255, 69), a light green,
  readable, so the Preedit now draws in the game's own IME green;
  `CandidateColor` is gold (255, 192, 80). Text node 12 pt at scale 1 →
  16 px handle; cursor node 4×24.
  
- (human) dump after second fix:
  ChatLog addon at (22,969) scale=1 visible=True; text input owner node id=5, channel label node id=4
#1 res at (22,969) 600×459 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, Focusable, EmitsEvents
  #16 collision at (22,969) 600×459 visible=yes alpha=255 flags=Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable, EmitsEvents
  #15 collision at (22,1386) 518×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
  #14 ninegrid at (22,969) 600×381 visible=no alpha=127 flags=AnchorTop, AnchorLeft, AnchorRight, EmitsEvents part=12 offsets(t/b/l/r)=20/20/20/20 blend=0 uv=(84,0) 42×42 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #11 res at (456,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, EmitsEvents timeline(label=101 frame=0)
    #13 component[3] at (456,1386) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=1
      #2 image at (456,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=24 wrap=2 imageFlags=0 uv=(168,56) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
    #12 component[2] at (456,1386) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=1
      #2 image at (456,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=11 wrap=2 imageFlags=0 uv=(140,28) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
  #10 component[4] at (420,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=1
    #2 image at (420,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=0 wrap=1 imageFlags=0 uv=(0,0) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
  #9 component[1] at (384,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=1
    #2 image at (384,1386) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=8 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
  #8 image at (353,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents part=15 wrap=1 imageFlags=FlipH uv=(1,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #70004 component[11] at (45,1386) 26×28 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, Enabled, EmitsEvents timeline(label=1 frame=0) component=Tab nodes=5
    #6 collision at (45,1388) 26×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #5 ninegrid at (45,1386) 26×28 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=7 offsets(t/b/l/r)=8/8/8/8 blend=0 uv=(28,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #3 res at (58,1391) 10×19 visible=yes alpha=127 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=17 frame=0.1)
      #4 text at (58,1391) 10×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize, Emboss lineSpacing=12
    #2 image at (39,1382) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) part=9 wrap=1 imageFlags=0 uv=(0,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #70003 component[11] at (353,1386) 39×42 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, Enabled, EmitsEvents timeline(label=1 frame=0) component=Tab nodes=5
    #6 collision at (353,1389) 26×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #5 ninegrid at (353,1386) 26×28 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=7 offsets(t/b/l/r)=8/8/8/8 blend=0 uv=(28,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #3 res at (372.5,1393.5) 10×19 visible=yes alpha=127 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=17 frame=0.1)
      #4 text at (372.5,1393.5) 10×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize, Emboss lineSpacing=12
    #2 image at (344,1380) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) part=9 wrap=1 imageFlags=0 uv=(0,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #70002 component[11] at (262,1386) 91.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Tab nodes=5
    #6 collision at (262,1389) 61×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #5 ninegrid at (262,1386) 61×28 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=7 offsets(t/b/l/r)=8/8/8/8 blend=0 uv=(28,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #3 res at (281.5,1393.5) 45×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
      #4 text at (281.5,1393.5) 45×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) text="Event" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize, Emboss lineSpacing=12
    #2 image at (253,1380) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) part=9 wrap=1 imageFlags=0 uv=(0,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #70001 component[11] at (168,1386) 94.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Tab nodes=5
    #6 collision at (168,1389) 63×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #5 ninegrid at (168,1386) 63×28 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=7 offsets(t/b/l/r)=8/8/8/8 blend=0 uv=(28,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #3 res at (187.5,1393.5) 47×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
      #4 text at (187.5,1393.5) 47×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) text="Battle" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize, Emboss lineSpacing=12
    #2 image at (159,1380) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) part=9 wrap=1 imageFlags=0 uv=(0,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #7 component[11] at (56,1386) 112.5×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents timeline(label=0 frame=2.3) component=Tab nodes=5
    #6 collision at (56,1389) 75×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #5 ninegrid at (56,1386) 75×28 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=7 offsets(t/b/l/r)=8/8/8/8 blend=0 uv=(28,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #3 res at (75.5,1393.5) 59×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3)
      #4 text at (75.5,1393.5) 59×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) text="General" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize, Emboss lineSpacing=12
    #2 image at (47,1380) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, EmitsEvents timeline(label=0 frame=0) part=9 wrap=1 imageFlags=0 uv=(0,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #6 image at (17,1386) 42×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents part=6 wrap=1 imageFlags=0 uv=(0,56) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #5 component[12] at (56,1358) 562×28 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, Focusable, Droppable, EmitsEvents timeline(label=0 frame=0.7) component=TextInput nodes=16 << text input component
    #17 ninegrid at (56,1358) 562×28 visible=yes alpha=153 flags=AnchorTop, AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, EmitsEvents part=17 offsets(t/b/l/r)=13/13/13/13 blend=0 uv=(128,84) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #16 text at (68,1363) 538×22 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorBottom, AnchorRight, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents text="" font=Axis/12pt align=0 colour=#FFF8F8F8 edge=#FF000000 background=#6400D268 textFlags=Edge, OverflowHidden lineSpacing=14 << text input's text node
    #4 res at (55,1374) 279×331.5 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents
      #15 ninegrid at (55,1374) 186×221 visible=no alpha=255 flags=Enabled, Fill, EmitsEvents part=13 offsets(t/b/l/r)=20/20/20/20 blend=0 uv=(84,42) 42×42 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #14 text at (74.5,1662) 160×21 visible=no alpha=255 flags=AnchorLeft, AnchorBottom, AnchorRight, Enabled, EmitsEvents text="" font=MiedingerMed/12pt align=21 colour=#FFCCCCCC edge=#FF000000 background=#00000000 textFlags=None lineSpacing=12
      #13 component[6] at (70,1632) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1633.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1632) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1632) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #12 component[6] at (70,1602) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1603.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1602) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1602) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #11 component[6] at (70,1572) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1573.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1572) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1572) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #10 component[6] at (70,1542) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1543.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1542) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1542) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #9 component[6] at (70,1512) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1513.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1512) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1512) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #8 component[6] at (70,1482) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1483.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1482) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1482) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #7 component[6] at (70,1452) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1453.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1452) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1452) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #6 component[6] at (70,1422) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1423.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1422) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1422) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
      #5 component[6] at (70,1392) 160×21 visible=no alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Enabled, EmitsEvents timeline(label=0 frame=0) component=Button nodes=3
        #4 collision at (70,1393.5) 160×19 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents
        #3 ninegrid at (70,1392) 160×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, Fill, EmitsEvents timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #2 text at (82,1392) 152×21 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorRight, Visible, Enabled, EmitsEvents text="" font=Axis/12pt align=3 colour=#FFFFFFFF edge=#FF000000 background=#00000000 textFlags=AutoAdjustNodeSize lineSpacing=12
    #2 res at (68,1361) 4×24 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, EmitsEvents timeline(label=101 frame=0.5) << text input's cursor node
      #3 image at (68,1361) 4×24 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled, EmitsEvents timeline(label=0 frame=0) part=14 wrap=1 imageFlags=0 uv=(84,88) 4×24 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
  #4 text at (61,1337) 441×31.5 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, EmitsEvents text="　 Say" font=Axis/14pt align=3 colour=#FFFFFFFF edge=#FF0C6A8E background=#00000000 textFlags=Glare lineSpacing=14 << channel label
  #3 component[8] at (22,1344) 174×42 visible=yes alpha=255 flags=AnchorLeft, AnchorBottom, Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents component=DropDownList nodes=2
    #3 component[9] at (40,1113) 133×172 visible=no alpha=255 flags=AnchorTop, AnchorLeft, HasCollision timeline(label=0 frame=0.3) component=List nodes=27
      #3 ninegrid at (40,1113) 133×172 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, AnchorBottom, AnchorRight, Visible, Enabled, HasCollision, Focusable, EmitsEvents part=0 offsets(t/b/l/r)=10/12/10/10 blend=0 uv=(0,0) 32×32 asset=3 textureType=Resource icon=4294967295 path="ui/uld/ListB_hr1.tex"
      #21025 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21024 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21023 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21022 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21021 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21020 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21019 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=32800 lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21018 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21017 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21016 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21015 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21014 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21013 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21012 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21011 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21010 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21009 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21008 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21007 component[10] at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1117) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1117) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1117) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1121.2) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1114) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21006 component[10] at (47.5,1312.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1312.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1312.5) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1312.5) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1316.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Free Company" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1309.5) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21005 component[10] at (47.5,1279.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1279.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1279.5) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1279.5) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1283.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Shout" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1276.5) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21004 component[10] at (47.5,1246.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1246.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1246.5) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1246.5) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1250.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Yell" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1243.5) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21003 component[10] at (47.5,1213.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1213.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1213.5) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1213.5) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1217.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Alliance" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1210.5) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21002 component[10] at (47.5,1180.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1180.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1180.5) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1180.5) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1184.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Party" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1177.5) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #21001 component[10] at (47.5,1147.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=8 frame=2) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1147.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1147.5) 101×24 visible=no alpha=0 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1147.5) 101×24 visible=yes alpha=214 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1151.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Say" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1144.5) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
      #2 component[10] at (47.5,1114.5) 117×22 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled, RespondToMouse timeline(label=1 frame=0.1) component=ListItemRenderer nodes=5
        #6 collision at (47.5,1114.5) 117×22 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, Fill, HasCollision, RespondToMouse, Focusable
        #5 ninegrid at (71.5,1114.5) 101×24 visible=yes alpha=0 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) part=1 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,22) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #4 ninegrid at (71.5,1114.5) 101×24 visible=no alpha=255 flags=AnchorLeft, AnchorRight, Enabled timeline(label=0 frame=0) part=0 offsets(t/b/l/r)=0/0/16/1 blend=0 uv=(0,0) 64×22 asset=4 textureType=Resource icon=4294967295 path="ui/uld/ListItemA_hr1.tex"
        #3 text at (83.5,1118.6) 93×20 visible=yes alpha=255 flags=AnchorLeft, AnchorRight, Visible, Enabled timeline(label=0 frame=0) text="Tell" font=Axis/12pt align=3 colour=#FFC5E1EE edge=#FF000000 background=#00000000 textFlags=Emboss lineSpacing=12
        #2 image at (47.5,1111.5) 28×28 visible=no alpha=255 flags=AnchorTop, AnchorLeft, Enabled timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(56,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #2 component[7] at (22,1344) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorLeft, Visible, Enabled, EmitsEvents timeline(label=0 frame=2) component=CheckBox nodes=1
      #2 image at (22,1344) 28×28 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, Focusable, EmitsEvents timeline(label=0 frame=0) part=5 wrap=1 imageFlags=0 uv=(140,0) 28×28 asset=2 textureType=Resource icon=4294967295 path="ui/uld/CircleButtons_hr1.tex"
  #2 component[5] at (586,979) 28×28 visible=yes alpha=255 flags=AnchorTop, AnchorRight, Visible, Enabled, EmitsEvents timeline(label=0 frame=0.3) component=Button nodes=3
    #4 collision at (590,983) 20×20 visible=yes alpha=255 flags=Visible, Enabled, HasCollision, RespondToMouse, EmitsEvents
    #3 image at (586,979) 28×28 visible=yes alpha=255 flags=Visible, Enabled, EmitsEvents timeline(label=0 frame=0) part=4 wrap=1 imageFlags=0 uv=(28,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
    #2 image at (586,979) 28×28 visible=no alpha=31 flags=Enabled, EmitsEvents timeline(label=0 frame=0) part=3 wrap=1 imageFlags=0 uv=(0,28) 28×28 asset=1 textureType=Resource icon=4294967295 path="ui/uld/ChatLog_hr1.tex"
- 2026-09-19 (agent): the third dump is complete (258 lines, every
  component's nodes) and **there is no badge node in the ChatLog addon.**
  The text input's 16 nodes are the frame nine-grid `#17`, the text node
  `#16`, the auto-translate menu (`#4` res: nine `ListItemA` Button rows
  160×21, a `MiedingerMed` page counter, a nine-grid back) and the cursor
  (`#2` res 4×24 with its image); the dropdown `#3` holds the channel List
  and the speech-bubble CheckBox `#2` (28×28 at x 22–50). Nothing draws in
  the label's reserved `"　 "` gap (x 61–80 at 14 pt): on Windows the
  game's IME layer paints the badge there outside the Atk tree, which also
  means there is no game node to hide behind. **Fallback taken, as written
  above:** the AXIS glyph, in the game's IME colour (the Preedit's ink;
  `!` keeps its warning colour), with a shadow. One correction from the
  geometry: "left of the label" would land at x ≈ 38, on the speech-bubble
  icon, so the glyph goes *inside* the label's box at its left edge —
  the gap the label reserves — vertically centred; left of the text node
  only when there is no label. Note for M4: the game draws no candidate
  rows of its own here; the `ListItemA.tex` rows under the input are the
  auto-translate menu (nine Buttons + page counter), not the IME's.
  `docs/dev-plugin.md` Indicator bullet rewritten accordingly. **Your
  turn:** steps 2–4 — compose (AXIS, the Preedit in the game's green,
  size following the chat font and HUD scale; override 24 bigger), the
  glyph in the gap before `Say` in the same green, `!` while Degraded;
  then `Status: resolved`.
- 2026-09-19 (human): in-game run passed. Notes: the Indicator should keep
  a fixed size like the channel name (about 20 by estimate) and not follow
  the chat text's size or colour; it sits a bit too far left — it should
  start at the chat box's edge. Is replicating the vanilla badge scrapped?
- 2026-09-19 (agent): Indicator now follows the channel label, not the
  chat text: its font size (`CurrentChannelTextNode.FontSize`, 14 pt here →
  19 px at 100 %, HUD scale applied, the override ignored) through a second
  handle slot in `OverlayFont`, and its colours — white glyph with the
  label's golden edge (`#8E6A0C`) as a 1 px outline in place of the black
  shadow; `!` keeps the warning colour with the same edge. Position: x at
  the text input's text node left edge (68 on the dump; the label box
  starts at 61, hence "too far left"), y centred on the label. The
  label-less fallback (left of the text node, overlay size, Preedit ink)
  stays. Vanilla badge: not scrapped by choice — the ticket's plan was to
  *borrow* the game's badge node or texture, the dump showed the ChatLog
  has none, and the ticket's own fallback clause ("spend nothing more")
  applied. Drawing the look by hand from `ref/icon.png` (pale rounded box,
  golden glyph, warm glow, all ImGui) is possible and small; say so and it
  becomes a follow-up ticket, or a note in 17 (M2 acceptance).
- 2026-09-19 (human): size and colour look good; the Indicator needs to be
  5 px further left. Also want to try the replicated badge.
- 2026-09-19 (agent): x is now the text node's edge − 5 px × HUD scale.
  The badge: a new `IndicatorStyle` in the config (`Label` default, the
  glyph as it is now; `Badge`), two radios under **Show Indicator** in the
  settings window. `Badge` redraws `ref/icon.png` with ImGui rects at the
  same spot: the glyph (label font size) in the label's edge gold —
  `#8e6a0c`, which is exactly the reference's glyph colour — centred in a
  box one glyph high plus 2 px padding, at least as wide as high; cream
  body `#f7f5ef`, top 35 % shading from clear to gold `#a5883d`, bottom
  25 % from clear to grey `#999b99`, white 1 px border, 3 px rounding, a
  4-ring golden glow fading outwards; all × HUD scale; `!` in warning
  colour in the same box. Colours are the reference's pixels; proportions
  are eyeballed from it, so tune from in-game. Config round trip and the
  by-name check cover the new field. **Your turn:** try both radios; the
  badge's look (band heights, glow strength, padding) is a constants
  change away — say what is off.
- 2026-09-19 (human): the badge's background should be only the middle
  cream colour; the body around the character is way too big.
- 2026-09-19 (agent): bands gone — cream `#f7f5ef` only, white border and
  glow kept. Box now square and 0.85 × the font size (`BadgeHeightOfFont`),
  no padding: the font's line height has air above and below the ink that
  the box was inheriting. Tune the one constant if still off.
- 2026-09-19 (human): better; still too much body right of and below the
  character, and the glyph's edge should be softened a little.
- 2026-09-19 (agent): the box is now fitted to the glyph's ink — the
  font's own glyph bounds (`ImFont.FindGlyph` X0/Y0/X1/Y1 scaled to the
  drawn size, `PushedFont.InkOf`) plus `BadgePadding` 1.5 px each side —
  instead of a share of the line box, whose air below the baseline and
  right of the ink was the extra body. The glyph is softened: four copies
  half a pixel off in each direction at 30 % alpha under the full one
  (`BadgeSoften`, `BadgeSoftenAlpha`).
- 2026-09-20 (human): change the Indicator icon option to
  `ref/ffxiv-indicator-uld-cropped-2.png` (a 22×23 crop of the Windows
  client's `あ` badge from a screenshot).
- 2026-09-20 (agent): the Badge style now draws that crop as a texture
  instead of the ImGui rects: `src/FfxivImeBridge/Assets/badge-hiragana.png`
  (the crop, its four white corner pixels — the crop tool's fill — made
  transparent) at 22×23 px × HUD scale, left edge and vertical centre as
  before, through `ITextureProvider.GetFromFile` per frame; the plain
  glyph draws until the texture has loaded. The crop has `あ` baked in, so
  the other glyphs (`A`, an initial, `!`) get `Assets/badge-frame.png` —
  the same crop with the `あ`'s ink replaced by the body cream `#fbf9f5`
  (pixels darker than `#e0` inside x 3–19, y 3–20; derived by script,
  not hand-painted) — with the glyph centred in the cream (x 2–20,
  y 2–21) by its ink, gold and softened as before. The PNGs copy next to
  the DLL (`Assets/`) at build. Colour bands, glow rings, padding and the
  fitted-box constants are gone with the hand drawing. **Your turn:** try
  `あ` and `A`; if the badge reads small or large next to the label, the
  size is the texture's own × HUD scale, so say by how much.
- 2026-09-20 (human): about 20 % too large.
- 2026-09-20 (agent): `BadgeScale` = 1 / 1.2 on the texture's size (and
  the frame's interior with it), before the HUD scale.
- 2026-09-20 (human): still too large, another 20 % or so.
- 2026-09-20 (agent): `BadgeScale` = 1 / 1.44 (≈ 15×16 px at 100 %).
- 2026-09-20 (human): good for now; close the ticket.
- 2026-09-20 (agent): `Status: resolved`. Open note for later, not blocking: at 1 / 1.44 the crop is downsampled with bilinear filtering, so the 1 px border is a little soft; resampling the two PNGs to ~15×16 and drawing 1:1 would sharpen it.
