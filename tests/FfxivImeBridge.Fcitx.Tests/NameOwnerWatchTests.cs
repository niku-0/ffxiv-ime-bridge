using System.Threading.Channels;
using Tmds.DBus.Protocol;
using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>
/// The availability watch is exercised against a well-known name the test
/// itself owns, on a second connection, so fcitx5 is never killed for it.
/// </summary>
public sealed class NameOwnerWatchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [FcitxFact]
    public async Task Reports_the_watched_name_appearing_and_leaving_the_bus()
    {
        var name = $"org.ffxivimebridge.Test{Environment.ProcessId}";
        using var watcher = await FcitxConnection.ConnectAsync(cancellationToken: new CancellationTokenSource(Timeout).Token);
        var seen = Channel.CreateUnbounded<bool>();
        watcher.FcitxAvailabilityChanged += available => seen.Writer.TryWrite(available);
        await watcher.WatchAvailabilityAsync(name, new CancellationTokenSource(Timeout).Token);

        var owner = new DBusConnection(DBusAddress.Session!);
        await owner.ConnectAsync();
        await owner.RequestNameAsync(name, RequestNameOptions.None);
        Assert.True(await seen.Reader.ReadAsync(new CancellationTokenSource(Timeout).Token));

        await owner.ReleaseNameAsync(name);
        Assert.False(await seen.Reader.ReadAsync(new CancellationTokenSource(Timeout).Token));

        // Owner dropping off the bus entirely counts as leaving too.
        await owner.RequestNameAsync(name, RequestNameOptions.None);
        Assert.True(await seen.Reader.ReadAsync(new CancellationTokenSource(Timeout).Token));
        owner.Dispose();
        Assert.False(await seen.Reader.ReadAsync(new CancellationTokenSource(Timeout).Token));
    }
}
