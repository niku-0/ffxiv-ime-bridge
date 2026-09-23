using System.Runtime.InteropServices;

namespace FfxivImeBridge.Capture;

/// <summary>
/// Reads the modifiers through Win32 <c>GetKeyState</c>, which is synchronous
/// with the message being dispatched — so, called inside the pump hook, it is
/// the state the message was posted with. Not Dalamud's <c>IKeyState</c>: that
/// is the game's key array, downstream of the messages the Gate swallows.
/// </summary>
internal sealed partial class Win32KeyStateReader : IKeyStateReader
{
    public Modifiers Read() => new()
    {
        Shift = IsDown(VirtualKey.Shift),
        Ctrl = IsDown(VirtualKey.Control),
        Alt = IsDown(VirtualKey.Menu),
        RightAlt = IsDown(VirtualKey.RMenu),
        CapsLock = IsToggled(VirtualKey.Capital),
        NumLock = IsToggled(VirtualKey.NumLock),
    };

    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;
    private static bool IsToggled(int vk) => (GetKeyState(vk) & 0x0001) != 0;

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);
}
