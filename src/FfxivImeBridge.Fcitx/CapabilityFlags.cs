namespace FfxivImeBridge.Fcitx;

/// <summary>
/// fcitx5 <c>CapabilityFlag</c> values (fcitx-utils/capabilityflags.h). Only the
/// ones relevant to a chat box are listed.
/// </summary>
[Flags]
public enum CapabilityFlags : ulong
{
    None = 0,
    ClientSideUI = 1ul << 0,
    Preedit = 1ul << 1,
    ClientSideControlState = 1ul << 2,
    Password = 1ul << 3,
    FormattedPreedit = 1ul << 4,
    /// <summary>Commit the preedit on focus-out instead of discarding it.</summary>
    ClientUnfocusCommit = 1ul << 5,
    SurroundingText = 1ul << 6,
    NoOnScreenKeyboard = 1ul << 15,
    NoSpellCheck = 1ul << 17,
    /// <summary>Ask fcitx5 to emit <c>CurrentIM</c> right after <c>FocusIn</c>.</summary>
    GetIMInfoOnFocus = 1ul << 23,
    RelativeRect = 1ul << 24,
    KeyEventOrderFix = 1ul << 37,
    ReportKeyRepeat = 1ul << 38,
    /// <summary>fcitx5 hands preedit and candidates to the client via <c>UpdateClientSideUI</c> instead of drawing its own panel.</summary>
    ClientSideInputPanel = 1ul << 39,
    Disable = 1ul << 40,
    CommitStringWithCursor = 1ul << 41,

    /// <summary>What a client that draws its own composition UI wants.</summary>
    ClientDrawsComposition = Preedit | FormattedPreedit | ClientSideInputPanel | GetIMInfoOnFocus | NoOnScreenKeyboard,
}
