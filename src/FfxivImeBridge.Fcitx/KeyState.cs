namespace FfxivImeBridge.Fcitx;

/// <summary>Modifier state bits as fcitx5 defines them (X11 modifier masks plus fcitx's own high bits).</summary>
[Flags]
public enum KeyState : uint
{
    None = 0,
    Shift = 1u << 0,
    CapsLock = 1u << 1,
    Ctrl = 1u << 2,
    Alt = 1u << 3,
    NumLock = 1u << 4,
    Hyper = 1u << 5,
    Super = 1u << 6,
    Meta = 1u << 28,
    /// <summary>Set by fcitx5 on key-release events it forwards back; clients don't need to set it.</summary>
    Release = 1u << 30,
    /// <summary>The key is an auto-repeat; only honoured when the <see cref="CapabilityFlags.ReportKeyRepeat"/> capability is set.</summary>
    Repeat = 1u << 31,
}
