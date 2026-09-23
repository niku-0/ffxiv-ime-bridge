namespace FfxivImeBridge.Fcitx;

/// <summary>
/// X11 keysym values fcitx5 expects in <c>ProcessKeyEvent</c>. Only the keys a
/// chat box can produce are listed; printable characters go through
/// <see cref="FromCodePoint"/>.
/// </summary>
public static class KeySym
{
    public const uint BackSpace = 0xff08;
    public const uint Tab = 0xff09;
    public const uint Return = 0xff0d;
    public const uint Escape = 0xff1b;
    public const uint Delete = 0xffff;
    public const uint Home = 0xff50;
    public const uint Left = 0xff51;
    public const uint Up = 0xff52;
    public const uint Right = 0xff53;
    public const uint Down = 0xff54;
    public const uint PageUp = 0xff55;
    public const uint PageDown = 0xff56;
    public const uint End = 0xff57;
    public const uint Space = 0x0020;

    public const uint ShiftL = 0xffe1;
    public const uint ShiftR = 0xffe2;
    public const uint ControlL = 0xffe3;
    public const uint ControlR = 0xffe4;
    public const uint AltL = 0xffe9;
    public const uint AltR = 0xffea;
    public const uint SuperL = 0xffeb;
    public const uint SuperR = 0xffec;

    public const uint CapsLock = 0xffe5;
    public const uint NumLock = 0xff7f;
    public const uint ScrollLock = 0xff14;

    public const uint Insert = 0xff63;
    public const uint Pause = 0xff13;
    public const uint Print = 0xff61;
    public const uint Menu = 0xff67;
    public const uint KPEnter = 0xff8d;
    public const uint KPMultiply = 0xffaa;
    public const uint KPAdd = 0xffab;
    public const uint KPSeparator = 0xffac;
    public const uint KPSubtract = 0xffad;
    public const uint KPDecimal = 0xffae;
    public const uint KPDivide = 0xffaf;
    public const uint KP0 = 0xffb0;

    public const uint F1 = 0xffbe;
    public const uint F12 = 0xffc9;

    /// <summary>A key with no keysym at all (<c>XK_VoidSymbol</c>); fcitx5 declines it, so it passes.</summary>
    public const uint VoidSymbol = 0xffffff;

    /// <summary><c>XK_F1</c>..<c>XK_F35</c> are contiguous.</summary>
    public static uint FunctionKey(int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, 35);
        return F1 + (uint)(n - 1);
    }

    /// <summary><c>XK_KP_0</c>..<c>XK_KP_9</c> are contiguous.</summary>
    public static uint KeypadDigit(int digit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digit, 9);
        return KP0 + (uint)digit;
    }

    /// <summary>Keysym for a Unicode code point: Latin-1 maps directly, everything else uses the 0x01000000 range.</summary>
    public static uint FromCodePoint(int codePoint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(codePoint);
        return codePoint < 0x100 ? (uint)codePoint : 0x01000000u | (uint)codePoint;
    }

    public static uint FromChar(char c) => FromCodePoint(c);
}
