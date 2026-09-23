using System.Collections.Immutable;
using System.Numerics;
using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Rendering;

/// <summary>
/// Turns a <see cref="CompositionState"/> snapshot into draw ops: the Preedit
/// inline at the Cursor, segment by segment with fcitx5's formatting, and the
/// candidate list anchored to it. Pure; the overlay supplies the font metrics
/// and paints the ops.
/// </summary>
internal static class CompositionLayout
{
    /// <summary>Between the preedit and the candidate box.</summary>
    public const float Gap = 4f;
    /// <summary>Inside the candidate box.</summary>
    public const float Padding = 4f;
    /// <summary>Between rows of a vertical list (and the aux lines).</summary>
    public const float RowGap = 2f;
    /// <summary>Between cells of a horizontal list.</summary>
    public const float CellGap = 12f;

    public static CompositionPlan Plan(CompositionState state, CursorAnchor anchor, Vector2 screen, TextMetrics metrics)
    {
        if (state.Preedit.IsEmpty) return CompositionPlan.Nothing;
        var ops = ImmutableArray.CreateBuilder<DrawOp>();
        var regions = ImmutableArray.CreateBuilder<HitRegion>();
        var preeditBox = LayoutPreedit(state.Preedit, anchor, metrics, ops);
        var candidateBox = LayoutCandidates(state, preeditBox, screen, metrics, ops, regions);
        return new CompositionPlan(ops.ToImmutable(), preeditBox, candidateBox, regions.ToImmutable());
    }

    /// <summary>
    /// Opaque ground the width of the text at the Cursor's x, then each segment
    /// with its flags, then the cursor bar. The line's baseline sits on the Chat
    /// Box text's (the text node's top plus the ascent AXIS has at its size),
    /// not centred on the cursor node, which sat the text low (ticket 15).
    /// </summary>
    private static Rect LayoutPreedit(Preedit preedit, CursorAnchor anchor, TextMetrics metrics, ImmutableArray<DrawOp>.Builder ops)
    {
        var lineHeight = metrics.LineHeight;
        var top = anchor.TextTop + metrics.ChatAscent - metrics.Ascent;
        var widths = preedit.Segments.Select(s => metrics.WidthOf(s.Text)).ToArray();
        var box = new Rect(anchor.X, top, widths.Sum(), lineHeight);
        ops.Add(new Fill(box, Paint.Background));

        var x = anchor.X;
        for (var i = 0; i < preedit.Segments.Length; i++)
        {
            var (text, format) = preedit.Segments[i];
            var width = widths[i];
            if (format.HasFlag(TextFormat.Highlight)) ops.Add(new Fill(new Rect(x, top, width, lineHeight), Paint.Highlight));
            ops.Add(new Text(new Vector2(x, top), text, Paint.Preedit, format.HasFlag(TextFormat.Bold)));
            if (format.HasFlag(TextFormat.Underline)) ops.Add(HorizontalLine(x, width, top + lineHeight - 1));
            if (format.HasFlag(TextFormat.Strike)) ops.Add(HorizontalLine(x, width, top + lineHeight / 2));
            x += width;
        }

        if (preedit.CursorIndex is var cursor and >= 0)
        {
            var cursorX = anchor.X + metrics.WidthOf(preedit.Text[..cursor]);
            ops.Add(new Line(new Vector2(cursorX, top), new Vector2(cursorX, top + lineHeight), Paint.Cursor));
        }
        return box;
    }

    private static Line HorizontalLine(float x, float width, float y) => new(new Vector2(x, y), new Vector2(x + width, y), Paint.Preedit);

    /// <summary>
    /// A cell of the candidate box, placed relative to the box's content origin:
    /// its text, and for a candidate or a paging mark what a press on it does
    /// (<see cref="Hit"/>, whose rect is the cell's) and whether it is the
    /// selected candidate (<see cref="Selected"/>: filled).
    /// </summary>
    private readonly record struct Cell(Rect Rect, string Value, Paint Paint, HitAction? Hit = null, int Candidate = -1, bool Selected = false);

