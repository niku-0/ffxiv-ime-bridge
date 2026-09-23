using Dalamud.Plugin.Services;
using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Session;

/// <summary>The live <see cref="FcitxConnection"/> behind the <see cref="IInputContextFactory"/> seam.</summary>
internal sealed class FcitxContextFactory : IInputContextFactory
{
    private const string Program = "ffxiv-ime-bridge";

    private readonly FcitxConnection connection;
    private readonly IPluginLog log;

    public FcitxContextFactory(FcitxConnection connection, IPluginLog log)
    {
        this.connection = connection;
        this.log = log;
        connection.FcitxAvailabilityChanged += available => AvailabilityChanged?.Invoke(available);
        connection.Disconnected += reason => ConnectionLost?.Invoke(reason.Message);
    }

    public event Action<bool>? AvailabilityChanged;
    public event Action<string>? ConnectionLost;

    public async Task<IInputContextClient> CreateContextAsync(CancellationToken cancellationToken)
    {
        var context = await connection.CreateInputContextAsync(Program, CapabilityFlags.ClientDrawsComposition, cancellationToken).ConfigureAwait(false);
        log.Information("Session: input context {Path} created", context.Path);
        return new InputContextClient(context, log);
    }
}

/// <summary>
/// One real <see cref="InputContext"/> behind the seam. The calls are sent
/// fire-and-forget (D-Bus keeps them in order on one connection) except the
/// key the Gate waits on; a failure is logged, never thrown into the game
/// thread. Events pass straight through on the reader thread; <c>ForwardKey</c>
/// is only logged in M1.
/// </summary>
internal sealed class InputContextClient : IInputContextClient
{
    private readonly InputContext context;
    private readonly IPluginLog log;

    public InputContextClient(InputContext context, IPluginLog log)
    {
        this.context = context;
        this.log = log;
        context.InputMethodChanged += info => InputMethodChanged?.Invoke(info);
        context.Committed += text => Committed?.Invoke(text);
        context.ForwardKey += key => log.Information("Session: ForwardKey ignored: keysym 0x{KeySym:X} state {State} release={Release}", key.KeySym, key.State, key.IsRelease);
    }

    public InputMethodInfo? CurrentInputMethod => context.CurrentInputMethod;

    public CompositionState State => context.State;

    public event Action<InputMethodInfo>? InputMethodChanged;
    public event Action<string>? Committed;

    public void FocusIn() => Forget(() => context.FocusInAsync(), "FocusIn");
    public void FocusOut() => Forget(() => context.FocusOutAsync(), "FocusOut");
    public void Reset() => Forget(() => context.ResetAsync(), "Reset");

    public Task<bool> ProcessKeyAsync(KeyEvent key)
    {
        Task<bool> call;
        try
        {
            call = context.ProcessKeyEventAsync(key);
        }
        catch (Exception ex)
        {
            log.Warning("Session: ProcessKeyEvent on {Path} failed: {Error}", context.Path, ex.Message);
            return Task.FromException<bool>(ex);
        }
        LogFault(call, "ProcessKeyEvent");
        return call;
    }

    public void SendKey(KeyEvent key) => Forget(() => context.ProcessKeyEventAsync(key), "ProcessKeyEvent");

    public void SelectCandidate(int index) => Forget(() => context.SelectCandidateAsync(index), "SelectCandidate");
    public void NextPage() => Forget(() => context.NextPageAsync(), "NextPage");
    public void PreviousPage() => Forget(() => context.PreviousPageAsync(), "PrevPage");

    public ValueTask DisposeAsync() => context.DisposeAsync();

    public void Abandon() => context.Abandon();

    /// <summary>Send and move on; a failure (connection gone, context destroyed) is logged, never thrown into the message pump.</summary>
    private void Forget(Func<Task> send, string name)
    {
        Task call;
        try
        {
            call = send();
        }
        catch (Exception ex)
        {
            log.Warning("Session: {Call} on {Path} failed: {Error}", name, context.Path, ex.Message);
            return;
        }
        LogFault(call, name);
    }

    private void LogFault(Task call, string name) => call.ContinueWith(
        t => log.Warning("Session: {Call} on {Path} failed: {Error}", name, context.Path, t.Exception?.InnerException?.Message ?? "unknown"),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
