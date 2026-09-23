using Dalamud.Plugin.Services;
using FfxivImeBridge.NativeWrite;
using FfxivImeBridge.Rendering;

namespace FfxivImeBridge.Capture;

/// <summary>One keyboard or mouse message as it went through its gate, for the debug window.</summary>
internal readonly record struct CaptureTraceEntry(DateTime At, string Message, GateDecision Decision, FocusSnapshot Focus)
{
    public override string ToString()
    {
        var asked = Decision.WaitMs is { } ms ? $"asked {ms,5:0.0}ms" : "unasked      ";
        return $"{At:HH:mm:ss.fff} {Decision.Verdict,-7} {Decision.Class?.ToString() ?? "-",-9} {Decision.Rule,-13} {asked} {Message}  [{Focus.OwnerLabel}]";
    }
}

/// <summary>
/// Wires the <see cref="MessagePumpHook"/> to the <see cref="KeyboardGate"/>
/// and, for buttons and the wheel, the <see cref="MouseGate"/> (ticket 16),
/// reading Chat Box focus per message (and reporting the edge to the session,
/// since a key can reach the hook before the tick has seen a focus gain), and
/// keeps one trace of what happened to both. After a waited call the
/// session's snapshot is refreshed and the commit queue drained right here,
/// before the next message (ADR-0002). The Toggle Key is the config's, and
/// flips Forwarding through the <see cref="Bridge"/>; the settings window's
/// "Press a key" runs through <see cref="BeginToggleKeyCapture"/>. Everything
/// here runs on the game's main thread (message pump and draw).
/// </summary>
internal sealed class KeyboardCapture : IDisposable
{
    private const int TraceCapacity = 200;

    private readonly Bridge bridge;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly ConfigStore config;
    private readonly KeyboardGate gate;
    private readonly MouseGate mouseGate;
    private readonly Queue<CaptureTraceEntry> trace = new(TraceCapacity);
    private readonly MessagePumpHook hook;
    private bool warnedOffThread;

    /// <param name="lastPlan">What the overlay drew last frame, for the mouse to hit-test; null when nothing was drawn.</param>
    public KeyboardCapture(Bridge bridge, IGameInteropProvider interop, IGameGui gui, IFramework framework, IPluginLog log, ConfigStore config, Func<CompositionPlan?> lastPlan)
    {
        this.bridge = bridge;
        this.gui = gui;
        this.framework = framework;
        this.log = log;
        this.config = config;
        gate = new KeyboardGate(new Win32KeyStateReader(), () => ChatBoxAccess.IsEmpty(gui)) { ToggleKey = config.Current.ToggleKey };
        mouseGate = new MouseGate(lastPlan);
        hook = new MessagePumpHook(interop, OnMessage, OnMouse);
        log.Information("Keyboard capture: DispatchMessageW import hooked");
    }

    public int Seen { get; private set; }
    public int Swallowed { get; private set; }
    public IEnumerable<CaptureTraceEntry> Trace => trace;

    /// <summary>How many messages fcitx5 was asked about, and the longest and mean wait, since the trace was last cleared.</summary>
    public int Asked { get; private set; }
    public double MaxWaitMs { get; private set; }
    public double MeanWaitMs => Asked == 0 ? 0 : totalWaitMs / Asked;
    private double totalWaitMs;

    /// <summary>Current focus state, for the debug window; main thread only.</summary>
    public FocusSnapshot ReadFocus() => ChatBoxFocus.Read(gui);

    /// <summary>A "Press a key" capture is waiting for its keydown.</summary>
    public bool CapturingToggleKey => gate.Capturing;

    /// <summary>
    /// The next non-modifier keydown, wherever focus is, is handed to
    /// <paramref name="onCaptured"/> with the modifiers held at it (null when it
    /// was Escape) and swallowed whole; the chord is the caller's to store.
    /// </summary>
    public void BeginToggleKeyCapture(Action<ToggleKey?> onCaptured) => gate.BeginCapture(onCaptured);

