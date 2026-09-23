using System.Net.Sockets;
using System.Threading.Channels;
using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>
/// The connection's death is reported (ticket 13). The bus itself is never
/// restarted for it: the test puts a relay socket of its own between the
/// connection and the real bus and pulls the relay away.
/// </summary>
public sealed class DisconnectTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [FcitxFact]
    public async Task The_socket_under_the_connection_going_away_is_reported_once_with_its_reason()
    {
        await using var relay = await BusRelay.StartAsync();
        var lost = Channel.CreateUnbounded<Exception>();
        using var connection = await FcitxConnection.ConnectAsync(relay.Address, new CancellationTokenSource(Timeout).Token);
        connection.Disconnected += reason => lost.Writer.TryWrite(reason);
        Assert.True(await connection.IsFcitxAvailableAsync(new CancellationTokenSource(Timeout).Token)); // the relay carries traffic

        await relay.DisposeAsync();

        var reason = await lost.Reader.ReadAsync(new CancellationTokenSource(Timeout).Token);
        Assert.NotNull(reason);
        await Task.Delay(100);
        Assert.False(lost.Reader.TryRead(out _));
    }

    [FcitxFact]
    public async Task Disposing_the_connection_ourselves_is_not_a_loss()
    {
        var connection = await FcitxConnection.ConnectAsync(cancellationToken: new CancellationTokenSource(Timeout).Token);
        var raised = 0;
        connection.Disconnected += _ => Interlocked.Increment(ref raised);

        connection.Dispose();

        await Task.Delay(200);
        Assert.Equal(0, raised);
    }

    /// <summary>A unix socket the test owns, copying bytes both ways to the real session bus until disposed.</summary>
    private sealed class BusRelay : IAsyncDisposable
    {
        private readonly string path;
        private readonly Socket listener;
        private readonly CancellationTokenSource stop = new();
        private readonly List<Socket> sockets = new();
        private Task? pumping;

        private BusRelay(string path, Socket listener)
        {
            this.path = path;
            this.listener = listener;
        }

        public string Address => $"unix:path={path}";

        public static Task<BusRelay> StartAsync()
        {
            var busPath = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!.Split(',')[0]["unix:path=".Length..];
            var path = Path.Combine(Path.GetTempPath(), $"imebridge-relay-{Environment.ProcessId}-{Guid.NewGuid():N}");
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            var relay = new BusRelay(path, listener);
            relay.pumping = relay.PumpAsync(busPath);
            return Task.FromResult(relay);
        }

        private async Task PumpAsync(string busPath)
        {
            using var client = await listener.AcceptAsync(stop.Token);
            using var bus = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await bus.ConnectAsync(new UnixDomainSocketEndPoint(busPath), stop.Token);
            lock (sockets) sockets.AddRange([client, bus]);
            using var toBus = new NetworkStream(client, ownsSocket: false);
            using var fromBus = new NetworkStream(bus, ownsSocket: false);
            try
            {
                await Task.WhenAny(toBus.CopyToAsync(fromBus, stop.Token), fromBus.CopyToAsync(toBus, stop.Token));
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                // Torn down by the test.
            }
        }

        /// <summary>Pulls the relay away; a second call does nothing.</summary>
        public async ValueTask DisposeAsync()
        {
            if (stop.IsCancellationRequested) return;
            stop.Cancel();
            lock (sockets)
            {
                foreach (var socket in sockets)
                {
                    try { socket.Shutdown(SocketShutdown.Both); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
                    socket.Close();
                }
            }
            listener.Close();
            try { if (pumping is not null) await pumping; } catch (Exception) { }
            File.Delete(path);
        }
    }
}
