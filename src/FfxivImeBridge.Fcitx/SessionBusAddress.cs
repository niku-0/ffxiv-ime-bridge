using System.Text.RegularExpressions;

namespace FfxivImeBridge.Fcitx;

/// <summary>
/// A D-Bus session bus address (<c>DBUS_SESSION_BUS_ADDRESS</c>) taken apart
/// far enough to reach it from inside Wine: the socket path, how that path is
/// spelled on the Windows side, and the Linux uid the bus will expect in
/// <c>AUTH EXTERNAL</c> (the process is uid 1000 to the kernel whatever
/// Windows identity .NET reports).
/// </summary>
public sealed partial record SessionBusAddress(string Raw, string? UnixPath, uint? UnixUserId)
{
    /// <summary>The socket path as a Wine process sees the root filesystem: <c>Z:\run\user\1000\bus</c>.</summary>
    public string? WinePath => UnixPath is null ? null : "Z:" + UnixPath.Replace('/', '\\');

    /// <summary>Takes the first <c>unix:path=</c> entry; other transports leave <see cref="UnixPath"/> null. Never throws.</summary>
    public static SessionBusAddress Parse(string address)
    {
        var path = UnixPathPattern().Match(address) is { Success: true } m ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
        var uid = path is not null && RunUserPattern().Match(path) is { Success: true } u ? uint.Parse(u.Groups[1].Value) : (uint?)null;
        return new SessionBusAddress(address, path, uid);
    }

    [GeneratedRegex(@"unix:(?:[^;,]*,)*path=([^,;]+)")]
    private static partial Regex UnixPathPattern();

    [GeneratedRegex(@"^/run/user/(\d+)/")]
    private static partial Regex RunUserPattern();
}
