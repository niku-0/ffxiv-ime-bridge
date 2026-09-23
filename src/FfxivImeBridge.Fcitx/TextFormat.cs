namespace FfxivImeBridge.Fcitx;

/// <summary>Per-segment preedit formatting (fcitx-utils/textformatflags.h).</summary>
[Flags]
public enum TextFormat
{
    None = 0,
    Underline = 1 << 3,
    /// <summary>The segment currently being converted; draw it selected.</summary>
    Highlight = 1 << 4,
    DontCommit = 1 << 5,
    Bold = 1 << 6,
    Strike = 1 << 7,
    Italic = 1 << 8,
}
