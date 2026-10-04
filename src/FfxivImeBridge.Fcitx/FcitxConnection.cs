using Tmds.DBus.Protocol;

namespace FfxivImeBridge.Fcitx;

/// <summary>
/// A session-bus connection to fcitx5. Creates <see cref="InputContext"/>s and
/// exposes the controller calls a client needs (current input method) and
/// fcitx5's tray icon.
/// Disposing the connection destroys every input context created on it. A
/// connection that dies under the client (the bus restarting, the socket
/// closing) says so once through <see cref="Disconnected"/> and is then only
/// good for disposing.
/// </summary>
public sealed class FcitxConnection : IDisposable
{
    private readonly DBusConnection connection;
    private IDisposable? availabilityWatch;
    private IDisposable? trayIconWatch;
    private volatile TrayItem? trayItem;
    private volatile bool disposing;

    private FcitxConnection(DBusConnection connection)
    {
        this.connection = connection;
        connection.DisconnectedAsync().ContinueWith(
            t =>
            {
                if (disposing) return; // our own Dispose is not a loss
                Disconnected?.Invoke(t.IsCompletedSuccessfully ? t.Result ?? new IOException("The D-Bus connection closed.") : t.Exception?.InnerException ?? new IOException("The D-Bus connection closed."));
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// The connection died under us, with the reason. Raised once, from the
    /// connection's own thread, never for our own <see cref="Dispose"/>. Every
    /// input context on it is gone with it; nothing on it is recreated.
    /// </summary>
    public event Action<Exception>? Disconnected;

    /// <summary>
    /// The watched name appeared on (<see langword="true"/>) or left
    /// (<see langword="false"/>) the bus; see <see cref="WatchAvailabilityAsync"/>.
    /// Raised on the connection's receive thread.
    /// </summary>
    public event Action<bool>? FcitxAvailabilityChanged;

    /// <summary>
    /// fcitx5's tray icon may have changed: its tray item sent <c>NewIcon</c>,
    /// or any tray item did while fcitx5's is not found yet. Read the icon with
    /// <see cref="GetTrayIconNameAsync"/>. Raised on the connection's receive
    /// thread once <see cref="WatchTrayIconAsync"/> has subscribed.
    /// </summary>
    public event Action? TrayIconChanged;

    /// <summary>
    /// Connect to the session bus. <paramref name="address"/> defaults to
    /// <c>DBUS_SESSION_BUS_ADDRESS</c>; pass it explicitly when the environment
    /// variable is not visible (e.g. inside Wine).
    /// </summary>
    public static Task<FcitxConnection> ConnectAsync(string? address = null, CancellationToken cancellationToken = default)
        => ConnectAsync(new FcitxConnectionOptions { Address = address }, cancellationToken);

    /// <summary>Connect with explicit transport settings (see <see cref="FcitxConnectionOptions"/>).</summary>
    public static async Task<FcitxConnection> ConnectAsync(FcitxConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var connection = new DBusConnection(options.ToDBusOptions());
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        return new FcitxConnection(connection);
    }

    /// <summary>Whether fcitx5 currently owns its bus name.</summary>
    public async Task<bool> IsFcitxAvailableAsync(CancellationToken cancellationToken = default)
    {
        var services = await connection.ListServicesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return services.Contains(Fcitx5Names.BusName);
    }

    /// <summary>
    /// Subscribe to the bus's <c>NameOwnerChanged</c> for <paramref name="name"/>
    /// (fcitx5's by default) and raise <see cref="FcitxAvailabilityChanged"/> when its
    /// owner comes or goes. A name changing hands is reported as leaving and then
    /// appearing. Only one watch is kept; calling again replaces it.
    /// </summary>
    public async Task WatchAvailabilityAsync(string name = Fcitx5Names.BusName, CancellationToken cancellationToken = default)
    {
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Sender = "org.freedesktop.DBus",
            Interface = "org.freedesktop.DBus",
            Member = "NameOwnerChanged",
            Arg0 = name,
        };
        var watch = await connection.AddMatchAsync(
            rule,
            static (Message m, object? _) =>
            {
                // s s s : name, old owner, new owner (empty when there is none)
                var reader = m.GetBodyReader();
                reader.ReadString();
                var oldOwner = reader.ReadString();
                var newOwner = reader.ReadString();
                return (HadOwner: oldOwner.Length > 0, HasOwner: newOwner.Length > 0);
            },
            (Notification<(bool HadOwner, bool HasOwner)> n) =>
            {
                if (!n.HasValue) return;
                var (had, has) = n.Value;
                if (had && has)
                {
                    // Handed from one connection to another: the old one is gone before the new one is up.
                    FcitxAvailabilityChanged?.Invoke(false);
                    FcitxAvailabilityChanged?.Invoke(true);
                }
                else if (had != has)
                {
                    FcitxAvailabilityChanged?.Invoke(has);
                }
            },
            emitOnCapturedContext: false,
            flags: ObserverFlags.None,
            state: null).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref availabilityWatch, watch)?.Dispose();
    }

    /// <summary>
    /// Subscribe to the tray items' <c>NewIcon</c> and raise
    /// <see cref="TrayIconChanged"/> for fcitx5's. Only one watch is kept;
    /// calling again replaces it.
    /// </summary>
    public async Task WatchTrayIconAsync(CancellationToken cancellationToken = default)
    {
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = Fcitx5Names.StatusNotifierItemInterface,
            Member = "NewIcon",
        };
        var watch = await connection.AddMatchAsync(
            rule,
            static (Message m, object? _) => m.SenderAsString ?? "",
            (Notification<string> n) =>
            {
                if (!n.HasValue) return;
                if (trayItem is not { } item || item.Service == n.Value) TrayIconChanged?.Invoke();
            },
            emitOnCapturedContext: false,
            flags: ObserverFlags.None,
            state: null).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref trayIconWatch, watch)?.Dispose();
    }

    /// <summary>
    /// The icon fcitx5's tray item shows: the input method of the Input Context
    /// fcitx5 last had focused and, for Mozc, its mode (<c>fcitx_mozc_direct</c>,
    /// <c>fcitx_mozc_hiragana</c> …). Null when fcitx5 has no tray item (the
    /// desktop has no tray) or the item draws a text label instead of a named
    /// icon (classicui's <c>PreferTextIcon</c>).
    /// </summary>
    public async Task<string?> GetTrayIconNameAsync(CancellationToken cancellationToken = default)
    {
        if (trayItem is { } known)
        {
            try
            {
                return await GetIconNameAsync(known, cancellationToken).ConfigureAwait(false);
            }
            catch (DBusErrorReplyException)
            {
                trayItem = null; // fcitx5 restarted, or the tray went and took the item with it: look it up again
            }
        }
        if (await FindTrayItemAsync(cancellationToken).ConfigureAwait(false) is not { } found) return null;
        trayItem = found;
        return await GetIconNameAsync(found, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetIconNameAsync(TrayItem item, CancellationToken cancellationToken)
    {
        var icon = (await GetPropertyAsync(item.Service, item.Path, Fcitx5Names.StatusNotifierItemInterface, "IconName", cancellationToken).ConfigureAwait(false)).GetString();
        return icon.Length > 0 ? icon : null;
    }

    /// <summary>
    /// fcitx5's entry among the tray's registered items; null when there is no
    /// tray or fcitx5 is not registered with it. Every item is asked for its
    /// <c>Id</c> at once, so one that never answers holds nothing up.
    /// </summary>
    private async Task<TrayItem?> FindTrayItemAsync(CancellationToken cancellationToken)
    {
        string[] registered;
        try
        {
            registered = (await GetPropertyAsync(Fcitx5Names.StatusNotifierWatcherName, Fcitx5Names.StatusNotifierWatcherPath, Fcitx5Names.StatusNotifierWatcherName, "RegisteredStatusNotifierItems", cancellationToken).ConfigureAwait(false)).GetArray<string>();
        }
        catch (DBusErrorReplyException)
        {
            return null; // no tray on this desktop
        }
        await foreach (var lookup in Task.WhenEach(registered.Select(entry => FcitxOrNullAsync(TrayItem.Parse(entry), cancellationToken))))
        {
            if (await lookup.ConfigureAwait(false) is { } fcitx) return fcitx;
        }
        return null;
    }

    private async Task<TrayItem?> FcitxOrNullAsync(TrayItem item, CancellationToken cancellationToken)
    {
        try
        {
            var id = await GetPropertyAsync(item.Service, item.Path, Fcitx5Names.StatusNotifierItemInterface, "Id", cancellationToken).ConfigureAwait(false);
            return id.GetString() == Fcitx5Names.TrayItemId ? item : null;
        }
        catch (DBusErrorReplyException)
        {
            return null; // gone since it registered
        }
    }

    private Task<VariantValue> GetPropertyAsync(string destination, string path, string @interface, string property, CancellationToken cancellationToken)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, "org.freedesktop.DBus.Properties", "Get", "ss", MethodCall.Flags);
        writer.WriteString(@interface);
        writer.WriteString(property);
        return connection.CallMethodAsync(writer.CreateMessage(), static (m, _) => m.GetBodyReader().ReadVariantValue()).WaitAsync(cancellationToken);
    }

    /// <summary>A tray item's address as the watcher lists it: <c>service/path</c>, or a bare service at the default path.</summary>
    private sealed record TrayItem(string Service, string Path)
    {
        public static TrayItem Parse(string registered) => registered.IndexOf('/') is var slash and > 0
            ? new(registered[..slash], registered[slash..])
            : new(registered, Fcitx5Names.StatusNotifierItemDefaultPath);
    }

    /// <summary>fcitx5's <c>InputMethod1.Version</c>.</summary>
    public Task<uint> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Fcitx5Names.InputMethodPath, Fcitx5Names.InputMethodInterface, "Version", flags: MethodCall.Flags);
        return connection.CallMethodAsync(writer.CreateMessage(), static (m, _) => m.GetBodyReader().ReadUInt32()).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Create an input context. <paramref name="program"/> is what fcitx5 shows
    /// as the client name. Capabilities are applied before returning.
    /// </summary>
    public async Task<InputContext> CreateInputContextAsync(
        string program,
        CapabilityFlags capabilities = CapabilityFlags.ClientDrawsComposition,
        CancellationToken cancellationToken = default)
    {
        MessageBuffer message;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(Fcitx5Names.BusName, Fcitx5Names.InputMethodPath, Fcitx5Names.InputMethodInterface, "CreateInputContext", "a(ss)", MethodCall.Flags);
            var array = writer.WriteArrayStart(DBusType.Struct);
            writer.WriteStructureStart();
            writer.WriteString("program");
            writer.WriteString(program);
            writer.WriteArrayEnd(array);
            message = writer.CreateMessage();
        }

        var (path, uuid) = await connection.CallMethodAsync(message, static (m, _) =>
        {
            var reader = m.GetBodyReader();
            var path = reader.ReadObjectPathAsString();
            var uuid = reader.ReadArrayOfByte();
            return (path, uuid);
        }).WaitAsync(cancellationToken).ConfigureAwait(false);

        var context = new InputContext(connection, path, uuid);
        try
        {
            await context.SubscribeAsync(cancellationToken).ConfigureAwait(false);
            await context.SetCapabilityAsync(capabilities, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return context;
    }

    /// <summary>Unique name of the input method fcitx5 currently has active (e.g. <c>mozc</c>, <c>keyboard-us</c>).</summary>
    public Task<string> GetCurrentInputMethodAsync(CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Fcitx5Names.ControllerPath, Fcitx5Names.ControllerInterface, "CurrentInputMethod", flags: MethodCall.Flags);
        return connection.CallMethodAsync(writer.CreateMessage(), static (m, _) => m.GetBodyReader().ReadString()).WaitAsync(cancellationToken);
    }

    /// <summary>Switch fcitx5's active input method globally. Affects every application, not just this client.</summary>
    public Task SetCurrentInputMethodAsync(string uniqueName, CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, Fcitx5Names.ControllerPath, Fcitx5Names.ControllerInterface, "SetCurrentIM", "s", MethodCall.Flags);
        writer.WriteString(uniqueName);
        return connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(cancellationToken);
    }

    /// <summary>Introspection XML of an object on fcitx5, for diagnostics.</summary>
    public Task<string> IntrospectAsync(string path, CancellationToken cancellationToken = default)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Fcitx5Names.BusName, path, "org.freedesktop.DBus.Introspectable", "Introspect", flags: MethodCall.Flags);
        return connection.CallMethodAsync(writer.CreateMessage(), static (m, _) => m.GetBodyReader().ReadString()).WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        disposing = true;
        availabilityWatch?.Dispose();
        trayIconWatch?.Dispose();
        connection.Dispose();
    }
}
