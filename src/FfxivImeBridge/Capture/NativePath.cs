namespace FfxivImeBridge.Capture;

/// <summary>
/// Whether the Native Path takes keys before the message pump hook sees them
/// (GitHub issue #3). When <c>XMODIFIERS</c> names an input method server,
/// winex11 opens XIM, focuses an XIC on the game window and runs every X event
/// through <c>XFilterEvent</c> whatever the game's IME state, so fcitx5 keeps
/// its trigger key and every key it composes. Nothing inside the game turns
/// that off at runtime; XIVLauncher's <c>XMODIFIERS="@im=null"</c> hack does,
/// from the next launch.
/// </summary>
internal static class NativePath
{
    private const string ImModifier = "@im=";

    public static bool TakesKeys(string? xmodifiers)
    {
        var server = ServerName(xmodifiers);
        return server.Length > 0 && server is not ("null" or "none");
    }

    private static string ServerName(string? xmodifiers)
    {
        var start = xmodifiers?.IndexOf(ImModifier, StringComparison.Ordinal) ?? -1;
        if (start < 0) return "";
        var name = xmodifiers![(start + ImModifier.Length)..];
        var end = name.IndexOf('@');
        return end < 0 ? name : name[..end];
    }
}
