using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using FfxivImeBridge.Fcitx.Diagnostics;
using FfxivImeBridge.Session;

namespace FfxivImeBridge;

/// <summary>
/// Runs <see cref="TransportProbe"/> off the game thread and keeps the latest
/// steps for the debug window. Every step also goes to Dalamud's log so the
/// result survives in dalamud.log. One climb at a time: a request while one
/// runs joins it.
/// </summary>
internal sealed class ProbeRunner(IPluginLog log, IChatGui chat) : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource disposal = new();
    private ImmutableArray<ProbeStep> steps = ImmutableArray<ProbeStep>.Empty;
    private ProbeReport? report;
    private Task<ProbeReport?>? running;

    public ImmutableArray<ProbeStep> Steps => steps;
    public ProbeReport? Report => Volatile.Read(ref report);
    public bool IsRunning => running is { IsCompleted: false };

    /// <summary>The diagnostic (<c>/imebridge probe</c>, the debug window): climb on a throwaway connection and say how it went in chat.</summary>
    public void Start() => _ = RunAsync(silent: false);

    /// <summary>
    /// Climb on a throwaway connection of its own (it never touches the live
    /// one). The report, or null when the climb crashed or the plugin unloaded;
    /// a climb already running is joined instead of doubled. The one-line
    /// result goes to chat unless <paramref name="silent"/> (an automatic
    /// Reconnect); the log gets every step regardless.
    /// </summary>
    public Task<ProbeReport?> RunAsync(bool silent)
    {
        lock (gate)
        {
            if (disposal.IsCancellationRequested) return Task.FromResult<ProbeReport?>(null);
            if (running is { IsCompleted: false } inFlight) return inFlight;
            steps = ImmutableArray<ProbeStep>.Empty;
            Volatile.Write(ref report, null);
            return running = Task.Run(() => ClimbAsync(silent));
        }
    }

    private async Task<ProbeReport?> ClimbAsync(bool silent)
    {
        log.Information("Transport ladder: starting{Silent}", silent ? " (silent)" : "");
        try
        {
            var result = await TransportProbe.RunAsync(onStep: OnStep, cancellationToken: disposal.Token);
            Volatile.Write(ref report, result);
            log.Information("Transport ladder: {Summary}", result.Summary);
            // A local client-side line (IChatGui.Print), not a sent message: ADR-0001 stands.
            if (!silent) chat.Print(Strings.ProbeResult(result.Summary));
            return result;
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested)
        {
            return null; // plugin unloaded mid-probe
        }
        catch (Exception ex)
        {
            log.Error(ex, "Transport ladder crashed");
            OnStep(new ProbeStep("crash", ProbeOutcome.Failed, ex.ToString()));
            return null;
        }
    }

    private void OnStep(ProbeStep step)
    {
        ImmutableInterlocked.Update(ref steps, static (list, s) => list.Add(s), step);
        if (step.Outcome == ProbeOutcome.Failed)
            log.Warning("Transport ladder: {Step}", step.ToString());
        else
            log.Information("Transport ladder: {Step}", step.ToString());
    }

    public void Dispose() => Dispose(SessionLifecycle.Grace);

    /// <summary>Cancels a running climb and waits for its cleanup within <paramref name="budget"/>, what is left of the unload's, so the throwaway connection is dropped before the assembly goes away.</summary>
    public void Dispose(TimeSpan budget)
    {
        Task? toAwait;
        lock (gate)
        {
            disposal.Cancel();
            toAwait = running;
        }
        try
        {
            if (toAwait is not null && budget > TimeSpan.Zero) toAwait.Wait(budget);
        }
        catch (AggregateException)
        {
            // Already logged by ClimbAsync.
        }
        disposal.Dispose();
    }
}
