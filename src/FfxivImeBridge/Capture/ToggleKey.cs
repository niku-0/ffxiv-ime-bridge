using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FfxivImeBridge.Capture;

/// <summary>The modifiers a <see cref="ToggleKey"/> chord can list. AltGr is not one: it prints, so it is a key, not a chord.</summary>
[Flags]
[JsonConverter(typeof(StringEnumConverter))]
internal enum ChordModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
}

/// <summary>
/// The Toggle Key: modifiers plus a PC set-1 scancode, so it names a physical
/// key whatever the layout prints on it (ADR-0004). Matched in the message
/// hook, where the scancode is in every keydown's lParam; an extended key
/// keeps its <c>E0</c> prefix (<see cref="KeyMessage.Set1ScanCode"/>), so Home
/// and Numpad 7 are different chords. A chord with no modifier is unbound: it
/// matches nothing, so a bare letter can never flip Forwarding. Stored as-is
/// in the config file.
/// </summary>
internal readonly record struct ToggleKey(ChordModifiers Modifiers, int ScanCode)
{
    /// <summary>Alt + the key left of 1: <c>§</c> on a Nordic layout, <c>`</c> on US.</summary>
    public static readonly ToggleKey Default = new(ChordModifiers.Alt, KeyboardGate.DefaultToggleScanCode);

    [JsonIgnore]
    public bool IsBound => Modifiers != ChordModifiers.None && ScanCode != 0;

    /// <summary>Every listed modifier held, none of the other three, and the same scancode.</summary>
    public bool Matches(Modifiers held, int scanCode) => IsBound && scanCode == ScanCode && FromHeld(held) == Modifiers;

    /// <summary>The chord's modifiers as they stand in a message's modifier state.</summary>
    public static ChordModifiers FromHeld(Modifiers held)
    {
        var modifiers = ChordModifiers.None;
        if (held.Ctrl) modifiers |= ChordModifiers.Ctrl;
        if (held.Alt) modifiers |= ChordModifiers.Alt;
        if (held.Shift) modifiers |= ChordModifiers.Shift;
        return modifiers;
    }

    /// <summary>
    /// <c>Ctrl+Alt+§</c>: the modifiers in that order, then the key as
    /// <paramref name="keyName"/> names the scancode under the current layout,
    /// falling back to <c>sc 0x29</c> (<c>sc 0xE047</c> extended) for a key
    /// nothing names. Unbound reads as such.
    /// </summary>
    public string Describe(Func<int, string?> keyName)
    {
        if (!IsBound) return Strings.ToggleKeyUnbound;
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ChordModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ChordModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ChordModifiers.Shift)) parts.Add("Shift");
        parts.Add(keyName(ScanCode) is { Length: > 0 } name ? name : $"sc 0x{ScanCode:X2}");
        return string.Join('+', parts);
    }
}
