using System.Text;

namespace FfxivImeBridge.Capture;

/// <summary>The Win32 keyboard window messages (<c>WM_KEYFIRST</c>..<c>WM_KEYLAST</c>).</summary>
internal static class WindowMessage
{
    public const uint KeyDown = 0x0100;
    public const uint KeyUp = 0x0101;
    public const uint Char = 0x0102;
    public const uint DeadChar = 0x0103;
    public const uint SysKeyDown = 0x0104;
    public const uint SysKeyUp = 0x0105;
    public const uint SysChar = 0x0106;
    public const uint SysDeadChar = 0x0107;
    public const uint UniChar = 0x0109;

    public static bool IsKeyboard(uint message) => message is >= KeyDown and <= UniChar;
}

/// <summary>The Win32 virtual-key codes the plugin tells apart. Layout-specific (<c>VK_OEM_*</c>) keys are matched by range where needed.</summary>
internal static class VirtualKey
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int Pause = 0x13;
    public const int Capital = 0x14;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Prior = 0x21;
    public const int Next = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Snapshot = 0x2C;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int Digit0 = 0x30;
    public const int Digit9 = 0x39;
    public const int A = 0x41;
    public const int Z = 0x5A;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int Apps = 0x5D;
    public const int Numpad0 = 0x60;
    public const int Numpad9 = 0x69;
    public const int Multiply = 0x6A;
    public const int Add = 0x6B;
    public const int Separator = 0x6C;
    public const int Subtract = 0x6D;
    public const int Decimal = 0x6E;
    public const int Divide = 0x6F;
    public const int F1 = 0x70;
    public const int F24 = 0x87;
    public const int NumLock = 0x90;
    public const int Scroll = 0x91;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
    public const int Oem1 = 0xBA;
    public const int Oem3 = 0xC0;
    public const int Oem4 = 0xDB;
    public const int Oem8 = 0xDF;
    public const int Oem102 = 0xE2;
    /// <summary>What Wine posts for AltGr (X11 <c>ISO_Level3_Shift</c>) instead of <see cref="RMenu"/>: <c>vk=0xE4 sc=0x38 ext</c> on a Nordic layout (ticket 07's trace).</summary>
    public const int WineAltGr = 0xE4;
}

internal enum KeyMessageKind
{
    NotKeyboard,
    KeyDown,
    KeyUp,
    /// <summary><c>WM_CHAR</c>, <c>WM_SYSCHAR</c> or <c>WM_UNICHAR</c>: the layout-translated character a press produced.</summary>
    Char,
    DeadChar,
}

/// <summary>
/// A window message as the game's message pump sees it, decoded the way
/// <c>WM_KEYDOWN</c>/<c>WM_CHAR</c> pack their parameters. Pure: no Win32 calls.
/// </summary>
internal readonly record struct KeyMessage(uint Message, nuint WParam, nint LParam)
{
    public KeyMessageKind Kind => Message switch
    {
        WindowMessage.KeyDown or WindowMessage.SysKeyDown => KeyMessageKind.KeyDown,
        WindowMessage.KeyUp or WindowMessage.SysKeyUp => KeyMessageKind.KeyUp,
        WindowMessage.Char or WindowMessage.SysChar or WindowMessage.UniChar => KeyMessageKind.Char,
        WindowMessage.DeadChar or WindowMessage.SysDeadChar => KeyMessageKind.DeadChar,
        _ => KeyMessageKind.NotKeyboard,
    };

    /// <summary><c>WM_SYS*</c>: Alt was held, or no window had focus.</summary>
    public bool IsSystem => Message is WindowMessage.SysKeyDown or WindowMessage.SysKeyUp or WindowMessage.SysChar or WindowMessage.SysDeadChar;

    /// <summary>Virtual-key code of a key-down/up message.</summary>
    public int VirtualKey => (int)(WParam & 0xFF);

    /// <summary>Code point of a char message (UTF-16 unit for <c>WM_CHAR</c>, UTF-32 for <c>WM_UNICHAR</c>).</summary>
    public uint CodePoint => (uint)WParam;

    /// <summary>
    /// A <c>WM_CHAR</c> carrying half of a UTF-16 surrogate pair. No Windows layout
    /// under Wine produces one, and a half cannot be asked or passed alone: the
    /// Gate logs it and passes it, with no pairing logic.
    /// </summary>
    public bool IsSurrogateHalf => Message is (WindowMessage.Char or WindowMessage.SysChar) && WParam is >= 0xD800 and <= 0xDFFF;

    public int RepeatCount => (int)(LParam & 0xFFFF);

    /// <summary>PC set-1 scancode, bits 16–23. What <c>KeyCode.FromWindowsScanCode</c> wants.</summary>
    public int ScanCode => (int)((LParam >> 16) & 0xFF);

    public bool IsExtended => (LParam & (1L << 24)) != 0;

    /// <summary>
    /// The full set-1 scancode: <see cref="ScanCode"/>, with the <c>E0</c> prefix an
    /// extended key carries (<c>0xE047</c> for Home, <c>0x47</c> for Numpad 7).
    /// What a <see cref="ToggleKey"/> stores, so the twins are different chords.
    /// </summary>
    public int Set1ScanCode => IsExtended ? 0xE000 | ScanCode : ScanCode;

    /// <summary>Context code: Alt was down when the message was generated.</summary>
    public bool AltHeld => (LParam & (1L << 29)) != 0;

    /// <summary>Previous key state was down; on a key-down message this is auto-repeat.</summary>
    public bool IsRepeat => (LParam & (1L << 30)) != 0;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Message switch
        {
            WindowMessage.KeyDown => "KEYDOWN",
            WindowMessage.KeyUp => "KEYUP",
            WindowMessage.Char => "CHAR",
            WindowMessage.DeadChar => "DEADCHAR",
            WindowMessage.SysKeyDown => "SYSKEYDOWN",
            WindowMessage.SysKeyUp => "SYSKEYUP",
            WindowMessage.SysChar => "SYSCHAR",
            WindowMessage.SysDeadChar => "SYSDEADCHAR",
            WindowMessage.UniChar => "UNICHAR",
            _ => $"WM_0x{Message:X4}",
        });

        switch (Kind)
        {
            case KeyMessageKind.KeyDown or KeyMessageKind.KeyUp:
                sb.Append($" vk=0x{VirtualKey:X2}");
                break;
            case KeyMessageKind.Char or KeyMessageKind.DeadChar:
                sb.Append($" U+{CodePoint:X4}");
                if (CodePoint >= 0x20 && CodePoint != 0x7F && Rune.IsValid(CodePoint))
                    sb.Append($" '{new Rune(CodePoint)}'");
                break;
            default:
                return sb.ToString();
        }

        sb.Append($" sc=0x{ScanCode:X2}");
        if (IsExtended) sb.Append(" ext");
        if (AltHeld) sb.Append(" alt");
        if (IsRepeat && Kind == KeyMessageKind.KeyDown) sb.Append(" repeat");
        return sb.ToString();
    }
}
