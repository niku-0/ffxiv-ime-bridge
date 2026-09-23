using Tmds.DBus.Protocol;
using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>
/// The connection settings Wine needs: an explicit address, the Linux uid for
/// <c>AUTH EXTERNAL</c> (Tmds would otherwise present the Windows SID) and no
/// fd passing. Exercised on the host against the real bus.
/// </summary>
public sealed class FcitxConnectionOptionsTests
{
    private static CancellationToken Timeout => new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;

    [FcitxFact]
    public async Task Connects_with_an_explicit_address_and_uid_without_fd_passing()
    {
        var address = SessionBusAddress.Parse(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!);
        Assert.NotNull(address.UnixUserId);

        using var connection = await FcitxConnection.ConnectAsync(new FcitxConnectionOptions
        {
            Address = address.Raw,
            ExternalUserId = address.UnixUserId,
            SupportsFdPassing = false,
        }, Timeout);

        Assert.True(await connection.IsFcitxAvailableAsync(Timeout));
    }

    [FcitxFact]
    public async Task The_bus_rejects_an_external_auth_uid_that_is_not_the_peer_uid()
    {
        // This is why Tmds's Windows default (the SID) can never work from Wine: the bus
        // checks the claimed identity against SO_PEERCRED and does not accept ANONYMOUS either.
        var options = new FcitxConnectionOptions { ExternalUserId = 65534, SupportsFdPassing = false };

        await Assert.ThrowsAsync<DBusConnectFailedException>(() => FcitxConnection.ConnectAsync(options, Timeout));
    }
}