    /// <summary>
    /// Aux-up line, the candidates (a column for fcitx5's vertical hint, a row
    /// otherwise, with the paging marks after them), aux-down line; the box
    /// above the preedit when the Cursor is in the lower half of the screen,
    /// below otherwise, clamped to the screen. Null when there is nothing to list.
    /// Candidates and marks become hit regions (ticket 16); aux lines do not.
    /// </summary>
    private static Rect? LayoutCandidates(CompositionState state, Rect preeditBox, Vector2 screen, TextMetrics metrics, ImmutableArray<DrawOp>.Builder ops, ImmutableArray<HitRegion>.Builder regions)
    {
        var auxUp = Concat(state.AuxUp);
        var auxDown = Concat(state.AuxDown);
        if (state.Candidates.IsEmpty && auxUp.Length == 0 && auxDown.Length == 0) return null;

        var cells = new List<Cell>();
        var lineHeight = metrics.LineHeight;
        var contentWidth = 0f;
        var y = 0f;

        void AddLine(string value, Paint paint)
        {
            cells.Add(new Cell(new Rect(0, y, metrics.WidthOf(value), lineHeight), value, paint));
            contentWidth = Math.Max(contentWidth, metrics.WidthOf(value));
            y += lineHeight + RowGap;
        }

        // A row of cells, left to right on the current line: the horizontal list, and the vertical list's footer of marks.
        var x = 0f;
        void AddCell(string value, Paint paint, HitAction hit, int candidate = -1, bool selected = false)
        {
            cells.Add(new Cell(new Rect(x, y, metrics.WidthOf(value), lineHeight), value, paint, hit, candidate, selected));
            x += metrics.WidthOf(value) + CellGap;
        }
        void EndRow()
        {
            contentWidth = Math.Max(contentWidth, x - CellGap);
            x = 0f;
            y += lineHeight + RowGap;
        }

        if (auxUp.Length > 0) AddLine(auxUp, Paint.Muted);

        var hasMarks = state.HasPreviousPage || state.HasNextPage;
        if (state.Layout == CandidateLayout.Vertical)
        {
            var rows = state.Candidates.Select(c => c.Label + c.Text).ToArray();
            var rowWidth = rows.Length == 0 ? 0f : rows.Max(metrics.WidthOf);
            for (var i = 0; i < rows.Length; i++)
            {
                cells.Add(new Cell(new Rect(0, y, rowWidth, lineHeight), rows[i], Paint.Ink, HitAction.SelectCandidate, i, i == state.SelectedCandidate));
                y += lineHeight + RowGap;
            }
            contentWidth = Math.Max(contentWidth, rowWidth);
            if (hasMarks)
            {
                if (state.HasPreviousPage) AddCell("▲", Paint.Muted, HitAction.PreviousPage);
                if (state.HasNextPage) AddCell("▼", Paint.Muted, HitAction.NextPage);
                EndRow();
            }
        }
        else if (!state.Candidates.IsEmpty || hasMarks)
        {
            if (state.HasPreviousPage) AddCell("▲", Paint.Muted, HitAction.PreviousPage);
            for (var i = 0; i < state.Candidates.Length; i++)
            {
                AddCell(state.Candidates[i].Label + state.Candidates[i].Text, Paint.Ink, HitAction.SelectCandidate, i, i == state.SelectedCandidate);
            }
            if (state.HasNextPage) AddCell("▼", Paint.Muted, HitAction.NextPage);
            EndRow();
        }

        if (auxDown.Length > 0) AddLine(auxDown, Paint.Muted);

        // Vertical rows span the whole content width once it is known.
        if (state.Layout == CandidateLayout.Vertical)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Hit == HitAction.SelectCandidate) cells[i] = cells[i] with { Rect = cells[i].Rect with { Width = contentWidth } };
            }
        }

        var size = new Vector2(contentWidth + 2 * Padding, y - RowGap + 2 * Padding);
        var above = preeditBox.Y + preeditBox.Height / 2 > screen.Y / 2;
        var origin = new Vector2(preeditBox.X, above ? preeditBox.Y - Gap - size.Y : preeditBox.Bottom + Gap);
        origin = Vector2.Max(Vector2.Zero, Vector2.Min(origin, screen - size));
        var box = new Rect(origin.X, origin.Y, size.X, size.Y);

        ops.Add(new Fill(box, Paint.Background));
        var content = origin + new Vector2(Padding, Padding);
        foreach (var cell in cells)
        {
            if (cell.Selected) ops.Add(new Fill(cell.Rect.Offset(content), Paint.Selected));
        }
        foreach (var cell in cells)
        {
            ops.Add(new Text(content + cell.Rect.Position(), cell.Value, cell.Paint, Bold: false));
            if (cell.Hit is { } hit) regions.Add(new HitRegion(cell.Rect.Offset(content), hit, cell.Candidate));
        }
        return box;
    }

    private static string Concat(ImmutableArray<TextSegment> segments) => string.Concat(segments.Select(s => s.Text));

    private static Vector2 Position(this Rect rect) => new(rect.X, rect.Y);

    private static Rect Offset(this Rect rect, Vector2 by) => rect with { X = rect.X + by.X, Y = rect.Y + by.Y };
}
