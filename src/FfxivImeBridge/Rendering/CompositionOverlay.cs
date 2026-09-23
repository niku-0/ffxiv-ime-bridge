using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FfxivImeBridge.NativeWrite;

namespace FfxivImeBridge.Rendering;

/// <summary>
/// Draws the session's <see cref="Session.ForwardingSession.Composition"/>
/// snapshot — the Preedit inline at the Cursor and the candidate list next to
/// it — on the ImGui foreground list: no window, no focus, local player only.
/// The game's AXIS face through <see cref="OverlayFont"/>, at the Chat Box
/// text's own size (or the config override); the Preedit in the game's IME
/// colour when that reads (ticket 14), its baseline on the Chat Box text's
/// (ticket 15). Main thread (Draw). Reads the cursor and text nodes; writes
/// nothing.
/// </summary>
internal sealed class CompositionOverlay(Bridge bridge, IGameGui gui, OverlayFont font)
{
    private static readonly Vector4 Background = new(0.07f, 0.07f, 0.09f, 1f);
    private static readonly Vector4 Highlight = new(0.22f, 0.42f, 0.8f, 1f);
    private static readonly Vector4 Ink = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Muted = new(0.7f, 0.7f, 0.72f, 1f);

    /// <summary>What the last frame drew, for the <see cref="Capture.MouseGate"/> to hit-test (ticket 16); null while nothing is drawn.</summary>
    public CompositionPlan? LastPlan { get; private set; }

    /// <summary>Where the last frame put the Preedit against the Chat Box's nodes, for the debug tab; null while nothing is drawn.</summary>
    public PreeditPlacement? LastPreedit { get; private set; }

    /// <summary>Once per frame from <c>UiBuilder.Draw</c>; draws nothing unless the Chat Box is focused with a preedit to show.</summary>
    public void Draw()
    {
        LastPlan = null;
        LastPreedit = null;
        if (gui.GameUiHidden || bridge.Session is not { ChatBoxFocused: true } session) return;
        var state = session.Composition;
        if (state.Preedit.IsEmpty || ChatBoxAccess.CursorAnchor(gui) is not { } anchor || ChatBoxAccess.Style(gui) is not { } style) return;

        using var pushed = font.PushFor(style);
        var (imFont, fontSize) = (pushed.Font, pushed.SizePx);
        var chatAscent = pushed.AscentAt(OverlayFontSize.ChatTextPx(style.FontSizePt, style.Scale));
        var metrics = new TextMetrics(fontSize, pushed.Ascent, chatAscent, text => pushed.Measure(text).X);
        var plan = CompositionLayout.Plan(state, anchor, ImGui.GetMainViewport().Size, metrics);
        LastPlan = plan;
        LastPreedit = new PreeditPlacement(anchor.TextTop, anchor.Y, plan.PreeditBox!.Value.Y, metrics.Ascent, chatAscent);
        var preeditInk = style.PreeditInk ?? Ink;

        var draw = ImGui.GetForegroundDrawList();
        foreach (var op in plan.Ops)
        {
            switch (op)
            {
                case Fill fill:
                    draw.AddRectFilled(new Vector2(fill.Rect.X, fill.Rect.Y), new Vector2(fill.Rect.Right, fill.Rect.Bottom), Colour(fill.Paint, preeditInk));
                    break;
                case Text text:
                    draw.AddText(imFont, fontSize, text.At, Colour(text.Paint, preeditInk), text.Value);
                    if (text.Bold) draw.AddText(imFont, fontSize, text.At + new Vector2(1, 0), Colour(text.Paint, preeditInk), text.Value);
                    break;
                case Line line:
                    draw.AddLine(line.From, line.To, Colour(line.Paint, preeditInk), 1f);
                    break;
            }
        }
    }

    /// <summary><see cref="Paint.Preedit"/> is the game's IME colour when readable; the rest are ours.</summary>
    private static uint Colour(Paint paint, Vector4 preeditInk) => ImGui.GetColorU32(paint switch
    {
        Paint.Background => Background,
        Paint.Highlight or Paint.Selected => Highlight,
        Paint.Muted => Muted,
        Paint.Preedit => preeditInk,
        _ => Ink,
    });
}
