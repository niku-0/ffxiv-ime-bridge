using System.Numerics;

namespace FfxivImeBridge.NativeWrite;

/// <summary>
/// The text input's own styling as the overlay borrows it (ticket 14): the
/// text node's font and the accumulated scale it is drawn at (its own times
/// every ancestor's: the HUD scale), and the colours the game keeps for its own
/// (Windows) IME on the component's ULD data and the addon's handler values.
/// Colours are the game's <c>ByteColor</c> as one <c>uint</c>: its <c>R</c>,
/// <c>G</c>, <c>B</c>, <c>A</c> bytes overlay <c>RGBA</c> at offsets 0–3, so R
/// is the low byte and A the high one (ImGui's own layout).
/// </summary>
internal readonly record struct ChatBoxStyle(byte FontSizePt, byte FontType, float Scale, uint ImeColor, uint? HandlerImeColor, uint CandidateColor)
{
    /// <summary>Alpha under this and the colour is a hint, not ink.</summary>
    private const int MinAlpha = 0x80;
    /// <summary>The Preedit sits on a near-black ground; ink darker than this vanishes into it.</summary>
    private const int MinBrightness = 0x60;

    /// <summary>The handler's value when the addon set one (as the limits: ticket 04), else the ULD data's.</summary>
    public uint ImeColorInUse => HandlerImeColor is > 0 and var handler ? handler : ImeColor;

    /// <summary>The game's IME colour as the Preedit's ink, or null when it would not read on our ground (the overlay keeps its white).</summary>
    public Vector4? PreeditInk => Readable(ImeColorInUse) ? ToVector(ImeColorInUse) : null;

    private static bool Readable(uint rgba)
    {
        var (r, g, b, a) = Channels(rgba);
        return a >= MinAlpha && Math.Max(r, Math.Max(g, b)) >= MinBrightness;
    }

    internal static Vector4 ToVector(uint rgba)
    {
        var (r, g, b, a) = Channels(rgba);
        return new Vector4(r, g, b, a) / 255f;
    }

    private static (byte R, byte G, byte B, byte A) Channels(uint rgba) =>
        ((byte)rgba, (byte)(rgba >> 8), (byte)(rgba >> 16), (byte)(rgba >> 24));

    public override string ToString() =>
        $"font size={FontSizePt}pt type={FontType} scale={Scale:0.###} ime colour uld=#{ImeColor:X8} handler={(HandlerImeColor is { } h ? $"#{h:X8}" : "-")} candidate colour=#{CandidateColor:X8}";
}

/// <summary>
/// The channel label as the Indicator borrows it (ticket 14): its box, its font
/// size at its accumulated scale, and its text and edge colours (the game's
/// <c>ByteColor</c> as in <see cref="ChatBoxStyle"/>). On the reference client
/// AXIS 14 pt, white with a golden edge.
/// </summary>
internal readonly record struct ChannelLabel(ScreenBox Box, byte FontSizePt, float Scale, uint TextColor, uint EdgeColor)
{
    public Vector4 Ink => ChatBoxStyle.ToVector(TextColor);
    public Vector4 Edge => ChatBoxStyle.ToVector(EdgeColor);
}
