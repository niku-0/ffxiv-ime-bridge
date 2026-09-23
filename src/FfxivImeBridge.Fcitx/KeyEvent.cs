namespace FfxivImeBridge.Fcitx;

/// <summary>
/// One key press or release, in fcitx5's terms. <paramref name="KeyCode"/> is
/// the X11 keycode (see <see cref="Fcitx.KeyCode"/>); Mozc needs it and
/// composes nothing when it is 0.
/// </summary>
public readonly record struct KeyEvent(uint KeySym, uint KeyCode, KeyState State = KeyState.None, bool IsRelease = false, uint Time = 0)
{
    public static KeyEvent Press(uint keySym, uint keyCode, KeyState state = KeyState.None) => new(keySym, keyCode, state);
    public static KeyEvent Release(uint keySym, uint keyCode, KeyState state = KeyState.None) => new(keySym, keyCode, state, IsRelease: true);

    /// <summary>A press of an ASCII letter, digit or space on its usual physical key.</summary>
    public static KeyEvent Char(char c, KeyState state = KeyState.None) => new(Fcitx.KeySym.FromChar(c), Fcitx.KeyCode.FromAsciiChar(c), state);

    public KeyEvent AsRelease() => this with { IsRelease = true };
}
