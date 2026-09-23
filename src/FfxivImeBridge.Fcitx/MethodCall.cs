using Tmds.DBus.Protocol;

namespace FfxivImeBridge.Fcitx;

/// <summary>The flags every method call this library sends is written with.</summary>
internal static class MethodCall
{
    /// <summary>
    /// <see cref="MessageFlags.NoAutoStart"/>, on every call without exception.
    /// fcitx5 ships a D-Bus activation file (<c>org.fcitx.Fcitx5.service</c>),
    /// so a call addressed to its name while nobody owns it makes
    /// <c>dbus-daemon</c> exec <c>fcitx5</c>: unloading the plugin, a Reconnect,
    /// or the release of a key forwarded just before the loss would start
    /// fcitx5 again on a desktop the user had quit it on (ticket 18). With the
    /// flag the bus answers <c>org.freedesktop.DBus.Error.NameHasNoOwner</c>
    /// instead, which the callers already log as a fault.
    /// </summary>
    public const MessageFlags Flags = MessageFlags.NoAutoStart;
}
