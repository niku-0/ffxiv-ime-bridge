using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using FfxivImeBridge.NativeWrite;
using FfxivImeBridge.Rendering;

namespace FfxivImeBridge;

/// <summary>
/// The single glyph where the game's own input-mode badge sits, while the Chat
/// Box is focused and Forwarding is on: the context's input method, or <c>!</c>
/// while Degraded. The channel label (<c>Say</c>) begins with a full-width space
/// and a space that the game reserves for its badge — no node of the ChatLog
/// draws there (ticket 14's dump) — so the glyph goes into that gap: a little
/// left of the text input's edge, centred on the label, in the label's own
/// font size (never the chat text's or the override); AXIS through
/// <see cref="OverlayFont"/>. Two looks, the config's <see cref="IndicatorStyle"/>:
/// the bare glyph in the label's colours with its edge as an outline, or the
/// Windows client's badge itself — <c>Assets/badge-hiragana.png</c>, cropped
/// from a screenshot of that client (<c>ref/ffxiv-indicator-uld-cropped-2.png</c>),
/// drawn as a texture at its 22×23 px scaled down to about 0.7, times the HUD scale — for <c>あ</c>;
/// any other glyph goes over <c>Assets/badge-frame.png</c>, the same crop
/// with the <c>あ</c> painted out, in the label's edge gold with its edges
/// softened. A badge texture not (yet) loaded falls back to the bare glyph.
/// Without a label it sits left of the text node at the overlay's size, in the
/// Preedit's ink. ImGui foreground list, so only the local player sees it.
/// Hidden for a vanilla look by the config's <c>ShowIndicator</c>. Main thread (Draw).
/// </summary>
internal sealed class Indicator(Bridge bridge, IGameGui gui, ConfigStore config, OverlayFont font, ITextureProvider textures, string assetsDirectory)
{
    private const float Gap = 4f;
    /// <summary>Where the badge starts relative to the text input's text edge, at 100 % (the in-game check asked for it).</summary>
    private const float LeftOfTextEdge = 2f;
    private static readonly Vector4 Ink = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Shadow = new(0f, 0f, 0f, 0.85f);
    private static readonly Vector4 Warning = new(1f, 0.55f, 0.4f, 1f);
    private static readonly Vector2[] Outline = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];

    // The badge textures, and where the glyph goes on the frame: the cream inside the white ring, in texture pixels.
    private const string HiraganaBadge = "badge-hiragana.png";
    private const string FrameBadge = "badge-frame.png";
    private static readonly Rect FrameInterior = new(2f, 2f, 19f, 20f);
    /// <summary>The texture's size on screen at 100 %, as a share of its pixels: the crop read too large in-game, twice by about 20 %.</summary>
    private const float BadgeScale = 1f / 1.50f;
    /// <summary>The glyph's edges softened: copies this far off in each direction under it, at <see cref="BadgeSoftenAlpha"/>.</summary>
    private const float BadgeSoften = 0.5f;
    private const float BadgeSoftenAlpha = 0.3f;

    private readonly string hiraganaBadgePath = Path.Combine(assetsDirectory, HiraganaBadge);
    private readonly string frameBadgePath = Path.Combine(assetsDirectory, FrameBadge);

    /// <summary>The config's say; <c>/imebridge indicator</c> and the settings window write it.</summary>
    public bool Visible => config.Current.ShowIndicator;

    /// <summary>Once per frame from <c>UiBuilder.Draw</c>; draws nothing unless the glyph is due.</summary>
    public void Draw()
    {
        if (!Visible || gui.GameUiHidden || bridge.Session is not { Forwarding: true, ChatBoxFocused: true, IndicatorGlyph: { } glyph }) return;
        // The text node reports hidden while the box is empty, but its position is still valid (ticket 04).
        if (ChatBoxAccess.TextNodeBox(gui) is not { } text || ChatBoxAccess.Style(gui) is not { } style) return;
        var warning = glyph == "!";

        if (ChatBoxAccess.ChannelLabel(gui) is { } label)
        {
            using var pushed = font.PushForIndicator(label.FontSizePt, label.Scale);
            var size = pushed.Measure(glyph);
            var left = text.X - LeftOfTextEdge * label.Scale;
            var centreY = label.Box.Y + label.Box.Height / 2;
            if (config.Current.IndicatorStyle == IndicatorStyle.Badge && BadgeFor(glyph) is { } badge)
                DrawBadge(pushed, badge, left, centreY, glyph, warning ? Warning : label.Edge, label.Scale);
            else
                DrawOutlined(pushed, new Vector2(left, centreY - size.Y / 2), glyph, warning ? Warning : label.Ink, label.Edge);
        }
        else
        {
            using var pushed = font.PushFor(style);
            var size = pushed.Measure(glyph);
            var at = new Vector2(text.X - size.X - Gap, text.Y + (text.Height - size.Y) / 2);
            DrawOutlined(pushed, at, glyph, warning ? Warning : style.PreeditInk ?? Ink, Shadow);
        }
    }

    /// <summary>The glyph over its edge colour at the four neighbouring pixels, as the game edges its labels.</summary>
    private static void DrawOutlined(PushedFont pushed, Vector2 at, string glyph, Vector4 ink, Vector4 edge)
    {
        var draw = ImGui.GetForegroundDrawList();
        var edgeColour = ImGui.GetColorU32(edge);
        foreach (var offset in Outline) draw.AddText(pushed.Font, pushed.SizePx, at + offset, edgeColour, glyph);
        draw.AddText(pushed.Font, pushed.SizePx, at, ImGui.GetColorU32(ink), glyph);
    }

    /// <summary>The badge texture for this frame — the game's own <c>あ</c> badge, or the empty frame for any other glyph — or null while it is not loaded (a shared texture must not be held across frames).</summary>
    private IDalamudTextureWrap? BadgeFor(string glyph) =>
        textures.GetFromFile(glyph == "あ" ? hiraganaBadgePath : frameBadgePath).GetWrapOrDefault();

    /// <summary>
    /// The badge, its left edge at <paramref name="left"/> and centred on
    /// <paramref name="centreY"/>, at the texture's own size times
    /// <see cref="BadgeScale"/> and the HUD scale. The <c>あ</c> badge is complete; on the frame the glyph is drawn
    /// centred in the cream by its ink (the font's own glyph bounds), softened —
    /// four faint half-pixel copies under it — so its edge is not a hard step.
    /// </summary>
    private static void DrawBadge(PushedFont pushed, IDalamudTextureWrap badge, float left, float centreY, string glyph, Vector4 ink, float scale)
    {
        var draw = ImGui.GetForegroundDrawList();
        scale *= BadgeScale;
        var size = badge.Size * scale;
        var min = new Vector2(left, MathF.Round(centreY - size.Y / 2));
        draw.AddImage(badge.Handle, min, min + size);
        if (glyph == "あ") return;

        var interiorMin = min + new Vector2(FrameInterior.X, FrameInterior.Y) * scale;
        var interiorSize = new Vector2(FrameInterior.Width, FrameInterior.Height) * scale;
        var inkBox = pushed.InkOf(glyph);
        // The pen position that centres the ink in the interior.
        var at = interiorMin + (interiorSize - new Vector2(inkBox.Width, inkBox.Height)) / 2 - new Vector2(inkBox.X, inkBox.Y);
        var soft = ImGui.GetColorU32(ink with { W = ink.W * BadgeSoftenAlpha });
        foreach (var direction in Outline) draw.AddText(pushed.Font, pushed.SizePx, at + direction * BadgeSoften, soft, glyph);
        draw.AddText(pushed.Font, pushed.SizePx, at, ImGui.GetColorU32(ink), glyph);
    }
}
