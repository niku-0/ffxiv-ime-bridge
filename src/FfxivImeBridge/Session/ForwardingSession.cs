using System.Collections.Concurrent;
using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Session;

/// <summary>Why Forwarding is Degraded; decides what the next Chat Box focus gain has to do to lift it.</summary>
internal enum DegradedCause
{
    /// <summary>fcitx5 is there but stopped answering in time (ADR-0002); the context is kept.</summary>
    Timeouts,
    /// <summary>fcitx5 left the bus: its side of the Input Context is gone, so a new one is created when it is back.</summary>
    BusLost,
    /// <summary>The bus connection itself died: nothing on it can be recreated, only a Reconnect (a new session) lifts it.</summary>
    ConnectionLost,
}

/// <summary>
/// The plugin's one Input Context, tied to the Chat Box's focus, and the
/// Forwarding/Degraded state around it. Main thread only, except the library
/// events that <see cref="MainThreadQueue"/> carries over. The Gate acts iff
/// <see cref="GateActive"/>. A dead connection (<see cref="ConnectionLost"/>)
/// is the end of the session's usefulness: the owner replaces it by a Reconnect.
/// </summary>
internal sealed class ForwardingSession : IAsyncDisposable
{
    private readonly IInputContextFactory factory;
    private readonly Action<string> chatLine;
    private readonly Action<string> log;
    private readonly MainThreadQueue queue = new();
    private readonly ConcurrentQueue<string> commits = new();
    private readonly CancellationTokenSource disposal = new();
    private IInputContextClient? context;
    private Action<InputMethodInfo>? onInputMethodChanged;
    private Action<string>? onCommitted;
    private Task<IInputContextClient>? recreation;
    private DegradedCause? degraded;
    private bool fcitxOnBus = true;
    private bool forwarding;
    private bool chatBoxFocused;

    private ForwardingSession(IInputContextFactory factory, IInputContextClient context, Action<string> chatLine, Action<string> log)
    {
        this.factory = factory;
        this.chatLine = chatLine;
        this.log = log;
        factory.AvailabilityChanged += available => queue.Post(() => OnAvailabilityChanged(available));
        factory.ConnectionLost += reason => queue.Post(() => OnConnectionLost(reason));
        Attach(context);
    }

    /// <summary>Creates the first Input Context and the session around it.</summary>
    /// <param name="chatLine">Prints a local chat line (never a sent message).</param>
    /// <param name="forwarding">What Forwarding starts as (the config's startup behaviour); not a flip, so no chat line.</param>
    public static async Task<ForwardingSession> OpenAsync(IInputContextFactory factory, Action<string> chatLine, Action<string> log, bool forwarding = false, CancellationToken cancellationToken = default)
    {
        var context = await factory.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        return new ForwardingSession(factory, context, chatLine, log) { forwarding = forwarding };
    }

    /// <summary>The user's choice: keys typed into the focused Chat Box go to fcitx5. Every flip prints a chat line.</summary>
    public bool Forwarding
    {
        get => forwarding;
        set
        {
            if (forwarding == value) return;
            forwarding = value;
            chatLine(Strings.ForwardingFlipped(value));
        }
    }

    /// <summary>Forwarding with nothing behind it: fcitx5 gone or not answering. Lifts on the next Chat Box focus gain, except <see cref="ConnectionLost"/>.</summary>
    public bool Degraded => degraded is not null;

    /// <summary>Degraded because the connection died: only a Reconnect lifts it.</summary>
    public bool ConnectionLost => degraded == DegradedCause.ConnectionLost;

    /// <summary>
    /// There is nothing on the other end to talk to: fcitx5 left the bus (it
    /// reaped its side of the context) or the connection died. Any call made
    /// anyway would be addressed to an unowned name, and the bus would start
    /// fcitx5 for it (ticket 18).
    /// </summary>
    private bool NothingToTell => degraded is DegradedCause.BusLost or DegradedCause.ConnectionLost;

    /// <summary>Forwarding with fcitx5 behind it.</summary>
    public bool GateActive => Forwarding && !Degraded;

    public InputMethodInfo? CurrentInputMethod { get; private set; }

    /// <summary>The context the Gate asks; null only while a BusLost recreation has detached the old one.</summary>
    public IInputContextClient? Context => context;

    /// <summary>
    /// The context's state as last taken on the game thread — after each waited
    /// key reply and on each tick — for rendering (ticket 08). <see cref="Context"/>'s
    /// own <c>State</c> is always the freshest; this one only moves on the game thread.
    /// </summary>
    public CompositionState Composition { get; private set; } = CompositionState.Idle;

    /// <summary>Whether fcitx5 owns typed keys right now: there is a preedit or a candidate list.</summary>
    public bool IsComposing => context?.State.IsComposing ?? false;

    /// <summary>Copies the context's state into <see cref="Composition"/>. Game thread.</summary>
    public void TakeSnapshot() => Composition = context?.State ?? CompositionState.Idle;

    /// <summary>Chat Box focus as last observed, for the Indicator.</summary>
    public bool ChatBoxFocused => chatBoxFocused;

    /// <summary>
    /// What the Indicator shows: <c>!</c> while Degraded, else the context's
    /// input method (<c>あ</c> Mozc, <c>A</c> a keyboard layout, its initial
    /// otherwise), null until fcitx5 has named one.
    /// </summary>
    public string? IndicatorGlyph => Degraded ? "!" : CurrentInputMethod is { } im ? GlyphFor(im.UniqueName) : null;

