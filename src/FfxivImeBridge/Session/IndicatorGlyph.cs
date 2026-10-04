using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Session;

/// <summary>What the Indicator stands for; decides its colour and which badge it is drawn on.</summary>
internal enum IndicatorState
{
    /// <summary>Forwarding is Degraded: a warning.</summary>
    Degraded,
    /// <summary>Mozc in hiragana: the game's own <c>あ</c> badge.</summary>
    Hiragana,
    /// <summary>Any other input method or Mozc mode: a letter on the empty badge.</summary>
    Letter,
}

/// <summary>What the Indicator shows, and the text it draws for it.</summary>
internal readonly record struct IndicatorGlyph(IndicatorState State, string Text)
{
    public static readonly IndicatorGlyph Degraded = new(IndicatorState.Degraded, "!");

    private static readonly IndicatorGlyph Hiragana = new(IndicatorState.Hiragana, "あ");

    /// <summary>
    /// Mozc's modes other than hiragana by the tray icon fcitx5-mozc gives each.
    /// Mozc labels its ASCII modes <c>A</c> and <c>Ａ</c> like direct input, which
    /// look alike once centred on the badge, so they show <c>半</c> and <c>全</c>
    /// for its 半角英数 and 全角英数 instead.
    /// </summary>
    private static readonly Dictionary<string, IndicatorGlyph> MozcModes = new()
    {
        ["fcitx_mozc_direct"] = new(IndicatorState.Letter, "A"),
        ["fcitx_mozc_alpha_half"] = new(IndicatorState.Letter, "半"),
        ["fcitx_mozc_alpha_full"] = new(IndicatorState.Letter, "全"),
        ["fcitx_mozc_katakana_full"] = new(IndicatorState.Letter, "ア"),
        ["fcitx_mozc_katakana_half"] = new(IndicatorState.Letter, "ｱ"),
    };

    /// <summary>
    /// For Mozc its mode by <paramref name="trayIcon"/>, hiragana when that
    /// names none of them; <c>A</c> for a keyboard layout; the input method's
    /// initial otherwise.
    /// </summary>
    public static IndicatorGlyph For(InputMethodInfo inputMethod, string? trayIcon) => inputMethod.UniqueName switch
    {
        "mozc" => trayIcon is not null && MozcModes.TryGetValue(trayIcon, out var mode) ? mode : Hiragana,
        var name when name.StartsWith("keyboard-", StringComparison.Ordinal) => new(IndicatorState.Letter, "A"),
        var name => new(IndicatorState.Letter, name[..1].ToUpperInvariant()),
    };
}
