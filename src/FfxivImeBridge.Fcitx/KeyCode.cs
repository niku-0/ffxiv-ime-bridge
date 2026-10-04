namespace FfxivImeBridge.Fcitx;

/// <summary>
/// X11 keycodes for <see cref="KeyEvent.KeyCode"/>. Mozc silently composes
/// nothing when the keycode is 0, so every key event needs a real one.
/// X keycodes are Linux evdev codes + 8, and for the main keyboard block evdev
/// codes equal PC set-1 scancodes, which is what Windows (and Wine) report in
/// <c>WM_KEYDOWN</c>'s lParam.
/// </summary>
public static class KeyCode
{
    private const uint EvdevOffset = 8;

    /// <summary>
    /// X keycode for a PC set-1 scancode as Windows reports it (bits 16–23 of
    /// <c>WM_KEYDOWN</c> lParam, <paramref name="extended"/> = bit 24). Returns 0
    /// for scancodes with no evdev equivalent.
    /// </summary>
    public static uint FromWindowsScanCode(uint scanCode, bool extended)
    {
        if (!extended)
            return scanCode is > 0 and < 0x80 ? scanCode + EvdevOffset : 0;

        // E0-prefixed keys: the evdev code is unrelated to the scancode.
        var evdev = scanCode switch
        {
            0x1c => 96u,  // KP_Enter
            0x1d => 97u,  // right ctrl
            0x35 => 98u,  // KP_Divide
            0x38 => 100u, // right alt
            0x47 => 102u, // Home
            0x48 => 103u, // Up
            0x49 => 104u, // PageUp
            0x4b => 105u, // Left
            0x4d => 106u, // Right
            0x4f => 107u, // End
            0x50 => 108u, // Down
            0x51 => 109u, // PageDown
            0x52 => 110u, // Insert
            0x53 => 111u, // Delete
            0x5b => 125u, // left meta
            0x5c => 126u, // right meta
            0x5d => 127u, // Menu/compose
            _ => 0u,
        };
        return evdev == 0 ? 0 : evdev + EvdevOffset;
    }

    /// <summary>
    /// X keycode of the key that produces <paramref name="c"/> on a QWERTY
    /// physical layout (letters, digits, space). For tests and for callers that
    /// only have a character; the plugin uses real scancodes instead.
    /// </summary>
    public static uint FromAsciiChar(char c)
    {
        var evdev = char.ToLowerInvariant(c) switch
        {
            'q' => 16u, 'w' => 17u, 'e' => 18u, 'r' => 19u, 't' => 20u, 'y' => 21u, 'u' => 22u, 'i' => 23u, 'o' => 24u, 'p' => 25u,
            'a' => 30u, 's' => 31u, 'd' => 32u, 'f' => 33u, 'g' => 34u, 'h' => 35u, 'j' => 36u, 'k' => 37u, 'l' => 38u,
            'z' => 44u, 'x' => 45u, 'c' => 46u, 'v' => 47u, 'b' => 48u, 'n' => 49u, 'm' => 50u,
            '1' => 2u, '2' => 3u, '3' => 4u, '4' => 5u, '5' => 6u, '6' => 7u, '7' => 8u, '8' => 9u, '9' => 10u, '0' => 11u,
            ' ' => 57u,
            _ => 0u,
        };
        return evdev == 0 ? 0 : evdev + EvdevOffset;
    }
}
