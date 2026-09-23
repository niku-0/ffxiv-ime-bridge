using FfxivImeBridge.Session;

namespace FfxivImeBridge.Tests;

/// <summary>A scripted Transport Ladder: fails until <see cref="Reachable"/>, then opens a session over a fresh <see cref="FakeFcitx"/>; <see cref="Pending"/> holds one attempt open.</summary>
internal sealed class FakeSessionSource(List<string> chat) : ISessionSource
{
    public List<(bool Silent, bool Forwarding)> Attempts { get; } = new();
    public bool Reachable { get; set; }
    public TaskCompletionSource<OpenedSession?>? Pending { get; set; }
    public FakeFcitx? LastFcitx { get; private set; }
    public FakeTransport? LastTransport { get; private set; }

    public async Task<OpenedSession?> OpenAsync(bool silent, bool forwarding, CancellationToken cancellationToken)
    {
        Attempts.Add((silent, forwarding));
        if (Pending is { } pending)
        {
            Pending = null;
            return await pending.Task;
        }
        return Reachable ? await Open(forwarding) : null;
    }

    public async Task<OpenedSession> Open(bool forwarding)
    {
        LastFcitx = new FakeFcitx();
        LastTransport = new FakeTransport();
        var session = await ForwardingSession.OpenAsync(LastFcitx, chat.Add, _ => { }, forwarding);
        return new OpenedSession(session, LastTransport);
    }
}

/// <summary>Stands for the bus connection under a session; only its disposal matters.</summary>
internal sealed class FakeTransport : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>A clock the test moves.</summary>
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
