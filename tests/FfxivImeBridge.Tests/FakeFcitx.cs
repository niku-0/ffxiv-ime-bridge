using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.Session;

namespace FfxivImeBridge.Tests;

/// <summary>A scripted stand-in for one fcitx5 Input Context: records the calls the plugin makes and lets a test raise the events fcitx5 would.</summary>
internal sealed class FakeInputContext : IInputContextClient
{
    public List<string> Calls { get; } = new();
    public bool Disposed { get; private set; }

    /// <summary>Let go without a <c>DestroyIC</c> (ticket 18); <see cref="Disposed"/> stays false.</summary>
    public bool Abandoned { get; private set; }

    /// <summary>Keys the Gate waited on, in order.</summary>
    public List<KeyEvent> Asked { get; } = new();
    /// <summary>Keys sent fire-and-forget, in order.</summary>
    public List<KeyEvent> Sent { get; } = new();

    /// <summary>What <see cref="ProcessKeyAsync"/> answers when nothing is scripted: whether fcitx5 consumed the key.</summary>
    public bool Handled { get; set; }

    /// <summary>Scripted replies, consumed one per <see cref="ProcessKeyAsync"/> before <see cref="Handled"/> applies; a never-completing task is a timeout.</summary>
    public Queue<Task<bool>> Replies { get; } = new();

    public CompositionState State { get; set; } = CompositionState.Idle;

    public event Action<InputMethodInfo>? InputMethodChanged;
    public event Action<string>? Committed;

    public void FocusIn() => Calls.Add("FocusIn");
    public void FocusOut() => Calls.Add("FocusOut");
    public void Reset() => Calls.Add("Reset");

    public Task<bool> ProcessKeyAsync(KeyEvent key)
    {
        Asked.Add(key);
        return Replies.TryDequeue(out var reply) ? reply : Task.FromResult(Handled);
    }

    public void SendKey(KeyEvent key) => Sent.Add(key);

    public void SelectCandidate(int index) => Calls.Add($"SelectCandidate {index}");
    public void NextPage() => Calls.Add("NextPage");
    public void PreviousPage() => Calls.Add("PrevPage");

    /// <summary>When set, <see cref="DisposeAsync"/> waits on it: the teardown a hung fcitx5 never answers (ticket 19).</summary>
    public TaskCompletionSource? BlockDisposal { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (BlockDisposal is { } block) await block.Task;
        Disposed = true;
        Calls.Add("Dispose");
    }

    public void Abandon()
    {
        Abandoned = true;
        Calls.Add("Abandon");
    }

    /// <summary>What fcitx5 sends as <c>CurrentIM</c> after every <c>FocusIn</c>.</summary>
    public void RaiseInputMethod(string uniqueName)
    {
        InputMethodChanged?.Invoke(new InputMethodInfo(uniqueName, uniqueName, ""));
    }

    public void RaiseCommit(string text) => Committed?.Invoke(text);

    /// <summary>Put a preedit up, as Mozc does after the first handled key: a Composition is active.</summary>
    public void Compose(string preedit) => State = State with { Preedit = new Preedit([new TextSegment(preedit, TextFormat.Underline)], 0) };

    public void StopComposing() => State = CompositionState.Idle;
}

/// <summary>The fake bus: hands out <see cref="FakeInputContext"/>s and lets a test take fcitx5 off the bus and put it back.</summary>
internal sealed class FakeFcitx : IInputContextFactory
{
    public List<FakeInputContext> Created { get; } = new();

    /// <summary>When set, the next <see cref="CreateContextAsync"/> returns this instead of a fresh completed context.</summary>
    public TaskCompletionSource<IInputContextClient>? PendingCreation { get; set; }

    public event Action<bool>? AvailabilityChanged;
    public event Action<string>? ConnectionLost;
    public event Action? TrayIconChanged;

    /// <summary>What <see cref="GetTrayIconNameAsync"/> answers when nothing is scripted.</summary>
    public string? TrayIcon { get; set; }

    /// <summary>Scripted answers, consumed one per <see cref="GetTrayIconNameAsync"/> before <see cref="TrayIcon"/> applies; an incomplete task is a read still under way.</summary>
    public Queue<Task<string?>> TrayIconReplies { get; } = new();

    public Task<string?> GetTrayIconNameAsync(CancellationToken cancellationToken) =>
        TrayIconReplies.TryDequeue(out var reply) ? reply : Task.FromResult(TrayIcon);

    /// <summary>fcitx5's tray item says <c>NewIcon</c>, now showing <paramref name="icon"/>.</summary>
    public void ChangeTrayIcon(string? icon)
    {
        TrayIcon = icon;
        TrayIconChanged?.Invoke();
    }

    public Task<IInputContextClient> CreateContextAsync(CancellationToken cancellationToken)
    {
        if (PendingCreation is { } pending)
        {
            PendingCreation = null;
            return pending.Task;
        }
        var context = new FakeInputContext();
        Created.Add(context);
        return Task.FromResult<IInputContextClient>(context);
    }

    public void Leave() => AvailabilityChanged?.Invoke(false);
    public void Return() => AvailabilityChanged?.Invoke(true);

    /// <summary>The socket under the connection went away (ticket 13).</summary>
    public void Die(string reason) => ConnectionLost?.Invoke(reason);
}

/// <summary>The modifier keys as a test says they stand.</summary>
internal sealed class FakeKeyState : IKeyStateReader
{
    public Modifiers Modifiers { get; set; }

    public Modifiers Read() => Modifiers;
}
