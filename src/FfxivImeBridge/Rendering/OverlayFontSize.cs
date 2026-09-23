namespace FfxivImeBridge.Rendering;

/// <summary>
/// The pixel size the overlay's AXIS handle is built at (ticket 14): the
/// config override as screen pixels when set, else the Chat Box text node's
/// own size at its accumulated HUD scale. Pure; the overlay reads the node.
/// </summary>
internal static class OverlayFontSize
{
    /// <summary>
    /// The text node's <c>FontSize</c> is the game's pt unit — the fdt files are
    /// AXIS_12, AXIS_14, … and AXIS_12 draws 16 px lines at 100 % — so px = pt × 4/3,
    /// the same rule Dalamud's <c>GameFontStyle</c> applies (<c>SizePx = SizePt × 4/3</c>).
    /// </summary>
    private const float PixelsPerPoint = 4f / 3f;

    /// <summary>What the Chat Box uses when its text node cannot be read.</summary>
    private const byte DefaultPt = 12;

    /// <summary>Below this nothing is legible and the atlas would be rebuilt for nothing.</summary>
    public const float MinimumPx = 8f;

    /// <summary>Whole pixels, so the handle is rebuilt on a real change and not on every jitter of a fractional scale.</summary>
    public static float Choose(byte? nodeFontSizePt, float scale, float overridePx)
    {
        var px = overridePx > 0f ? overridePx : ChatTextPx(nodeFontSizePt, scale);
        return Math.Max(MinimumPx, MathF.Round(px));
    }

    /// <summary>The size the game draws the Chat Box text at, in screen pixels, unrounded: what its baseline is derived from (ticket 15).</summary>
    public static float ChatTextPx(byte? nodeFontSizePt, float scale) => (nodeFontSizePt is > 0 and var pt ? pt : DefaultPt) * PixelsPerPoint * scale;
}
