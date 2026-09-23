using System.Collections.Immutable;
using System.Numerics;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.Rendering;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>The segment → draw-op layout and the candidate list's placement, with a fixed-pitch stand-in for the font: 10 px per char, 20 px lines, the baseline 16 px down.</summary>
public sealed class CompositionLayoutTests
{
    private const float CharWidth = 10f;
    private const float LineHeight = 20f;
    private const float Ascent = 16f;
    private static readonly TextMetrics Metrics = new(LineHeight, Ascent, ChatAscent: Ascent, text => text.Length * CharWidth);
    private static readonly Vector2 Screen = new(1000, 800);

    /// <summary>A Cursor 24 px tall in the lower half of the screen, where the Chat Box usually is, with the text node's top 2 px below it as in-game.</summary>
    private static readonly CursorAnchor LowerAnchor = new(X: 100, Y: 700, Height: 24, Scale: 1f, TextTop: 702);
    private static readonly CursorAnchor UpperAnchor = new(X: 100, Y: 100, Height: 24, Scale: 1f, TextTop: 102);

    private static Preedit PreeditOf(int cursorByteOffset, params TextSegment[] segments) => new([.. segments], cursorByteOffset);

    private static CompositionState Composing(Preedit preedit) => CompositionState.Idle with { Preedit = preedit };

    private static CompositionState WithCandidates(CompositionState state, CandidateLayout layout, int selected, bool hasPrev = false, bool hasNext = false, params Candidate[] candidates) =>
        state with { Candidates = [.. candidates], SelectedCandidate = selected, Layout = layout, HasPreviousPage = hasPrev, HasNextPage = hasNext };

    private static ImmutableArray<TextSegment> Aux(string text) => [new TextSegment(text, TextFormat.None)];

    private static CompositionPlan Plan(CompositionState state, CursorAnchor anchor) => CompositionLayout.Plan(state, anchor, Screen, Metrics);

    [Fact]
    public void Empty_preedit_draws_nothing()
    {
        var plan = Plan(CompositionState.Idle, LowerAnchor);

        Assert.Empty(plan.Ops);
        Assert.Null(plan.PreeditBox);
        Assert.Null(plan.CandidateBox);
    }

    [Fact]
    public void Opaque_background_comes_first_and_spans_the_text_at_the_cursor()
    {
        var plan = Plan(Composing(PreeditOf(15, new TextSegment("こんにちは", TextFormat.Underline))), LowerAnchor);

        var background = Assert.IsType<Fill>(plan.Ops[0]);
        Assert.Equal(Paint.Background, background.Paint);
        Assert.Equal(new Rect(100, 702, 50, LineHeight), background.Rect); // the line's top on the text node's, ascents being equal
        Assert.Equal(background.Rect, plan.PreeditBox);
    }