    private static string GlyphFor(string uniqueName) => uniqueName switch
    {
        "mozc" => "あ",
        _ when uniqueName.StartsWith("keyboard-", StringComparison.Ordinal) => "A",
        _ => uniqueName[..1].ToUpperInvariant(),
    };

    /// <summary>The next committed text, in the order fcitx5 sent it. Drained on the game thread: from the hook after a waited reply, and from the tick.</summary>
    public bool TryTakeCommit(out string text) => commits.TryDequeue(out text!);

    /// <summary>
    /// Enter Degraded. One chat line per entry; a worse cause on top of a lesser
    /// one upgrades silently, because lifting it then needs more (a new context
    /// after a bus loss, a new session after the connection died).
    /// </summary>
    public void MarkDegraded(DegradedCause cause)
    {
        if (degraded is { } current && current >= cause) return;
        var entering = degraded is null;
        degraded = cause;
        log($"forwarding degraded: {cause}");
        if (entering)
        {
            chatLine(cause switch
            {
                DegradedCause.BusLost => Strings.DegradedBusLost,
                DegradedCause.ConnectionLost => Strings.DegradedConnectionLost,
                _ => Strings.DegradedNotAnswering,
            });
        }
    }

    /// <summary>
    /// Both readers of Chat Box focus — the framework tick and the message hook
    /// — report here; whichever sees an edge first issues the focus calls and
    /// the other sees no edge.
    /// </summary>
    public void ObserveFocus(bool chatBoxFocused)
    {
        if (this.chatBoxFocused == chatBoxFocused) return;
        this.chatBoxFocused = chatBoxFocused;
        if (chatBoxFocused) OnFocusGained();
        else OnFocusLost();
    }

    private void OnFocusGained()
    {
        switch (degraded)
        {
            case DegradedCause.BusLost when fcitxOnBus && recreation is null:
                log("fcitx5 is back; recreating the input context");
                recreation = factory.CreateContextAsync(disposal.Token);
                return;
            case DegradedCause.BusLost:
                return; // still gone, or a recreation is under way: stays Degraded
            case DegradedCause.ConnectionLost:
                return; // nothing to focus and nothing to recreate: a Reconnect replaces this session
            case DegradedCause.Timeouts:
                degraded = null;
                log("forwarding degraded lifted on focus gain");
                break;
        }
        context?.FocusIn();
    }

    private void OnFocusLost()
    {
        if (NothingToTell) return;
        context?.Reset();
        context?.FocusOut();
    }

    private void OnAvailabilityChanged(bool available)
    {
        fcitxOnBus = available;
        log($"fcitx5 {(available ? "appeared on" : "left")} the bus");
        if (!available) MarkDegraded(DegradedCause.BusLost);
    }

    private void OnConnectionLost(string reason)
    {
        log($"connection lost: {reason}");
        MarkDegraded(DegradedCause.ConnectionLost);
    }

    /// <summary>Drains what the reader thread posted, finishes a pending recreation and takes the state snapshot. Once per framework update.</summary>
    public void Tick()
    {
        queue.Drain();
        if (recreation is { IsCompleted: true } done)
        {
            recreation = null;
            CompleteRecreation(done);
        }
        TakeSnapshot();
    }

    private void CompleteRecreation(Task<IInputContextClient> done)
    {
        if (!done.IsCompletedSuccessfully)
        {
            log($"recreating the input context failed: {done.Exception?.InnerException?.Message ?? "cancelled"}");
            return; // stays Degraded; the next focus gain tries again
        }
        var old = Detach();
        Attach(done.Result);
        degraded = null;
        log("input context recreated; forwarding degraded lifted");
        if (old is not null) Forget(old.DisposeAsync());
        if (chatBoxFocused) context!.FocusIn();
    }

    private void Attach(IInputContextClient next)
    {
        context = next;
        CurrentInputMethod = null;
        onInputMethodChanged = info => queue.Post(() => CurrentInputMethod = info);
        onCommitted = commits.Enqueue;
        next.InputMethodChanged += onInputMethodChanged;
        next.Committed += onCommitted;
    }

    /// <summary>Stops listening to the current context (a late signal from a dead one must not land in the queues) and hands it back.</summary>
    private IInputContextClient? Detach()
    {
        var old = context;
        if (old is not null)
        {
            old.InputMethodChanged -= onInputMethodChanged;
            old.Committed -= onCommitted;
        }
        context = null;
        return old;
    }

    private void Forget(ValueTask task)
    {
        if (task.IsCompletedSuccessfully) return;
        task.AsTask().ContinueWith(t => log($"disposing the old input context failed: {t.Exception?.InnerException?.Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Focus lost for good (Reset, FocusOut) and the context destroyed — unless there is <see cref="NothingToTell"/>, when it is only let go. The caller bounds the wait (the plugin's unload budget).</summary>
    public async ValueTask DisposeAsync()
    {
        disposal.Cancel();
        if (chatBoxFocused) ObserveFocus(chatBoxFocused: false);
        var old = Detach();
        if (old is null) return;
        if (NothingToTell) old.Abandon();
        else await old.DisposeAsync().ConfigureAwait(false);
    }
}
