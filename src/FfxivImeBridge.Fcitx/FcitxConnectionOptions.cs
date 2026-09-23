using Tmds.DBus.Protocol;

namespace FfxivImeBridge.Fcitx;

/// <summary>
/// How to reach the session bus. The defaults are right on a Linux host; a
/// client inside Wine has to set every property, because Tmds.DBus.Protocol's
/// own defaults there (Windows SID as the <c>AUTH EXTERNAL</c> identity, unix
/// fd passing) are meaningless to a Linux bus.
/// </summary>
public sealed class FcitxConnectionOptions
{
    /// <summary>Bus address; null uses <c>DBUS_SESSION_BUS_ADDRESS</c>.</summary>
    public string? Address { get; init; }

    /// <summary>
    /// Linux uid presented in <c>AUTH EXTERNAL</c>. The bus compares it with the
    /// peer's kernel credentials, so it must be the uid the process actually
    /// runs as. Null lets Tmds pick: geteuid on Linux, the Windows SID under
    /// Wine (which the bus rejects).
    /// </summary>
    public uint? ExternalUserId { get; init; }

    /// <summary>Negotiate unix fd passing. Not meaningful on Windows/Wine.</summary>
    public bool SupportsFdPassing { get; init; } = !OperatingSystem.IsWindows();

    internal DBusConnectionOptions ToDBusOptions()
    {
        var address = Address ?? DBusAddress.Session ?? throw new InvalidOperationException("DBUS_SESSION_BUS_ADDRESS is not set and no address was given.");
        return new Setup(this, address);
    }

    private sealed class Setup(FcitxConnectionOptions options, string address) : DBusConnectionOptions(address)
    {
        protected override async ValueTask<SetupResult> SetupAsync(CancellationToken cancellationToken)
        {
            var result = await base.SetupAsync(cancellationToken).ConfigureAwait(false);
            if (options.ExternalUserId is { } uid) result.UserId = uid.ToString();
            result.SupportsFdPassing = options.SupportsFdPassing;
            return result;
        }
    }
}
