using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Capture;

/// <summary>
/// The modifier keys as they stood when a message was posted. Read by an
/// <see cref="IKeyStateReader"/> right inside the pump, where Win32's
/// message-synchronous key state is still that of the message in hand.
/// </summary>
internal readonly record struct Modifiers
{
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    /// <summary>Right Alt on its own: how Wine may report AltGr instead of Windows' Ctrl+Alt.</summary>
    public bool RightAlt { get; init; }
    public bool CapsLock { get; init; }
    public bool NumLock { get; init; }

    /// <summary>AltGr: Ctrl and Alt together (Windows) or right Alt alone (Wine). Prints on a Nordic layout, so not a chord.</summary>
    public bool IsAltGr => (Ctrl && Alt) || RightAlt;

    /// <summary>Ctrl or Alt is held, AltGr included: the key is a chord, or a printing key that is always asked.</summary>
    public bool IsChorded => Ctrl || Alt || RightAlt;

    /// <summary>The same state with the Alt the message itself records (the <c>WM_SYS*</c> context code) folded in.</summary>
    public Modifiers For(KeyMessage message) => message.AltHeld ? this with { Alt = true } : this;

    /// <summary>The same state in fcitx5's bits (X11 modifier masks).</summary>
    public KeyState ToKeyState()
    {
        var state = KeyState.None;
        if (Shift) state |= KeyState.Shift;
        if (Ctrl) state |= KeyState.Ctrl;
        if (Alt || RightAlt) state |= KeyState.Alt;
        if (CapsLock) state |= KeyState.CapsLock;
        if (NumLock) state |= KeyState.NumLock;
        return state;
    }
}
