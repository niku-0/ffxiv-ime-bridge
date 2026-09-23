using FfxivImeBridge.Fcitx;

namespace FfxivImeBridge.Session;

/// <summary>
/// What the plugin needs from one fcitx5 Input Context: the calls it makes and
/// the events it consumes. The real one wraps <see cref="InputContext"/>; the
/// tests substitute a scripted fake. Every call but <see cref="ProcessKeyAsync"/>
/// is fire-and-forget; events are raised on the D-Bus reader thread and must be
/// queued before touching anything of the game's.
/// </summary>
internal interface IInputContextClient : IAsyncDisposable
{
    /// <summary>The input method fcitx5 last reported for this context; null until the first <c>CurrentIM</c>.</summary>
    InputMethodInfo? CurrentInputMethod { get; }

    /// <summary>What fcitx5 last asked to have drawn; already updated when a <see cref="ProcessKeyAsync"/> reply arrives. Any thread.</summary>
    CompositionState State { get; }

    /// <summary>fcitx5's <c>CurrentIM</c>: after every <c>FocusIn</c> and every switch.</summary>
    event Action<InputMethodInfo>? InputMethodChanged;

    /// <summary>fcitx5's <c>CommitString</c>.</summary>
    event Action<string>? Committed;

    /// <summary>The Chat Box gained focus: keys will follow.</summary>
    void FocusIn();
    /// <summary>The Chat Box lost focus. Mozc commits on this unless <see cref="Reset"/> came first.</summary>
    void FocusOut();
    /// <summary>Abandon the composition without committing.</summary>
    void Reset();

    /// <summary>
    /// The one call the Gate waits on (ADR-0002): <see langword="true"/> when
    /// fcitx5 consumed the key. The task must never need the game thread to
    /// complete; a fault (connection gone) is the implementation's to log, and
    /// the Gate treats it as no reply.
    /// </summary>
    Task<bool> ProcessKeyAsync(KeyEvent key);

    /// <summary>A key fcitx5 is told about but not asked about: modifier presses, and the release of every press it was told about.</summary>
    void SendKey(KeyEvent key);

    /// <summary>Mouse selection (ticket 16): the candidate at <paramref name="index"/> on the current page, as fcitx5's own panel would.</summary>
    void SelectCandidate(int index);
    void NextPage();
    void PreviousPage();

    /// <summary>
    /// Let the context go without telling fcitx5, for when fcitx5 or the
    /// connection is gone: <see cref="IAsyncDisposable.DisposeAsync"/>'s
    /// <c>DestroyIC</c> would be a method call to an unowned bus name, which
    /// the bus would answer by starting fcitx5 again (ticket 18).
    /// </summary>
    void Abandon();
}

/// <summary>Where Input Contexts come from, and whether fcitx5 is on the bus to serve them.</summary>
internal interface IInputContextFactory
{
    /// <summary>fcitx5 appeared on (<see langword="true"/>) or left (<see langword="false"/>) the bus. Reader thread.</summary>
    event Action<bool>? AvailabilityChanged;

    /// <summary>The connection under the contexts died, with the reason; nothing on it can be recreated. Reader thread.</summary>
    event Action<string>? ConnectionLost;

    /// <summary>A fresh context with <see cref="CapabilityFlags.ClientDrawsComposition"/>, starting on fcitx5's default input method.</summary>
    Task<IInputContextClient> CreateContextAsync(CancellationToken cancellationToken);
}
