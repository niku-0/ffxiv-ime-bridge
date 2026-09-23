using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Capture;

/// <summary>How the Gate must treat a key (spec "Keyboard capture").</summary>
internal enum KeyClass
{
    /// <summary>Its keysym is the layout-translated character, which only exists at its <c>WM_CHAR</c>: asked there.</summary>
    Printing,
    /// <summary>A non-printing key, or a Ctrl/Alt chord: the keysym is known at the keydown and it is asked there.</summary>
    Fixed,
    /// <summary>Shift, Ctrl, Alt, Win or a lock key on its own: sent, never asked, always passed.</summary>
    Modifier,
    /// <summary><c>WM_DEADCHAR</c>: swallowed silently; the composed character follows as a <c>WM_CHAR</c>.</summary>
    DeadChar,
}

/// <summary>
/// Pure mapping from a Win32 keyboard message to what fcitx5 wants: the
/// <see cref="KeyClass"/> the Gate decides by, and the <see cref="KeyEvent"/>
/// for <c>ProcessKeyEvent</c>. Nothing here decides swallow-or-pass.
/// </summary>
internal static class KeyTranslation
{
    private const int ScRightShift = 0x36;

    /// <summary>
    /// The class of a key-down or key-up message's key, or <see cref="KeyClass.DeadChar"/>.
    /// A release is classified from the modifiers as they stand at the release; that
    /// is only stable for <see cref="KeyClass.Modifier"/> keys — any other release
    /// goes where its press went, and its event is the press's <see cref="KeyEvent.AsRelease"/>.
    /// </summary>
    public static KeyClass Classify(KeyMessage message, Modifiers modifiers)
    {
        switch (message.Kind)
        {
            case KeyMessageKind.DeadChar:
                return KeyClass.DeadChar;
            case KeyMessageKind.KeyDown or KeyMessageKind.KeyUp:
                break;
            default:
                throw new ArgumentException($"{message} has no key to classify", nameof(message));
        }

        var vk = message.VirtualKey;
        if (IsModifierKey(vk)) return KeyClass.Modifier;
        if (!IsTextKey(vk)) return KeyClass.Fixed;

        // AltGr prints (@{}[]\|~€ on a Nordic layout); any other Ctrl/Alt makes a chord with a fixed keysym.
        var held = modifiers.For(message);
        return held.IsChorded && !held.IsAltGr ? KeyClass.Fixed : KeyClass.Printing;
    }

    /// <summary>
    /// The press fcitx5 is asked about at a <c>WM_CHAR</c>: keysym from the code
    /// point, keycode from the char's own lParam, modifiers as read. A control
    /// character's char (Enter's U+000D, Ctrl+V's U+0016) translates too, but the Gate
    /// never asks at one: it goes where its <see cref="KeyClass.Fixed"/> keydown went.
    /// <see langword="null"/> for a surrogate half (see <see cref="KeyMessage.IsSurrogateHalf"/>).
    /// </summary>
    public static KeyEvent? FromChar(KeyMessage message, Modifiers modifiers, uint? time = null)
    {
        if (message.Kind != KeyMessageKind.Char)
            throw new ArgumentException($"{message} is not a char message", nameof(message));
        if (message.IsSurrogateHalf)
            return null;

        return new KeyEvent(
            KeySym.FromCodePoint((int)message.CodePoint),
            KeyCodeOf(message),
            modifiers.For(message).ToKeyState(),
            IsRelease: false,
            time ?? Timestamp());
    }

    /// <summary>
    /// The event for a <see cref="KeyClass.Fixed"/> or <see cref="KeyClass.Modifier"/>
    /// keydown, or a <see cref="KeyClass.Modifier"/> key-up: the X keysym of a
    /// non-printing key, the Latin keysym of a Ctrl/Alt+letter/digit chord
    /// (layout-independent), <see cref="KeySym.VoidSymbol"/> for a chord on a
    /// layout-specific key; keycode from the scancode. Not for a
    /// <see cref="KeyClass.Printing"/> key, whose press comes from its <c>WM_CHAR</c>
    /// (<see cref="FromChar"/>), nor for any other release, which is the stored
    /// press's <see cref="KeyEvent.AsRelease"/> — its modifiers may have changed since.
    /// </summary>
    public static KeyEvent FromKey(KeyMessage message, Modifiers modifiers, uint? time = null)
    {
        var keyClass = Classify(message, modifiers);
        var translatable = keyClass == KeyClass.Modifier || (keyClass == KeyClass.Fixed && message.Kind == KeyMessageKind.KeyDown);
        if (!translatable)
            throw new InvalidOperationException($"{message} is {keyClass}: its keysym is not known at this message");

        var held = modifiers.For(message);
        var upper = held.Shift ^ held.CapsLock;

        return new KeyEvent(
            KeySymOf(message.VirtualKey, message.ScanCode, message.IsExtended, upper),
            KeyCodeOf(message),
            held.ToKeyState(),
            message.Kind == KeyMessageKind.KeyUp,
            time ?? Timestamp());
    }

