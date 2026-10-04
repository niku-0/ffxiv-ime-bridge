using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Session;

/// <summary>What the Indicator stands for; decides its colour and which badge it is drawn on.</summary>
internal enum IndicatorState
{
    /// <summary>Forwarding is Degraded: a warning.</summary>
    Degraded,
    /// <summary>Mozc: the game's own <c>あ</c> badge.</summary>
    Hiragana,
    /// <summary>Any other input method: a letter on the empty badge.</summary>
    Letter,
}

/// <summary>What the Indicator shows, and the text it draws for it.</summary>
internal readonly record struct IndicatorGlyph(IndicatorState State, string Text)
{
    public static readonly IndicatorGlyph Degraded = new(IndicatorState.Degraded, "!");

    /// <summary><c>あ</c> for Mozc, <c>A</c> for a keyboard layout, the input method's initial otherwise.</summary>
    public static IndicatorGlyph For(InputMethodInfo inputMethod) => inputMethod.UniqueName switch
    {
        "mozc" => new(IndicatorState.Hiragana, "あ"),
        var name when name.StartsWith("keyboard-", StringComparison.Ordinal) => new(IndicatorState.Letter, "A"),
        var name => new(IndicatorState.Letter, name[..1].ToUpperInvariant()),
    };
}
