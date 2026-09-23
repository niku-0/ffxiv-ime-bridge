using System.Numerics;

namespace FfxivImeBridge.Capture;

/// <summary>The Win32 mouse window messages (<c>WM_MOUSEFIRST</c>..<c>WM_MOUSELAST</c>).</summary>
internal static class MouseWindowMessage
{
    public const uint MouseMove = 0x0200;
    public const uint LButtonDown = 0x0201;
    public const uint LButtonUp = 0x0202;
    public const uint LButtonDblClk = 0x0203;
    public const uint RButtonDown = 0x0204;
    public const uint RButtonUp = 0x0205;
    public const uint RButtonDblClk = 0x0206;
    public const uint MButtonDown = 0x0207;
    public const uint MButtonUp = 0x0208;
    public const uint MButtonDblClk = 0x0209;
    public const uint MouseWheel = 0x020A;
    public const uint XButtonDown = 0x020B;
    public const uint XButtonUp = 0x020C;
    public const uint XButtonDblClk = 0x020D;
    public const uint MouseHWheel = 0x020E;

    /// <summary>What the hook hands the <see cref="MouseGate"/>: the buttons and the wheel. Never a move (ticket 16), nor the horizontal wheel.</summary>
    public static bool IsFiltered(uint message) => message is >= LButtonDown and <= XButtonDblClk;
}

internal enum MouseMessageKind
{
    NotMouse,
    /// <summary>A button down — or a double-click, which Windows posts in place of the second down.</summary>
    Press,
    Release,
    Wheel,
}

/// <summary>The X buttons are one to the gate: which of the two is in the high word of <c>wParam</c> and does not matter.</summary>
internal enum MouseButton
{
    Left,
    Right,
    Middle,
    X,
}

/// <summary>
/// A mouse message as the game's message pump sees it, with its point in
/// client pixels — the overlay's coordinates. A button message's point is
/// <c>lParam</c>'s; the wheel's is in screen pixels on the wire and the hook
/// converts it (<see cref="PointOf"/> only unpacks). Pure: no Win32 calls.
/// </summary>
internal readonly record struct MouseMessage(uint Message, nuint WParam, Vector2 Point)
{
    public MouseMessageKind Kind => Message switch
    {
        MouseWindowMessage.LButtonDown or MouseWindowMessage.RButtonDown or MouseWindowMessage.MButtonDown or MouseWindowMessage.XButtonDown
            or MouseWindowMessage.LButtonDblClk or MouseWindowMessage.RButtonDblClk or MouseWindowMessage.MButtonDblClk or MouseWindowMessage.XButtonDblClk => MouseMessageKind.Press,
        MouseWindowMessage.LButtonUp or MouseWindowMessage.RButtonUp or MouseWindowMessage.MButtonUp or MouseWindowMessage.XButtonUp => MouseMessageKind.Release,
        MouseWindowMessage.MouseWheel => MouseMessageKind.Wheel,
        _ => MouseMessageKind.NotMouse,
    };

    /// <summary>The button of a press or release; <see cref="MouseButton.Left"/> for anything else.</summary>
    public MouseButton Button => Message switch
    {
        MouseWindowMessage.RButtonDown or MouseWindowMessage.RButtonUp or MouseWindowMessage.RButtonDblClk => MouseButton.Right,
        MouseWindowMessage.MButtonDown or MouseWindowMessage.MButtonUp or MouseWindowMessage.MButtonDblClk => MouseButton.Middle,
        MouseWindowMessage.XButtonDown or MouseWindowMessage.XButtonUp or MouseWindowMessage.XButtonDblClk => MouseButton.X,
        _ => MouseButton.Left,
    };

    /// <summary>The wheel's travel, in the high word of <c>wParam</c>: positive away from the user (up), a notch is 120.</summary>
    public int WheelDelta => (short)((WParam >> 16) & 0xFFFF);

    /// <summary>The signed x and y words a mouse message's <c>lParam</c> packs.</summary>
    public static Vector2 PointOf(nint lParam) => new((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

    public override string ToString()
    {
        var name = Message switch
        {
            MouseWindowMessage.LButtonDown => "LBUTTONDOWN",
            MouseWindowMessage.LButtonUp => "LBUTTONUP",
            MouseWindowMessage.LButtonDblClk => "LBUTTONDBLCLK",
            MouseWindowMessage.RButtonDown => "RBUTTONDOWN",
            MouseWindowMessage.RButtonUp => "RBUTTONUP",
            MouseWindowMessage.RButtonDblClk => "RBUTTONDBLCLK",
            MouseWindowMessage.MButtonDown => "MBUTTONDOWN",
            MouseWindowMessage.MButtonUp => "MBUTTONUP",
            MouseWindowMessage.MButtonDblClk => "MBUTTONDBLCLK",
            MouseWindowMessage.MouseWheel => "MOUSEWHEEL",
            MouseWindowMessage.XButtonDown => "XBUTTONDOWN",
            MouseWindowMessage.XButtonUp => "XBUTTONUP",
            MouseWindowMessage.XButtonDblClk => "XBUTTONDBLCLK",
            _ => $"WM_0x{Message:X4}",
        };
        var delta = Kind == MouseMessageKind.Wheel ? $" {WheelDelta:+#;-#;0}" : "";
        return $"{name}{delta} ({Point.X:0},{Point.Y:0})";
    }
}
