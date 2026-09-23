using System.Diagnostics;

namespace FfxivImeBridge.Session;

/// <summary>
/// What one climb of the Transport Ladder yields: the session and the bus
/// connection under it. The transport is only disposable here so the tests
/// can stand one in; the one caller that needs the real connection (the
/// debug aid that switches the input method) knows it is an <c>FcitxConnection</c>.
/// </summary>
internal sealed record OpenedSession(ForwardingSession Session, IDisposable Transport);

/// <summary>Climbs the Transport Ladder and opens a session on what it finds — the load-time steps, and a Reconnect's.</summary>
internal interface ISessionSource
{
    /// <summary>
    /// The ladder, a connection, an Input Context and the session around it,
    /// with <paramref name="forwarding"/> as its starting Forwarding; null when
    /// the ladder does not hold (the ladder itself says why: in the log always,
    /// in chat unless <paramref name="silent"/>). Any other failure throws.
    /// </summary>
    Task<OpenedSession?> OpenAsync(bool silent, bool forwarding, CancellationToken cancellationToken);
}

/// <summary>What asked for a climb of the ladder; decides what is said about it.</summary>
internal enum ReconnectTrigger
{
    /// <summary>Plugin load: loud, as always.</summary>
    Load,
    /// <summary><c>/imebridge reconnect</c> or the settings window: loud, whatever the outcome.</summary>
    Manual,
    /// <summary>A Chat Box focus gain while Inert or the connection is dead: silent unless it succeeds.</summary>
    Automatic,
}

/// <summary>
/// The session's whole life: opened on load, held for the Gate and the drawing
/// code, replaced by a Reconnect — tear down whatever is left, climb the
/// ladder again, re-apply the config's startup behaviour — and disposed within
/// the unload budget. A Reconnect is the only way out of Inert and of a dead
/// connection (<see cref="ForwardingSession.ConnectionLost"/>); it runs by
/// hand, or on a Chat Box focus gain at most once per
/// <see cref="AutomaticInterval"/>. One attempt at a time: while one runs the
/// session is gone and another request is ignored. Main thread, except the
/// attempt's own continuation.
/// </summary>
internal sealed class SessionLifecycle : IDisposable
{
    /// <summary>Automatic attempts are this far apart at least; load and manual attempts count too.</summary>
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The whole unload's budget on the framework thread, which a Reconnect's
    /// teardown borrows as its own bound. <c>Plugin.Dispose</c> splits it between
    /// this and the Transport Ladder, rather than letting each wait it out in
    /// turn and freeze the game for twice as long (ticket 19).
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    private readonly ISessionSource source;
    private readonly Configuration config;
    private readonly Action<string> chatLine;
    private readonly Action<string> log;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource disposal = new();
    private OpenedSession? opened;
    private Task? attempt;
    private DateTimeOffset? lastAttempt;
    private bool chatBoxFocused;

    /// <param name="chatLine">Prints a local chat line (never a sent message).</param>
    public SessionLifecycle(ISessionSource source, Configuration config, Action<string> chatLine, Action<string> log, TimeProvider clock)
    {
        this.source = source;
        this.config = config;
        this.chatLine = chatLine;
        this.log = log;
        this.clock = clock;
    }

    /// <summary>The live session; null while an attempt runs or after one failed (Inert).</summary>
    public ForwardingSession? Session => Volatile.Read(ref opened)?.Session;

    /// <summary>The connection under the live session, for the debug aid that needs it.</summary>
    public IDisposable? Transport => Volatile.Read(ref opened)?.Transport;

    /// <summary>An attempt is climbing the ladder.</summary>
    public bool Opening => attempt is { IsCompleted: false };

    /// <summary>The last attempt failed and none is running: nothing is hooked into, and only a Reconnect changes that.</summary>
    public bool Inert => lastAttempt is not null && !Opening && Session is null;

    /// <summary>The running attempt, for whoever has to wait on it (tests, unload).</summary>
    public Task? Pending => attempt;

    /// <summary>Load: climb the ladder, loud.</summary>
    public void Start() => Attempt(ReconnectTrigger.Load);

