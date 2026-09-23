using Dalamud.Plugin.Services;
using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.NativeWrite;
using FfxivImeBridge.Session;

namespace FfxivImeBridge;

/// <summary>
/// The Dalamud side of the session's life (<see cref="SessionLifecycle"/>):
/// the Transport Ladder and the live connection behind its
/// <see cref="ISessionSource"/>, the framework tick that drives the session
/// and feeds Chat Box focus to both, the user's toggle (every flip saved),
/// and the two drains that carry a Commit to the <see cref="NativeWriter"/>
/// on the game thread. A Reconnect (by hand, or automatic on a focus gain
/// while Inert or the connection is dead) is the lifecycle's; this only
/// asks for it.
/// </summary>
internal sealed class Bridge : IDisposable
{
    private readonly IFramework framework;
    private readonly IGameGui gui;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly IToastGui toast;
    private readonly NativeWriter writer;
    private readonly ConfigStore config;
    private readonly SessionLifecycle lifecycle;
    private readonly CancellationTokenSource disposal = new();

    public Bridge(IFramework framework, IGameGui gui, IPluginLog log, IChatGui chat, IToastGui toast, ProbeRunner probe, NativeWriter writer, ConfigStore config)
    {
        this.framework = framework;
        this.gui = gui;
        this.log = log;
        this.chat = chat;
        this.toast = toast;
        this.writer = writer;
        this.config = config;
        lifecycle = new SessionLifecycle(new LadderSessionSource(probe, chat, log), config.Current, line => chat.Print(line), line => log.Information("Session: {Line}", line), TimeProvider.System);
        framework.Update += OnUpdate;
    }

    /// <summary>The live session; null while the ladder is climbing or after it failed (inert).</summary>
    public ForwardingSession? Session => lifecycle.Session;

    /// <summary>The ladder failed and nothing is climbing: the plugin does nothing and says so when asked to. A Reconnect is the way out.</summary>
    public bool Inert => lifecycle.Inert;

    /// <summary>A Reconnect (or the load) is climbing the ladder.</summary>
    public bool Reconnecting => lifecycle.Opening;

    /// <summary>Run the ladder once and, if it holds, open the session on a connection of its own.</summary>
    public void Start() => lifecycle.Start();

    /// <summary>By hand (<c>/imebridge reconnect</c>, the settings window): tear down and climb again, whatever the clock says. One at a time.</summary>
    public void Reconnect()
    {
        if (!lifecycle.Reconnect()) toast.ShowError(Strings.StillConnecting);
    }

    /// <summary>The user's toggle, from the chord, the command or a checkbox: every flip is saved. Inert → a toast, nothing else.</summary>
    public void SetForwarding(bool? value)
    {
        if (Session is { } live)
        {
            var next = value ?? !live.Forwarding;
            if (next == live.Forwarding) return;
            live.Forwarding = next;
            config.Current.Forwarding = next;
            config.Save();
        }
        else
        {
            toast.ShowError(Inert ? Strings.NotReachable : Strings.StillConnecting);
        }
    }

    /// <summary>
    /// Debug aid (ticket 05's in-game check): switch the input method of the
    /// <em>focused</em> context after a delay, which is long enough to click back
    /// into the Chat Box (sending the command unfocused it). Since ticket 07
    /// fcitx5's own trigger key does the same through the Gate; a switch from
    /// the host lands on the host's focused window instead.
    /// </summary>
    public void SwitchInputMethodAfter(string uniqueName, TimeSpan delay)
    {
        if (lifecycle.Transport is not FcitxConnection live)
        {
            toast.ShowError(Strings.NoLiveConnection);
            return;
        }
        chat.Print(Strings.SwitchingInputMethod(uniqueName, delay));
        framework.RunOnTick(() =>
        {
            if (Session is not { ChatBoxFocused: true })
            {
                chat.Print(Strings.NothingSwitched);
                return;
            }
            live.SetCurrentInputMethodAsync(uniqueName).ContinueWith(
                t => log.Warning("Session: SetCurrentIM({Name}) failed: {Error}", uniqueName, t.Exception?.InnerException?.Message ?? "unknown"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }, delay, cancellationToken: disposal.Token);
    }

    /// <summary>The tick: Chat Box focus to the lifecycle (an automatic Reconnect) and the session, the session's own tick, and the tick's drain — a Commit that arrived out of band (a reply after its timeout, fcitx5 committing on its own).</summary>
    private void OnUpdate(IFramework _)
    {
        var focused = ChatBoxFocus.Read(gui).ChatBoxFocused;
        lifecycle.ObserveFocus(focused);
        if (Session is not { } live) return;
        live.Tick();
        live.ObserveFocus(focused);
        DrainCommits(live);
    }

    /// <summary>
    /// The hook's drain, right after a waited key reply on the game thread: the
    /// state snapshot and any Commit are already in (signals precede the reply),
    /// so take them before the next message (ADR-0002). The write cannot wait
    /// for the tick: a later passed key would land in the Chat Box first.
    /// </summary>
    public void SettleAfterReply()
    {
        if (Session is not { } live) return;
        live.TakeSnapshot();
        DrainCommits(live);
    }

    private void DrainCommits(ForwardingSession live)
    {
        while (live.TryTakeCommit(out var text)) writer.Write(text);
    }

    public void Dispose() => Dispose(SessionLifecycle.Grace);

    /// <summary>Tears the session down within <paramref name="budget"/>, what is left of the unload's; a climb still running is cancelled first, so dispose this before the ladder.</summary>
    public void Dispose(TimeSpan budget)
    {
        framework.Update -= OnUpdate;
        disposal.Cancel();
        lifecycle.Dispose(budget);
        disposal.Dispose();
    }
}

/// <summary>The load-time steps as one <see cref="ISessionSource"/>: the ladder on its throwaway connection, then a live connection, its availability watch and the session.</summary>
internal sealed class LadderSessionSource(ProbeRunner probe, IChatGui chat, IPluginLog log) : ISessionSource
{
    public async Task<OpenedSession?> OpenAsync(bool silent, bool forwarding, CancellationToken cancellationToken)
    {
        var report = await probe.RunAsync(silent).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (report is not { Succeeded: true, ConnectionOptions: { } options }) return null;

        var live = await FcitxConnection.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        try
        {
            await live.WatchAvailabilityAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var session = await ForwardingSession.OpenAsync(new FcitxContextFactory(live, log), line => chat.Print(line), line => log.Information("Session: {Line}", line), forwarding, cancellationToken).ConfigureAwait(false);
            return new OpenedSession(session, live);
        }
        catch
        {
            live.Dispose();
            throw;
        }
    }
}
