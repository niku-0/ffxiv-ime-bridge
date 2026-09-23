using System.Collections.Immutable;
using System.Numerics;

namespace FfxivImeBridge.Rendering;

/// <summary>
/// The Cursor on screen: the cursor node's top-left, its drawn height, the UI
/// scale it is drawn at, and the top of the Chat Box text's line (the text
/// node's, which the game draws its text down from — ticket 15).
/// </summary>
internal readonly record struct CursorAnchor(float X, float Y, float Height, float Scale, float TextTop);

/// <summary>An axis-aligned box in screen pixels.</summary>
internal readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    /// <summary>Left and top edges in, right and bottom out, as pixel rects go.</summary>
    public bool Contains(Vector2 point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
}

/// <summary>What a draw op stands for; the overlay picks the colour.</summary>
internal enum Paint
{
    /// <summary>Opaque ground under the preedit and the candidate box.</summary>
    Background,
    /// <summary>The segment being converted (<see cref="Fcitx.TextFormat.Highlight"/>).</summary>
    Highlight,
    /// <summary>Candidate text.</summary>
    Ink,
    /// <summary>The Preedit's text, underlines and strikes: the game's IME colour when it reads, else <see cref="Ink"/>.</summary>
    Preedit,
    /// <summary>The preedit cursor bar.</summary>
    Cursor,
    /// <summary>The selected candidate's row.</summary>
    Selected,
    /// <summary>Aux text and paging marks.</summary>
    Muted,
}

internal abstract record DrawOp;

internal sealed record Fill(Rect Rect, Paint Paint) : DrawOp;

/// <summary>Text with its top-left at <paramref name="At"/>; <paramref name="Bold"/> is faux (drawn twice, 1 px apart).</summary>
internal sealed record Text(Vector2 At, string Value, Paint Paint, bool Bold) : DrawOp;

/// <summary>A 1 px line.</summary>
internal sealed record Line(Vector2 From, Vector2 To, Paint Paint) : DrawOp;

/// <summary>
/// How the overlay's font measures: its line height, its ascent (the baseline
/// below a line's top) and the width of a run, all at the drawn size — and
/// <paramref name="ChatAscent"/>, the Chat Box text's own ascent, AXIS at the
/// text node's size: the same as <paramref name="Ascent"/> unless the size
/// override puts the overlay at another size (ticket 15).
/// </summary>
internal readonly record struct TextMetrics(float LineHeight, float Ascent, float ChatAscent, Func<string, float> WidthOf);

/// <summary>
/// The Preedit's vertical placement in one frame, in screen pixels (ticket 15):
/// the text node's top, the cursor node's top, the Preedit's top, its ascent
/// and the chat text's. Its baseline is <see cref="Top"/> + <see cref="Ascent"/>,
/// meant to equal <see cref="TextTop"/> + <see cref="ChatAscent"/>.
/// </summary>
internal readonly record struct PreeditPlacement(float TextTop, float CursorTop, float Top, float Ascent, float ChatAscent)
{
    public float Baseline => Top + Ascent;

    public override string ToString() =>
        $"text node top={TextTop:0.#} cursor node top={CursorTop:0.#}; preedit top={Top:0.#} baseline={Baseline:0.#} (ascent={Ascent:0.##}, chat text ascent={ChatAscent:0.##})";
}

/// <summary>What a left press on a spot of the candidate box does (ticket 16).</summary>
internal enum HitAction
{
    /// <summary>A candidate row or cell: <c>SelectCandidate(<see cref="HitRegion.Candidate"/>)</c>.</summary>
    SelectCandidate,
    /// <summary>The <c>▲</c> mark.</summary>
    PreviousPage,
    /// <summary>The <c>▼</c> mark.</summary>
    NextPage,
}

/// <summary>A clickable spot of the candidate box: its rect on screen and what a left press there does; <paramref name="Candidate"/> is the index for <see cref="HitAction.SelectCandidate"/>.</summary>
internal readonly record struct HitRegion(Rect Rect, HitAction Action, int Candidate = -1);

/// <summary>Where a point falls on what a frame drew (ticket 16).</summary>
internal enum HitZone
{
    Outside,
    Preedit,
    CandidateBox,
}

/// <summary>
/// What one frame draws, in order, the two boxes it occupies (null when not
/// drawn) and the clickable spots inside the candidate box, in drawing order.
/// The <see cref="Capture.MouseGate"/> hit-tests the last frame's plan.
/// </summary>
internal sealed record CompositionPlan(ImmutableArray<DrawOp> Ops, Rect? PreeditBox, Rect? CandidateBox, ImmutableArray<HitRegion> HitRegions)
{
    public static readonly CompositionPlan Nothing = new([], null, null, []);

    /// <summary>The candidate box wins where the two overlap (it is drawn over the preedit).</summary>
    public HitZone ZoneAt(Vector2 point) =>
        CandidateBox is { } box && box.Contains(point) ? HitZone.CandidateBox
        : PreeditBox is { } preedit && preedit.Contains(point) ? HitZone.Preedit
        : HitZone.Outside;

    /// <summary>The region under the point, or null over the padding, the aux lines or outside the box.</summary>
    public HitRegion? RegionAt(Vector2 point)
    {
        foreach (var region in HitRegions)
        {
            if (region.Rect.Contains(point)) return region;
        }
        return null;
    }
}