    [Fact]
    public void Preedit_baseline_sits_on_the_chat_texts_baseline_whatever_the_overlay_font_size()
    {
        // A size override: the overlay's font is taller than the chat text (ascent 24 vs 13), so its line starts above the text node.
        var overridden = Metrics with { Ascent = 24f, ChatAscent = 13f };

        var plan = CompositionLayout.Plan(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Underline))), LowerAnchor, Screen, overridden);

        var text = Assert.Single(plan.Ops.OfType<Text>());
        Assert.Equal(702 + 13 - 24, text.At.Y);
        Assert.Equal(702 + 13, text.At.Y + overridden.Ascent); // the chat text's baseline: text node top + its ascent
        Assert.Equal(text.At.Y, plan.PreeditBox!.Value.Y);
    }

    [Fact]
    public void Underlined_segment_gets_its_text_and_a_line_along_its_bottom()
    {
        var plan = Plan(Composing(PreeditOf(15, new TextSegment("こんにちは", TextFormat.Underline))), LowerAnchor);

        var text = Assert.Single(plan.Ops.OfType<Text>());
        Assert.Equal(new Text(new Vector2(100, 702), "こんにちは", Paint.Preedit, Bold: false), text);
        var underline = Assert.Single(plan.Ops.OfType<Line>(), l => l.Paint == Paint.Preedit);
        Assert.Equal(new Vector2(100, 721), underline.From);
        Assert.Equal(new Vector2(150, 721), underline.To);
    }

    [Fact]
    public void Segments_are_laid_out_left_to_right_and_the_highlighted_one_gets_a_fill_under_its_text()
    {
        var state = Composing(PreeditOf(0,
            new TextSegment("きょう", TextFormat.Highlight),
            new TextSegment("は", TextFormat.Underline)));

        var plan = Plan(state, LowerAnchor);

        var texts = plan.Ops.OfType<Text>().ToArray();
        Assert.Equal(["きょう", "は"], texts.Select(t => t.Value));
        Assert.Equal(100, texts[0].At.X);
        Assert.Equal(130, texts[1].At.X);

        var highlight = Assert.Single(plan.Ops.OfType<Fill>(), f => f.Paint == Paint.Highlight);
        Assert.Equal(new Rect(100, 702, 30, LineHeight), highlight.Rect);
        Assert.True(plan.Ops.IndexOf(highlight) > 0, "the highlight sits over the background");
        Assert.True(plan.Ops.IndexOf(highlight) < plan.Ops.IndexOf(texts[0]), "the highlight sits under its text");
        Assert.DoesNotContain(plan.Ops.OfType<Line>(), l => l.Paint == Paint.Preedit && l.From.X == 100); // the highlighted segment is not underlined
    }

    [Fact]
    public void Bold_and_strike_map_to_the_text_weight_and_a_mid_line()
    {
        var plan = Plan(Composing(PreeditOf(0, new TextSegment("ab", TextFormat.Bold | TextFormat.Strike))), LowerAnchor);

        Assert.True(Assert.Single(plan.Ops.OfType<Text>()).Bold);
        var strike = Assert.Single(plan.Ops.OfType<Line>(), l => l.Paint == Paint.Preedit);
        Assert.Equal(new Vector2(100, 712), strike.From);
        Assert.Equal(new Vector2(120, 712), strike.To);
    }

    [Fact]
    public void Preedit_cursor_is_a_bar_at_the_utf8_byte_offset()
    {
        // か is 3 bytes: a cursor at byte 3 sits after the first character.
        var plan = Plan(Composing(PreeditOf(3, new TextSegment("かき", TextFormat.Underline))), LowerAnchor);

        var bar = Assert.Single(plan.Ops.OfType<Line>(), l => l.Paint == Paint.Cursor);
        Assert.Equal(new Vector2(110, 702), bar.From);
        Assert.Equal(new Vector2(110, 722), bar.To);
    }

    [Fact]
    public void No_cursor_bar_when_fcitx5_reports_none()
    {
        var plan = Plan(Composing(PreeditOf(-1, new TextSegment("か", TextFormat.Underline))), LowerAnchor);

        Assert.DoesNotContain(plan.Ops.OfType<Line>(), l => l.Paint == Paint.Cursor);
    }

    [Fact]
    public void No_candidate_box_without_candidates_or_aux_text()
    {
        var plan = Plan(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Underline))), LowerAnchor);

        Assert.Null(plan.CandidateBox);
    }

    [Fact]
    public void Vertical_list_opens_above_the_preedit_when_the_cursor_is_in_the_lower_half()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0,
            candidates: [new Candidate("1. ", "化"), new Candidate("2. ", "か [ひらがな]")]);

        var plan = Plan(state, LowerAnchor);

        var box = Assert.NotNull(plan.CandidateBox);
        Assert.Equal(plan.PreeditBox!.Value.Y - CompositionLayout.Gap, box.Bottom);
        Assert.Equal(plan.PreeditBox.Value.X, box.X);

        var rows = plan.Ops.OfType<Text>().Where(t => t.Paint == Paint.Ink && t.At.Y < plan.PreeditBox.Value.Y).ToArray();
        Assert.Equal(["1. 化", "2. か [ひらがな]"], rows.Select(r => r.Value));
        Assert.Equal(rows[0].At.X, rows[1].At.X);
        Assert.Equal(rows[0].At.Y + LineHeight + CompositionLayout.RowGap, rows[1].At.Y);
        Assert.Equal(box.Width, rows.Max(r => r.Value.Length) * CharWidth + 2 * CompositionLayout.Padding);
    }

    [Fact]
    public void List_opens_below_the_preedit_when_the_cursor_is_in_the_upper_half()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0,
            candidates: [new Candidate("1. ", "化")]);

        var plan = Plan(state, UpperAnchor);

        var box = Assert.NotNull(plan.CandidateBox);
        Assert.Equal(plan.PreeditBox!.Value.Bottom + CompositionLayout.Gap, box.Y);
    }

    [Fact]
    public void Selected_row_is_filled_under_its_text()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 1,
            candidates: [new Candidate("1. ", "化"), new Candidate("2. ", "可")]);

        var plan = Plan(state, LowerAnchor);

        var selected = Assert.Single(plan.Ops.OfType<Fill>(), f => f.Paint == Paint.Selected);
        var row = Assert.Single(plan.Ops.OfType<Text>(), t => t.Value == "2. 可");
        Assert.Equal(row.At.Y, selected.Rect.Y);
        Assert.Equal(LineHeight, selected.Rect.Height);
        Assert.Equal(plan.CandidateBox!.Value.X + CompositionLayout.Padding, selected.Rect.X);
        Assert.Equal(plan.CandidateBox.Value.Width - 2 * CompositionLayout.Padding, selected.Rect.Width);
        Assert.True(plan.Ops.IndexOf(selected) < plan.Ops.IndexOf(row));
    }

    [Fact]
    public void Prediction_list_has_no_selected_row()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Underline))), CandidateLayout.Vertical, selected: -1,
            candidates: [new Candidate("", "化"), new Candidate("", "から")]);

        var plan = Plan(state, LowerAnchor);

        Assert.DoesNotContain(plan.Ops.OfType<Fill>(), f => f.Paint == Paint.Selected);
        Assert.Contains(plan.Ops.OfType<Text>(), t => t.Value == "化");
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "▲")]
    [InlineData(false, true, "▼")]
    [InlineData(true, true, "▲ ▼")]
    public void Vertical_list_ends_with_the_paging_marks_it_has(bool hasPrev, bool hasNext, string marks)
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0, hasPrev, hasNext,
            [new Candidate("1. ", "化")]);

        var plan = Plan(state, LowerAnchor);

        var footer = plan.Ops.OfType<Text>().Where(t => t.Paint == Paint.Muted).OrderBy(t => t.At.X).ToArray();
        Assert.Equal(marks.Split(' ', StringSplitOptions.RemoveEmptyEntries), footer.Select(t => t.Value));
        var lastRow = plan.Ops.OfType<Text>().Single(t => t.Value == "1. 化");
        Assert.All(footer, mark => Assert.True(mark.At.Y > lastRow.At.Y, "the marks come after the rows"));
        if (footer.Length > 0) Assert.Single(footer.Select(t => t.At.Y).Distinct()); // one footer line
    }

    [Fact]
    public void Vertical_rows_and_marks_are_hit_regions_spanning_the_content_width()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0, hasPrev: true, hasNext: true,
            [new Candidate("1. ", "化"), new Candidate("2. ", "か [ひらがな]")]);

        var plan = Plan(state, LowerAnchor);

        var rows = plan.Ops.OfType<Text>().Where(t => t.Paint == Paint.Ink).ToArray();
        var select = plan.HitRegions.Where(r => r.Action == HitAction.SelectCandidate).ToArray();
        Assert.Equal([0, 1], select.Select(r => r.Candidate));
        Assert.Equal(new Rect(rows[0].At.X, rows[0].At.Y, plan.CandidateBox!.Value.Width - 2 * CompositionLayout.Padding, LineHeight), select[0].Rect);
        Assert.Equal(new Rect(rows[1].At.X, rows[1].At.Y, select[0].Rect.Width, LineHeight), select[1].Rect);

        var prev = Assert.Single(plan.HitRegions, r => r.Action == HitAction.PreviousPage);
        var next = Assert.Single(plan.HitRegions, r => r.Action == HitAction.NextPage);
        var marks = plan.Ops.OfType<Text>().Where(t => t.Paint == Paint.Muted).ToDictionary(t => t.Value, t => t.At);
        Assert.Equal(new Rect(marks["▲"].X, marks["▲"].Y, CharWidth, LineHeight), prev.Rect);
        Assert.Equal(new Rect(marks["▼"].X, marks["▼"].Y, CharWidth, LineHeight), next.Rect);
        Assert.True(next.Rect.X > prev.Rect.Right);
    }

    [Fact]
    public void Horizontal_cells_and_marks_are_hit_regions_the_width_of_their_text()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.NotSet, selected: 0, hasPrev: true, hasNext: true,
            [new Candidate("1. ", "化"), new Candidate("2. ", "可")]);

        var plan = Plan(state, LowerAnchor);

        var cells = plan.Ops.OfType<Text>().Where(t => t.At.Y < plan.PreeditBox!.Value.Y).ToDictionary(t => t.Value, t => t.At);
        HitRegion[] expected =
        [
            new(new Rect(cells["▲"].X, cells["▲"].Y, CharWidth, LineHeight), HitAction.PreviousPage),
            new(new Rect(cells["1. 化"].X, cells["1. 化"].Y, 4 * CharWidth, LineHeight), HitAction.SelectCandidate, 0),
            new(new Rect(cells["2. 可"].X, cells["2. 可"].Y, 4 * CharWidth, LineHeight), HitAction.SelectCandidate, 1),
            new(new Rect(cells["▼"].X, cells["▼"].Y, CharWidth, LineHeight), HitAction.NextPage),
        ];
        Assert.Equal(expected, plan.HitRegions);
    }

    [Fact]
    public void Aux_lines_are_not_hit_regions()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Underline))), CandidateLayout.Vertical, selected: -1,
            candidates: [new Candidate("", "化")]) with { AuxUp = Aux("[Tabキーで選択]"), AuxDown = Aux("1/3") };

        var plan = Plan(state, LowerAnchor);

        var region = Assert.Single(plan.HitRegions);
        Assert.Equal(HitAction.SelectCandidate, region.Action);
    }

    [Fact]
    public void A_point_is_placed_in_the_preedit_the_candidate_box_or_outside_and_on_its_region()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0, hasNext: true,
            candidates: [new Candidate("1. ", "化"), new Candidate("2. ", "可")]);

        var plan = Plan(state, LowerAnchor);
        var box = plan.CandidateBox!.Value;
        var preedit = plan.PreeditBox!.Value;

        Assert.Equal(HitZone.Preedit, plan.ZoneAt(new Vector2(preedit.X + 1, preedit.Y + 1)));
        Assert.Equal(HitZone.CandidateBox, plan.ZoneAt(new Vector2(box.X + 1, box.Y + 1)));
        Assert.Equal(HitZone.Outside, plan.ZoneAt(new Vector2(box.Right + 1, box.Y + 1)));
        Assert.Equal(HitZone.Outside, plan.ZoneAt(new Vector2(box.Right, box.Y))); // edges are exclusive

        var second = plan.HitRegions.Single(r => r.Candidate == 1);
        Assert.Equal(second, plan.RegionAt(new Vector2(second.Rect.X, second.Rect.Y + LineHeight / 2)));
        Assert.Null(plan.RegionAt(new Vector2(box.X + 1, box.Y + 1))); // the padding
        Assert.Null(plan.RegionAt(new Vector2(preedit.X + 1, preedit.Y + 1)));
    }

    [Fact]
    public void Nothing_drawn_has_no_regions_and_places_every_point_outside()
    {
        var plan = Plan(CompositionState.Idle, LowerAnchor);

        Assert.Empty(plan.HitRegions);
        Assert.Equal(HitZone.Outside, plan.ZoneAt(new Vector2(100, 700)));
        Assert.Null(plan.RegionAt(new Vector2(100, 700)));
    }

    [Fact]
    public void Horizontal_list_puts_the_candidates_side_by_side_with_the_marks_at_either_end()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.NotSet, selected: 0, hasPrev: true, hasNext: true,
            [new Candidate("1. ", "化"), new Candidate("2. ", "可")]);

        var plan = Plan(state, LowerAnchor);

        var cells = plan.Ops.OfType<Text>().Where(t => t.At.Y < plan.PreeditBox!.Value.Y).OrderBy(t => t.At.X).ToArray();
        Assert.Equal(["▲", "1. 化", "2. 可", "▼"], cells.Select(c => c.Value));
        Assert.All(cells, c => Assert.Equal(cells[0].At.Y, c.At.Y));
        Assert.Equal(cells[1].At.X + 4 * CharWidth + CompositionLayout.CellGap, cells[2].At.X);
        Assert.Equal(LineHeight + 2 * CompositionLayout.Padding, plan.CandidateBox!.Value.Height);
    }

    [Fact]
    public void Aux_text_goes_above_and_below_the_rows()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Underline))), CandidateLayout.Vertical, selected: -1,
            candidates: [new Candidate("", "化")]) with { AuxUp = Aux("[Tabキーで選択]"), AuxDown = Aux("1/3") };

        var plan = Plan(state, LowerAnchor);

        var texts = plan.Ops.OfType<Text>().Where(t => t.At.Y < plan.PreeditBox!.Value.Y).OrderBy(t => t.At.Y).ToArray();
        Assert.Equal(["[Tabキーで選択]", "化", "1/3"], texts.Select(t => t.Value));
        Assert.Equal(Paint.Muted, texts[0].Paint);
        Assert.Equal(Paint.Muted, texts[2].Paint);
    }

    [Fact]
    public void Aux_text_alone_still_opens_the_box()
    {
        var state = Composing(PreeditOf(0, new TextSegment("か", TextFormat.Underline))) with { AuxUp = Aux("あ") };

        var plan = Plan(state, LowerAnchor);

        Assert.NotNull(plan.CandidateBox);
    }

    [Fact]
    public void List_is_clamped_to_the_screen()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0,
            candidates: [new Candidate("1. ", "a long candidate that runs off the edge")]);

        var rightEdge = Plan(state, LowerAnchor with { X = 990 });
        Assert.Equal(Screen.X, rightEdge.CandidateBox!.Value.Right);

        // Twenty rows do not fit on either side of a Cursor near the middle: just above it the list goes below and is pushed up, just below it the reverse.
        var tall = WithCandidates(state, CandidateLayout.Vertical, selected: 0, candidates: [.. Enumerable.Range(1, 20).Select(i => new Candidate($"{i}. ", "化"))]);
        var bottomEdge = Plan(tall, UpperAnchor with { Y = 370, TextTop = 372 });
        Assert.Equal(Screen.Y, bottomEdge.CandidateBox!.Value.Bottom);
        var topEdge = Plan(tall, LowerAnchor with { Y = 401, TextTop = 403 });
        Assert.Equal(0, topEdge.CandidateBox!.Value.Y);
    }

    [Fact]
    public void Candidate_box_has_an_opaque_background_under_its_rows()
    {
        var state = WithCandidates(Composing(PreeditOf(0, new TextSegment("か", TextFormat.Highlight))), CandidateLayout.Vertical, selected: 0,
            candidates: [new Candidate("1. ", "化")]);

        var plan = Plan(state, LowerAnchor);

        var panel = Assert.Single(plan.Ops.OfType<Fill>(), f => f.Paint == Paint.Background && f.Rect == plan.CandidateBox);
        var row = plan.Ops.OfType<Text>().Single(t => t.Value == "1. 化");
        Assert.True(plan.Ops.IndexOf(panel) < plan.Ops.IndexOf(row));
    }
}
