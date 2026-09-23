using System.Collections.Immutable;
using Tmds.DBus.Protocol;

namespace FfxivImeBridge.Fcitx;

/// <summary>
/// One <c>org.fcitx.Fcitx.InputContext1</c> object. Keys go in through
/// <see cref="ProcessKeyEventAsync"/>; what to draw comes back as
/// <see cref="State"/> (updated before the key call returns) and committed text
/// through <see cref="Committed"/>.
/// </summary>
/// <remarks>
/// The preedit arrives through <c>UpdateFormattedPreedit</c> and everything else
/// (aux text, candidates, paging) through <c>UpdateClientSideUI</c>; both are
/// folded into <see cref="State"/>, so it may change twice per key.
///
/// Events fire on the connection's receive thread. fcitx5 emits its signals
/// before it replies to the method call that caused them, and the reply is
/// only completed after those signals have been dispatched, so by the time
/// <c>await ProcessKeyEventAsync(...)</c> returns, <see cref="State"/> and any
/// <see cref="Committed"/> text already reflect that key.
/// </remarks>
public sealed class InputContext : IAsyncDisposable
{
    private readonly DBusConnection connection;
    private readonly List<IDisposable> subscriptions = new();
    private CompositionState state = CompositionState.Idle;
    private bool disposed;

    internal InputContext(DBusConnection connection, string path, byte[] uuid)
    {
        this.connection = connection;
        Path = path;
        Uuid = uuid;
    }

    public string Path { get; }
    public byte[] Uuid { get; }

    /// <summary>Latest snapshot from <c>UpdateClientSideUI</c>.</summary>
    public CompositionState State => Volatile.Read(ref state);

    /// <summary>Input method fcitx5 last reported for this context (after <c>FocusIn</c> or a switch); null until then.</summary>
    public InputMethodInfo? CurrentInputMethod { get; private set; }

    public event Action<CompositionState>? StateChanged;
    /// <summary>Raised for every <c>UpdateClientSideUI</c> specifically (after it is merged into <see cref="State"/>); <see cref="StateChanged"/> also fires for the preedit signal.</summary>
    public event Action<CompositionState>? PanelUpdated;
    public event Action<string>? Committed;
    /// <summary>fcitx5 wants this key delivered to the application as if it were typed (it handled the press but is passing it on).</summary>
    public event Action<KeyEvent>? ForwardKey;
    public event Action<InputMethodInfo>? InputMethodChanged;
    /// <summary>(offset, size): delete text around the cursor. Not used by Mozc in a plain text field; surfaced for completeness.</summary>
    public event Action<(int Offset, uint Size)>? DeleteSurroundingText;