    /// <summary>
    /// Enter, Escape, Tab, Backspace, Delete, arrows, Home/End/PgUp/PgDn and the
    /// F-keys pass without asking while no Composition is active — unless Ctrl or
    /// Alt is held (Ctrl+Space and the like are always asked). Spec "Keyboard capture".
    /// </summary>
    public static bool SkipsOutsideComposition(KeyMessage message, Modifiers modifiers)
    {
        if (modifiers.For(message).IsChorded)
            return false;

        return message.VirtualKey switch
        {
            VirtualKey.Back or VirtualKey.Tab or VirtualKey.Return or VirtualKey.Escape => true,
            >= VirtualKey.Prior and <= VirtualKey.Down => true, // PgUp, PgDn, End, Home, arrows
            VirtualKey.Delete => true,
            >= VirtualKey.F1 and <= VirtualKey.F24 => true,
            _ => false,
        };
    }

    /// <summary>fcitx5's optional <c>time</c> argument: the tick count, wrapped the way X11 timestamps wrap.</summary>
    public static uint Timestamp() => unchecked((uint)Environment.TickCount);

    /// <summary>Keys whose press puts a character into the Chat Box: <see cref="KeyClass.Printing"/> unless chorded.</summary>
    private static bool IsTextKey(int vk) => vk switch
    {
        VirtualKey.Space => true,
        >= VirtualKey.Digit0 and <= VirtualKey.Digit9 => true,
        >= VirtualKey.A and <= VirtualKey.Z => true,
        >= VirtualKey.Numpad0 and <= VirtualKey.Divide => true, // numpad 0-9 and operators
        >= VirtualKey.Oem1 and <= VirtualKey.Oem3 => true, // ;: =+ ,< -_ .> /? `~ on US
        >= VirtualKey.Oem4 and <= VirtualKey.Oem8 => true, // [{ \| ]} '" and layout-specific
        VirtualKey.Oem102 => true, // the <> key next to left Shift on ISO keyboards
        _ => false,
    };

    private static bool IsModifierKey(int vk) => vk is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu or VirtualKey.Capital
        or VirtualKey.LWin or VirtualKey.RWin or VirtualKey.NumLock or VirtualKey.Scroll or VirtualKey.WineAltGr or (>= VirtualKey.LShift and <= VirtualKey.RMenu);

    private static uint KeyCodeOf(KeyMessage message) => KeyCode.FromWindowsScanCode((uint)message.ScanCode, message.IsExtended);

    private static uint KeySymOf(int vk, int scanCode, bool extended, bool upper) => vk switch
    {
        VirtualKey.Back => KeySym.BackSpace,
        VirtualKey.Tab => KeySym.Tab,
        VirtualKey.Return => extended ? KeySym.KPEnter : KeySym.Return,
        VirtualKey.Pause => KeySym.Pause,
        VirtualKey.Escape => KeySym.Escape,
        VirtualKey.Space => KeySym.Space,
        VirtualKey.Prior => KeySym.PageUp,
        VirtualKey.Next => KeySym.PageDown,
        VirtualKey.End => KeySym.End,
        VirtualKey.Home => KeySym.Home,
        VirtualKey.Left => KeySym.Left,
        VirtualKey.Up => KeySym.Up,
        VirtualKey.Right => KeySym.Right,
        VirtualKey.Down => KeySym.Down,
        VirtualKey.Snapshot => KeySym.Print,
        VirtualKey.Insert => KeySym.Insert,
        VirtualKey.Delete => KeySym.Delete,
        >= VirtualKey.Digit0 and <= VirtualKey.Digit9 => (uint)vk, // '0'..'9' share their ASCII codes
        >= VirtualKey.A and <= VirtualKey.Z => upper ? (uint)vk : (uint)vk + 0x20, // 'A'..'Z' / 'a'..'z'
        VirtualKey.Apps => KeySym.Menu,
        >= VirtualKey.Numpad0 and <= VirtualKey.Numpad9 => KeySym.KeypadDigit(vk - VirtualKey.Numpad0),
        VirtualKey.Multiply => KeySym.KPMultiply,
        VirtualKey.Add => KeySym.KPAdd,
        VirtualKey.Separator => KeySym.KPSeparator,
        VirtualKey.Subtract => KeySym.KPSubtract,
        VirtualKey.Decimal => KeySym.KPDecimal,
        VirtualKey.Divide => KeySym.KPDivide,
        >= VirtualKey.F1 and <= VirtualKey.F24 => KeySym.FunctionKey(vk - VirtualKey.F1 + 1),
        VirtualKey.Shift or VirtualKey.LShift => scanCode == ScRightShift ? KeySym.ShiftR : KeySym.ShiftL, // right Shift is not an extended key
        VirtualKey.RShift => KeySym.ShiftR,
        VirtualKey.Control or VirtualKey.LControl => extended ? KeySym.ControlR : KeySym.ControlL,
        VirtualKey.RControl => KeySym.ControlR,
        VirtualKey.Menu or VirtualKey.LMenu => extended ? KeySym.AltR : KeySym.AltL,
        VirtualKey.RMenu or VirtualKey.WineAltGr => KeySym.AltR,
        VirtualKey.LWin => KeySym.SuperL,
        VirtualKey.RWin => KeySym.SuperR,
        VirtualKey.Capital => KeySym.CapsLock,
        VirtualKey.NumLock => KeySym.NumLock,
        VirtualKey.Scroll => KeySym.ScrollLock,
        _ => KeySym.VoidSymbol,
    };
}
