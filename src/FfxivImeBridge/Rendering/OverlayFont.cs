using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin.Services;
using FfxivImeBridge.NativeWrite;

namespace FfxivImeBridge.Rendering;

/// <summary>
/// The AXIS handles the overlay draws with (ticket 14), on an atlas of the
/// plugin's own that Dalamud's global scale leaves alone, so a requested pixel
/// size is the size on screen: one for the Preedit and the candidates at the
/// Chat Box text's size, one for the Indicator at the channel label's. A new
/// size builds a second handle asynchronously while the current one keeps
/// drawing, scaled by ImGui to the wanted size; the new one takes over once it
/// is available. Only a slot's very first build has nothing to fall back on
/// but the font ImGui has current. Main thread (Draw).
/// </summary>
internal sealed class OverlayFont : IDisposable
{
    private readonly IFontAtlas atlas;
    private readonly ConfigStore config;
    private readonly Slot overlay;
    private readonly Slot indicator;

    public OverlayFont(IUiBuilder ui, ConfigStore config, IPluginLog log)
    {
        this.config = config;
        atlas = ui.CreateFontAtlas(FontAtlasAutoRebuildMode.Async, isGlobalScaled: false, "FfxivImeBridge overlay");
        overlay = new Slot(atlas, log);
        indicator = new Slot(atlas, log);
    }

    /// <summary>
    /// Pushes the overlay's handle for the Chat Box's style — its text size at
    /// its HUD scale, or the config override — and hands back what a draw
    /// needs: the font, the pixel size to draw it at, and a measurer. Dispose to pop.
    /// </summary>
    public PushedFont PushFor(ChatBoxStyle style) =>
        Push(overlay, OverlayFontSize.Choose(style.FontSizePt, style.Scale, config.Current.FontSizeOverride));

    /// <summary>The Indicator's handle at the channel label's size: the label's, not the chat text's, and never the override.</summary>
    public PushedFont PushForIndicator(byte labelFontSizePt, float labelScale) =>
        Push(indicator, OverlayFontSize.Choose(labelFontSizePt, labelScale, overridePx: 0f));

    private static PushedFont Push(Slot slot, float sizePx)
    {
        var pop = slot.At(sizePx).Push();
        return new PushedFont(pop, ImGui.GetFont(), sizePx);
    }

    /// <summary>Handles before the atlas: a handle outliving its atlas is what Dalamud warns about.</summary>
    public void Dispose()
    {
        overlay.Dispose();
        indicator.Dispose();
        atlas.Dispose();
    }

    /// <summary>One size's worth of handle: the one drawing and, after a size change, the one building to replace it.</summary>
    private sealed class Slot(IFontAtlas atlas, IPluginLog log) : IDisposable
    {
        private IFontHandle? current;
        private float currentPx;
        private IFontHandle? pending;
        private float pendingPx;

        /// <summary>
        /// The handle to draw with at <paramref name="wantedPx"/>: the current one,
        /// or the first ever while it builds. A landed build takes over if its size
        /// is still wanted and goes if the size moved on meanwhile; one build runs
        /// at a time, so a dragged slider costs one rebuild per landing.
        /// </summary>
        public IFontHandle At(float wantedPx)
        {
            if (pending is not null && (pending.Available || pending.LoadException is not null))
            {
                if (pendingPx == wantedPx || current is null)
                {
                    current?.Dispose();
                    current = pending;
                    currentPx = pendingPx;
                }
                else pending.Dispose();
                pending = null;
            }

            if (pending is null && (current is null || wantedPx != currentPx))
            {
                pending = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, wantedPx));
                pendingPx = wantedPx;
                _ = atlas.BuildFontsAsync().ContinueWith(
                    t => log.Warning(t.Exception!, "Overlay font: building AXIS at {Size} px failed", wantedPx),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            return current ?? pending!;
        }

        public void Dispose()
        {
            pending?.Dispose();
            current?.Dispose();
        }
    }
}

/// <summary>A pushed overlay font for one draw: the font, the pixel size to draw it at, and the measure of a run at that size. Dispose pops it.</summary>
internal readonly struct PushedFont(IDisposable pop, ImFontPtr font, float sizePx) : IDisposable
{
    public ImFontPtr Font => font;
    public float SizePx => sizePx;

    public Vector2 Measure(string text) => ImGui.CalcTextSizeA(font, sizePx, float.MaxValue, 0f, text, out _);

    /// <summary>The baseline below the top of a line drawn at <see cref="SizePx"/>.</summary>
    public float Ascent => AscentAt(sizePx);

    /// <summary>
    /// The baseline below the top of a line drawn at <paramref name="atPx"/>: the
    /// font's ascent scaled from its built size. For an AXIS handle this is the
    /// fdt's own ascent, so at the Chat Box text's size it is where the game
    /// draws that text's baseline (ticket 15).
    /// </summary>
    public float AscentAt(float atPx) => font.Ascent * atPx / font.FontSize;

    /// <summary>
    /// The ink of <paramref name="text"/>'s first character, relative to the pen
    /// position text is drawn at: the glyph's own bounds, scaled from the font's
    /// built size to <see cref="SizePx"/>. The advance box has air around the ink
    /// that a tight frame must not inherit.
    /// </summary>
    public unsafe Rect InkOf(string text)
    {
        var glyph = font.FindGlyph(text.Length > 0 ? text[0] : ' ');
        var scale = sizePx / font.FontSize;
        return new Rect(glyph->X0 * scale, glyph->Y0 * scale, (glyph->X1 - glyph->X0) * scale, (glyph->Y1 - glyph->Y0) * scale);
    }

    public void Dispose() => pop.Dispose();
}