    internal async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        subscriptions.Add(await Watch("UpdateFormattedPreedit", ReadFormattedPreedit, n => Set(State with { Preedit = n })).AsTask()
                                       .WaitAsync(cancellationToken).ConfigureAwait(false));
        subscriptions.Add(await Watch("UpdateClientSideUI", ReadClientSideUI, n => { Set(n with { Preedit = State.Preedit }); PanelUpdated?.Invoke(State); }).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false));
        subscriptions.Add(await Watch("CommitString", static (m, _) => m.GetBodyReader().ReadString(), n => Committed?.Invoke(n)).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false));
        subscriptions.Add(await Watch("ForwardKey", ReadForwardKey, n => ForwardKey?.Invoke(n)).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false));
        subscriptions.Add(await Watch("CurrentIM", ReadCurrentIM, n => { CurrentInputMethod = n; InputMethodChanged?.Invoke(n); }).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false));
        subscriptions.Add(await Watch("DeleteSurroundingText", ReadDeleteSurrounding, n => DeleteSurroundingText?.Invoke(n)).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false));
    }

    private ValueTask<IDisposable> Watch<T>(string signal, MessageValueReader<T> reader, Action<T> handler)
    {
        return connection.WatchSignalAsync(
            Fcitx5Names.BusName, Path, Fcitx5Names.InputContextInterface, signal,
            reader,
            (Notification<T> n) => { if (n.HasValue) handler(n.Value); },
            flags: ObserverFlags.None,
            emitOnCapturedContext: false,
            state: null);
    }

    private void Set(CompositionState next)
    {
        Volatile.Write(ref state, next);
        StateChanged?.Invoke(next);
    }

    /// <summary>Returns true when fcitx5 consumed the key; false means the application should handle it itself.</summary>
    public Task<bool> ProcessKeyEventAsync(KeyEvent key, CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Path, Fcitx5Names.InputContextInterface, "ProcessKeyEvent", "uuubu", MethodCall.Flags);
        writer.WriteUInt32(key.KeySym);
        writer.WriteUInt32(key.KeyCode);
        writer.WriteUInt32((uint)key.State);
        writer.WriteBool(key.IsRelease);
        writer.WriteUInt32(key.Time);
        return connection.CallMethodAsync(writer.CreateMessage(), static (m, _) => m.GetBodyReader().ReadBool()).WaitAsync(cancellationToken);
    }

    public Task FocusInAsync(CancellationToken cancellationToken = default) => Call("FocusIn", cancellationToken);
    public Task FocusOutAsync(CancellationToken cancellationToken = default) => Call("FocusOut", cancellationToken);
    /// <summary>Abandon the current composition without committing.</summary>
    public Task ResetAsync(CancellationToken cancellationToken = default) => Call("Reset", cancellationToken);
    public Task NextPageAsync(CancellationToken cancellationToken = default) => Call("NextPage", cancellationToken);
    public Task PreviousPageAsync(CancellationToken cancellationToken = default) => Call("PrevPage", cancellationToken);

    /// <summary>Select a candidate by its index in <see cref="CompositionState.Candidates"/> (current page).</summary>
    public Task SelectCandidateAsync(int index, CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Path, Fcitx5Names.InputContextInterface, "SelectCandidate", "i", MethodCall.Flags);
        writer.WriteInt32(index);
        return connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(cancellationToken);
    }

    public Task SetCapabilityAsync(CapabilityFlags capabilities, CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Path, Fcitx5Names.InputContextInterface, "SetCapability", "t", MethodCall.Flags);
        writer.WriteUInt64((ulong)capabilities);
        return connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(cancellationToken);
    }

    /// <summary>Tell fcitx5 where the cursor is on screen, in case it ever draws anything itself.</summary>
    public Task SetCursorRectAsync(int x, int y, int width, int height, CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Path, Fcitx5Names.InputContextInterface, "SetCursorRect", "iiii", MethodCall.Flags);
        writer.WriteInt32(x);
        writer.WriteInt32(y);
        writer.WriteInt32(width);
        writer.WriteInt32(height);
        return connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(cancellationToken);
    }

    private Task Call(string member, CancellationToken cancellationToken)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Path, Fcitx5Names.InputContextInterface, member, flags: MethodCall.Flags);
        return connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(cancellationToken);
    }

    /// <summary>Destroy the context on fcitx5's side and stop listening. Safe to call twice.</summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            await Call("DestroyIC", CancellationToken.None).ConfigureAwait(false);
        }
        catch (DBusExceptionBase)
        {
            // Connection gone or context already destroyed: nothing left to release.
        }
        Unsubscribe();
    }

    /// <summary>
    /// Stop listening and let the context go without a <c>DestroyIC</c>: fcitx5
    /// reaped its side when it left the bus, so the call would only reach a
    /// fcitx5 the bus started for it (ticket 18). Safe to call twice, and after
    /// <see cref="DisposeAsync"/>.
    /// </summary>
    public void Abandon()
    {
        disposed = true;
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        foreach (var subscription in subscriptions) subscription.Dispose();
        subscriptions.Clear();
    }

    // --- signal body readers: signatures verified by live introspection (README.md) ---

    private static Preedit ReadFormattedPreedit(Message message, object? _)
    {
        // a(si) i
        var reader = message.GetBodyReader();
        var segments = ReadFormattedText(ref reader);
        var cursor = reader.ReadInt32();
        return new Preedit(segments, cursor);
    }

    private static CompositionState ReadClientSideUI(Message message, object? _)
    {
        // a(si) i a(si) a(si) a(ss) i i b b
        // The preedit here is the input panel's, which stays empty while we
        // declare the Preedit capability: the real one arrives via
        // UpdateFormattedPreedit and is merged in by the caller.
        var reader = message.GetBodyReader();
        var panelPreedit = ReadFormattedText(ref reader);
        var panelCursor = reader.ReadInt32();
        var auxUp = ReadFormattedText(ref reader);
        var auxDown = ReadFormattedText(ref reader);
        var candidates = ImmutableArray.CreateBuilder<Candidate>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var label = reader.ReadString();
            var text = reader.ReadString();
            candidates.Add(new Candidate(label, text));
        }
        var selected = reader.ReadInt32();
        var layout = (CandidateLayout)reader.ReadInt32();
        var hasPrev = reader.ReadBool();
        var hasNext = reader.ReadBool();
        return new CompositionState(new Preedit(panelPreedit, panelCursor), auxUp, auxDown, candidates.ToImmutable(), selected, layout, hasPrev, hasNext);
    }

    private static ImmutableArray<TextSegment> ReadFormattedText(ref Reader reader)
    {
        var segments = ImmutableArray.CreateBuilder<TextSegment>();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var text = reader.ReadString();
            var format = (TextFormat)reader.ReadInt32();
            segments.Add(new TextSegment(text, format));
        }
        return segments.ToImmutable();
    }

    private static KeyEvent ReadForwardKey(Message message, object? _)
    {
        // u u b : keysym, state, isRelease
        var reader = message.GetBodyReader();
        var keySym = reader.ReadUInt32();
        var state = (KeyState)reader.ReadUInt32();
        var isRelease = reader.ReadBool();
        return new KeyEvent(keySym, KeyCode: 0, state, isRelease);
    }

    private static InputMethodInfo ReadCurrentIM(Message message, object? _)
    {
        // s s s : name, uniqueName, languageCode
        var reader = message.GetBodyReader();
        return new InputMethodInfo(reader.ReadString(), reader.ReadString(), reader.ReadString());
    }

    private static (int, uint) ReadDeleteSurrounding(Message message, object? _)
    {
        var reader = message.GetBodyReader();
        return (reader.ReadInt32(), reader.ReadUInt32());
    }
}
