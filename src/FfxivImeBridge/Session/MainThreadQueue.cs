using System.Collections.Concurrent;

namespace FfxivImeBridge.Session;

/// <summary>
/// Hands work from the D-Bus reader thread to the game's main thread. Library
/// events <see cref="Post"/> here; the framework tick <see cref="Drain"/>s. No
/// event handler touches game memory directly.
/// </summary>
internal sealed class MainThreadQueue
{
    private readonly ConcurrentQueue<Action> pending = new();

    /// <summary>Queue work for the next <see cref="Drain"/>. Any thread.</summary>
    public void Post(Action action) => pending.Enqueue(action);

    /// <summary>Runs everything posted so far, in order. Main thread only.</summary>
    public void Drain()
    {
        while (pending.TryDequeue(out var action)) action();
    }
}
