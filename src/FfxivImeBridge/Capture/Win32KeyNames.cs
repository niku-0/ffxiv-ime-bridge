using System.Runtime.InteropServices;

namespace FfxivImeBridge.Capture;

/// <summary>
/// What the current layout prints on a set-1 scancode
/// (<see cref="KeyMessage.Set1ScanCode"/>), for showing a <see cref="ToggleKey"/>
/// as <c>Alt+§</c> rather than <c>Alt+sc 0x29</c>: <c>MapVirtualKeyW</c>
/// scancode → virtual key → unshifted character, and for a key that prints
/// nothing (F5, Home) the name <c>GetKeyNameTextW</c> gives it, told apart
/// from its numpad twin by the extended bit; null when neither knows the key.
/// Win32 only, so the settings window calls it on the main thread.
/// </summary>
internal static partial class Win32KeyNames
{
    private const uint MapScanCodeToVirtualKeyEx = 3; // MAPVK_VSC_TO_VK_EX
    private const uint MapVirtualKeyToChar = 2; // MAPVK_VK_TO_CHAR
    private const int ExtendedPrefix = 0xE000;

    public static string? NameFor(int set1ScanCode)
    {
        var extended = (set1ScanCode & 0xFF00) == ExtendedPrefix;
        var scanCode = set1ScanCode & 0xFF;
        if (scanCode == 0 || (set1ScanCode & ~0xFF) != (extended ? ExtendedPrefix : 0)) return null;

        // An extended key prints nothing, and MapVirtualKey's scancode form has no room for the prefix.
        if (!extended && MapVirtualKeyW((uint)scanCode, MapScanCodeToVirtualKeyEx) is var vk and not 0)
        {
            // Low word is the character; the top bit flags a dead key, which still names the key.
            var ch = (char)(MapVirtualKeyW(vk, MapVirtualKeyToChar) & 0xFFFF);
            if (ch > ' ' && !char.IsControl(ch)) return char.ToUpperInvariant(ch).ToString();
        }

        Span<char> name = stackalloc char[64];
        var lParam = (scanCode << 16) | (extended ? 1 << 24 : 0);
        var length = GetKeyNameTextW(lParam, name, name.Length);
        return length > 0 ? new string(name[..length]) : null;
    }

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetKeyNameTextW(int lParam, Span<char> lpString, int cchSize);
}