    /// <summary>By hand: runs whatever the clock says; false when an attempt is already running (or after unload).</summary>
    public bool Reconnect() => Attempt(ReconnectTrigger.Manual);

    /// <summary>The tick's Chat Box focus: a gain while Inert or the connection is dead starts a silent attempt, if none ran in the last <see cref="AutomaticInterval"/>.</summary>
    public void ObserveFocus(bool chatBoxFocused)
    {
        if (this.chatBoxFocused == chatBoxFocused) return;
        this.chatBoxFocused = chatBoxFocused;
        if (!chatBoxFocused || !NeedsReconnect) return;
        if (lastAttempt is { } last && clock.GetUtcNow() - last < AutomaticInterval) return;
        Attempt(ReconnectTrigger.Automatic);
    }

    private bool NeedsReconnect => Inert || Session is { ConnectionLost: true };

    private bool Attempt(ReconnectTrigger trigger)
    {
        if (Opening || disposal.IsCancellationRequested) return false;
        lastAttempt = clock.GetUtcNow();
        var old = Interlocked.Exchange(ref opened, null);
        attempt = AttemptAsync(trigger, old);
        return true;
    }

    private async Task AttemptAsync(ReconnectTrigger trigger, OpenedSession? old)
    {
        if (old is not null) await TearDownAsync(old).ConfigureAwait(false);
        log($"{trigger}: climbing the transport ladder");
        OpenedSession? next = null;
        try
        {
            next = await source.OpenAsync(trigger == ReconnectTrigger.Automatic, config.InitialForwarding, disposal.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested)
        {
            return; // unloaded mid-climb
        }
        catch (Exception ex)
        {
            log($"opening the live connection failed: {ex.Message}");
        }

        if (next is null)
        {
            log($"{trigger}: transport ladder did not pass; plugin is inert");
            if (trigger != ReconnectTrigger.Automatic) chatLine(Strings.NotReachable);
            return;
        }
        if (disposal.IsCancellationRequested)
        {
            await TearDownAsync(next).ConfigureAwait(false); // unloaded while the session was opening
            return;
        }
        Volatile.Write(ref opened, next);
        log($"{trigger}: session open; forwarding {(next.Session.Forwarding ? "on" : "off")} ({config.Startup})");
        chatLine(Strings.Reachable(next.Session.Forwarding));
    }

    /// <summary>Reset, FocusOut, destroy the context and drop the connection, each bounded and each failure only logged: a dead connection has nothing to answer with.</summary>
    private async Task TearDownAsync(OpenedSession live)
    {
        try
        {
            await live.Session.DisposeAsync().AsTask().WaitAsync(Grace).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log($"tearing the session down failed: {ex.Message}");
        }
        try
        {
            live.Transport.Dispose();
        }
        catch (Exception ex)
        {
            log($"dropping the connection failed: {ex.Message}");
        }
    }

    /// <summary>Unload with the whole budget to itself: only the tests and a caller with nothing else to tear down.</summary>
    public void Dispose() => Dispose(Grace);

    /// <summary>
    /// Unload: cancel a running attempt, tear the session down, all within
    /// <paramref name="budget"/> — what is left of the unload's after whatever
    /// ran before it. What is cut short fcitx5 reaps when the connection drops.
    /// </summary>
    public void Dispose(TimeSpan budget)
    {
        disposal.Cancel();
        var spent = Stopwatch.StartNew();
        Wait(attempt, budget - spent.Elapsed);
        if (Interlocked.Exchange(ref opened, null) is { } live) Wait(TearDownAsync(live), budget - spent.Elapsed);
        disposal.Dispose();
    }

    private void Wait(Task? task, TimeSpan remaining)
    {
        if (task is null) return;
        try
        {
            if (remaining <= TimeSpan.Zero || !task.Wait(remaining)) log("unload budget spent; fcitx5 reaps the context when the connection drops");
        }
        catch (AggregateException ex)
        {
            log($"cleanup failed on unload: {ex.InnerException?.Message}");
        }
    }
}