    public void CancelToggleKeyCapture() => gate.CancelCapture();

    public void ClearTrace()
    {
        trace.Clear();
        Asked = 0;
        MaxWaitMs = 0;
        totalWaitMs = 0;
    }

    private bool OnMessage(KeyMessage message) => Guarded(message, Decide);

    private bool OnMouse(MouseMessage message) => Guarded(message, DecideMouse);

    /// <summary>An exception in a decision would unwind through the game's message loop; fail open instead.</summary>
    private bool Guarded<TMessage>(TMessage message, Func<TMessage, bool> decide) where TMessage : struct
    {
        try
        {
            return OnMainThread() && decide(message);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Keyboard capture: {Message} passed through after an error", message);
            return false;
        }
    }

    /// <summary>
    /// Focus and the overlay's plan come from game memory and the draw, which
    /// are only safe on the main thread. The pump is expected to be that thread;
    /// if it is not, say so once and touch nothing.
    /// </summary>
    private bool OnMainThread()
    {
        if (framework.IsInFrameworkUpdateThread) return true;
        if (!warnedOffThread)
        {
            warnedOffThread = true;
            log.Warning("Keyboard capture: DispatchMessageW called off the framework thread; passing everything through");
        }
        return false;
    }

    private bool Decide(KeyMessage message)
    {
        var focus = ChatBoxFocus.Read(gui);
        var session = bridge.Session;
        session?.ObserveFocus(focus.ChatBoxFocused);
        gate.Session = session;
        gate.ToggleKey = config.Current.ToggleKey; // the settings window may have changed it since the last message
        var decision = gate.Decide(message, focus.ChatBoxFocused);
        if (decision.Asked) bridge.SettleAfterReply();

        Record(message.ToString(), decision, focus);

        switch (decision.Rule)
        {
            case GateRule.Toggle:
                bridge.SetForwarding(null);
                break;
            case GateRule.Captured:
                log.Information("Keyboard capture: Toggle Key capture ended by {Message}", message);
                break;
            case GateRule.SurrogateHalf:
                // The message names the code unit typed, so this goes where the trace goes (ticket 18).
                log.Debug("Keyboard capture: surrogate half {Message} passed unasked", message);
                break;
        }
        return decision.Verdict != GateVerdict.Pass;
    }

    /// <summary>The mouse over the overlay (ticket 16): the gate hit-tests the last frame's plan; a hit acts on the session's context and is swallowed.</summary>
    private bool DecideMouse(MouseMessage message)
    {
        mouseGate.Session = bridge.Session;
        var decision = mouseGate.Decide(message);
        if (decision.Acted) Record(message.ToString(), decision, ChatBoxFocus.Read(gui)); // nothing to read focus for otherwise
        return decision.Verdict != GateVerdict.Pass;
    }

    /// <summary>
    /// Counts every message, and traces only the ones the gate
    /// <see cref="GateDecision.Acted"/> on: the trace goes on the clipboard
    /// and into the log, so a key the gate let through untouched must leave no
    /// record of itself (ticket 18).
    /// </summary>
    private void Record(string message, GateDecision decision, FocusSnapshot focus)
    {
        Seen++;
        if (decision.Verdict != GateVerdict.Pass) Swallowed++;
        if (decision.WaitMs is { } ms)
        {
            Asked++;
            totalWaitMs += ms;
            if (ms > MaxWaitMs) MaxWaitMs = ms;
        }
        if (!decision.Acted) return;
        if (trace.Count == TraceCapacity) trace.Dequeue();
        trace.Enqueue(new CaptureTraceEntry(DateTime.Now, message, decision, focus));
        log.Debug("Keyboard capture: {Verdict} {Rule} {Message} ({Focus})", decision.Verdict, decision.Rule, message, focus);
    }

    public void Dispose() => hook.Dispose();
}
