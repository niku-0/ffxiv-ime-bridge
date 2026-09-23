using FfxivImeBridge.Capture;

namespace FfxivImeBridge.Tests;

/// <summary>Builds keyboard messages the way Windows packs them, for the gate and parser tests.</summary>
internal static class Messages
{
    // lParam layout for keyboard messages: bits 0-15 repeat count, 16-23 scancode,
    // 24 extended, 29 context code (Alt held), 30 previous key state, 31 transition.
    public static nint LParam(int scanCode, bool extended = false, bool alt = false, bool wasDown = false, int repeat = 1)
        => repeat | (scanCode << 16) | (extended ? 1 << 24 : 0) | (alt ? 1 << 29 : 0) | (wasDown ? 1 << 30 : 0);

    public static KeyMessage Down(int vk, int sc, bool repeat = false) => new(WindowMessage.KeyDown, (nuint)vk, LParam(sc, wasDown: repeat));
    public static KeyMessage Up(int vk, int sc) => new(WindowMessage.KeyUp, (nuint)vk, LParam(sc, wasDown: true));
    public static KeyMessage Char(uint cp, int sc) => new(WindowMessage.Char, cp, LParam(sc));
    public static KeyMessage SysDown(int vk, int sc, bool repeat = false) => new(WindowMessage.SysKeyDown, (nuint)vk, LParam(sc, alt: true, wasDown: repeat));
    public static KeyMessage SysUp(int vk, int sc) => new(WindowMessage.SysKeyUp, (nuint)vk, LParam(sc, alt: true, wasDown: true));
    public static KeyMessage SysChar(uint cp, int sc) => new(WindowMessage.SysChar, cp, LParam(sc, alt: true));
}
