# M2.6 — Mouse selection in the candidate list

Status: resolved
Type: task
Blocked by: 15

Click a candidate to select it, click or wheel to page, all without the
game seeing the click — the spec's condition for allowing mouse selection
at all (grilling 2026-09-18, Q7, Q13). The library already has
`SelectCandidateAsync`, `NextPageAsync`, `PreviousPageAsync`.
Spec: "Rendering" (candidate list, click-to-select); `CONTEXT.md` Swallow;
ADR-0003 (why the hook and not an ImGui window).

## Done when

- `CompositionPlan` gains hit regions: one `Rect` per candidate row/cell
  (`Select(i)`), one per `▲`/`▼` mark (`PreviousPage`/`NextPage`), plus
  the preedit box and the candidate box as a whole. Pure, host-tested.
- The overlay keeps the last frame's plan; `MessagePumpHook` also passes
  mouse messages (`WM_LBUTTONDOWN/UP`, `WM_RBUTTONDOWN/UP`,
  `WM_MBUTTONDOWN/UP`, `WM_XBUTTON*`, `WM_MOUSEWHEEL`, `WM_MOUSEMOVE` is
  **not** filtered) to a `MouseGate` that decides, while the Gate is
  active and a Composition is shown:
  - Inside the candidate box: every button press and its release, and the
    wheel, are Swallowed. Left press on a row → `SelectCandidate(i)`; on
    `▲`/`▼` → the page call; wheel up/down → previous/next page. Other
    buttons do nothing. A release is Swallowed iff its press was.
  - Inside the preedit box: Swallowed, nothing called.
  - Anywhere else: passed untouched (a click elsewhere in the Chat Box
    moves the native Cursor; the preedit re-anchors next frame, as the
    spec accepts).
  - The calls are fire-and-forget; the `UpdateClientSideUI` they cause
    arrives out of band and the tick renders it, as today.
- No hover highlight: the selected row is fcitx5's state only.
- Trace: mouse decisions appear in the Keyboard tab's trace with their own
  rule names (`Candidate`, `Page`, `Preedit`) so a wrong hit-test is
  visible.
- `docs/dev-plugin.md`: mouse rules in the rendering section; "mouse
  selection is M2" goes.
- Host tests: hit regions from a plan (vertical and horizontal layouts,
  paging marks); the mouse gate's verdicts and calls for each region and
  button; release follows press; nothing acts without a Composition.

## In-game confirmation (human)

1. `konnichiha`, Space: list shown. Click the third row: it becomes the
   selected one (preedit changes), Chat Box still focused, nothing sent.
2. Click `▼` (or wheel down over the list): next page; `▲`/wheel up: back.
3. Right-click inside the list: nothing happens, no game context menu.
4. Click on the preedit itself: nothing happens, cursor unmoved.
5. Click in the Chat Box text left of the preedit: the native cursor moves
   there and the preedit follows it; Enter commits at the new spot.
6. Click on the chat log (message history) mid-composition: as before this
   ticket (whatever the game did at M1 — note it).
7. Set `Status: resolved`.

Safety: mouse messages over the overlay are dropped, nothing else; no
game memory written; nothing sent (ADR-0001).

## Comments

- 2026-09-21 (agent): implemented; over to the in-game confirmation.
  - Layout: `CompositionPlan.HitRegions` — a `HitRegion(Rect, HitAction,
    Candidate)` per candidate row (the content width in a column) or cell
    (its text width in a row) and per `▲`/`▼`; `ZoneAt` (preedit / candidate
    box / outside; the box wins where they overlap) and `RegionAt` (null over
    the padding and the aux lines). The column's footer `▲ ▼` is now two
    cells `CellGap` apart instead of one string, so each mark has a rect.
  - Capture: `MouseMessage` (pure; point in client pixels — the hook
    converts `WM_MOUSEWHEEL`'s screen point with `ScreenToClient` for the
    window the message was posted to), `MouseGate` (pure, per the ticket's
    rules, plus the session's last observed Chat Box focus, so a click after
    a focus loss is `Inactive` even before the next frame drops the plan; a
    double-click's `WM_*DBLCLK`, which Windows posts in place of the second
    down, is a press — beyond the ticket's message list, or it would leak to
    the game; `WM_MOUSEHWHEEL` and moves are not filtered). `KeyboardCapture` hosts both gates and one
    trace; mouse decisions while nothing is shown (`Inactive`) are not
    traced, so ordinary play does not flood it. `Seen`/`Swallowed` count the
    traced mouse messages too.
  - Session: `IInputContextClient.SelectCandidate/NextPage/PreviousPage`,
    fire-and-forget through `Forget` like `FocusIn`.
  - Wheel over the box pages regardless of `HasPreviousPage`/`HasNextPage`;
    fcitx5 ignores a page it does not have.
  - Not verified on the host: that `lParam`'s client coordinates line up
    with the overlay's ImGui coordinates 1:1 under Wine (they should — node
    `ScreenX/Y` are client pixels and the overlay draws at them), and the
    `ScreenToClient` conversion for the wheel. The trace prints each mouse
    message's point next to its rule; a row's rect can be read off the
    Chat Box readout if a hit lands wrong.

- 2026-09-21 (human): in-game confirmation passed — steps 1–6 as described:
  click selects a row, `▼`/`▲` and the wheel page, right-click and a click
  on the preedit do nothing, a click left of the preedit moves the native
  Cursor and the preedit follows. Resolved.
