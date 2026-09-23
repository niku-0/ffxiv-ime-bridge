using System.Collections.Immutable;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FfxivImeBridge.Fcitx.Diagnostics;

/// <summary>
/// Climbs the transport ladder from the spec one rung at a time and reports
/// where it stops: environment → address → raw AF_UNIX socket → connect →
/// D-Bus authentication → fcitx5 present → input context → focus → input
/// method → key → preedit → client-side UI. Each rung is a separate step so
/// that a failure inside Wine names the exact call that broke (socket
/// creation, connect, or auth), which is what decides whether the plugin
/// needs a helper process. The rungs from "input-method" on are about
/// fcitx5's setup (is Mozc there?), not about the transport.
/// </summary>
public static class TransportProbe
{
    public const string PreeditRung = "preedit";

    public static async Task<ProbeReport> RunAsync(ProbeSettings? settings = null, Action<ProbeStep>? onStep = null, CancellationToken cancellationToken = default)
    {
        settings ??= new ProbeSettings();
        var run = new Ladder(settings, onStep, cancellationToken);
        await run.ClimbAsync().ConfigureAwait(false);
        return new ProbeReport(run.Steps.ToImmutableArray(), run.AuthenticatedWith);
    }

    /// <summary>One climb: the rungs in order, plus the connection and context they build up.</summary>
    private sealed class Ladder
    {
        private readonly ProbeSettings settings;
        private readonly Action<ProbeStep>? onStep;
        private readonly CancellationToken cancellationToken;
        private readonly (string Name, Func<CancellationToken, Task<string>> Climb)[] rungs;

        public List<ProbeStep> Steps { get; } = new();

        /// <summary>Set once "authenticate" passes: the options a live connection can reuse.</summary>
        public FcitxConnectionOptions? AuthenticatedWith { get; private set; }

        private SessionBusAddress? address;
        private uint? unixUserId;
        private FcitxConnection? connection;
        private InputContext? context;
        private string? previousInputMethod;
        private int panelUpdates;

        public Ladder(ProbeSettings settings, Action<ProbeStep>? onStep, CancellationToken cancellationToken)
        {
            this.settings = settings;
            this.onStep = onStep;
            this.cancellationToken = cancellationToken;
            rungs =
            [
                ("platform", _ => Task.FromResult(Platform())),
                ("environment", _ => Task.FromResult(ReadEnvironment())),
                ("address", _ => Task.FromResult(ParseAddress())),
                ("socket", _ => Task.FromResult(CreateSocket())),
                ("connect", ConnectRawAsync),
                ("authenticate", AuthenticateAsync),
                ("fcitx5", Fcitx5Async),
                ("input-context", CreateContextAsync),
                ("focus", FocusAsync),
                ("input-method", SwitchInputMethodAsync),
                ("key", KeyAsync),
                (PreeditRung, _ => Task.FromResult(PreeditText())),
                ("client-side-ui", _ => Task.FromResult(ClientSideUi())),
            ];
        }

        public async Task ClimbAsync()
        {
            try
            {
                foreach (var (name, climb) in rungs)
                {
                    if (Steps.Any(s => s.Outcome == ProbeOutcome.Failed))
                    {
                        Record(name, ProbeOutcome.Skipped, "not attempted");
                        continue;
                    }
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(settings.StepTimeout);
                        Record(name, ProbeOutcome.Passed, await climb(timeout.Token).ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        Record(name, ProbeOutcome.Failed, Describe(ex));
                    }
                }
            }
            finally
            {
                await CleanUpAsync().ConfigureAwait(false);
            }
        }

        private static string Platform()
        {
            return $"{RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; {WineDescription()}";
        }

        /// <summary>
        /// Wine puts its loader variables in the process environment; the classic
        /// <c>ntdll!wine_get_version</c> check is unreliable because wine-staging
        /// can hide those exports (wine-xiv does).
        /// </summary>
        private static string WineDescription()
        {
            if (!OperatingSystem.IsWindows()) return "not Windows";
            var prefix = Environment.GetEnvironmentVariable("WINEPREFIX");
            var underWine = prefix is not null || Environment.GetEnvironmentVariable("WINELOADERNOEXEC") is not null;
            if (!underWine) return "not Wine";
            try
            {
                return $"Wine {Marshal.PtrToStringAnsi(wine_get_version())} (WINEPREFIX={prefix})";
            }
            catch (EntryPointNotFoundException)
            {
                return $"Wine, version export hidden (WINEPREFIX={prefix})";
            }
        }

        private string ReadEnvironment()
        {
            var raw = settings.Address ?? Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "unset";
            if (string.IsNullOrEmpty(raw))
                throw new InvalidOperationException($"DBUS_SESSION_BUS_ADDRESS is not visible to this process (XDG_RUNTIME_DIR={runtimeDir}).");
            address = new SessionBusAddress(raw, null, null);
            return $"DBUS_SESSION_BUS_ADDRESS={raw}; XDG_RUNTIME_DIR={runtimeDir}";
        }

        private string ParseAddress()
        {
            address = SessionBusAddress.Parse(address!.Raw);
            if (address.UnixPath is null)
                throw new NotSupportedException($"No unix:path= entry in '{address.Raw}'; only AF_UNIX transports are on the ladder.");
            unixUserId = settings.UnixUserId ?? address.UnixUserId;
            // On Linux Tmds falls back to geteuid(); on Windows its fallback is the SID, which the bus will reject.
            if (unixUserId is null && OperatingSystem.IsWindows())
                throw new InvalidOperationException("Cannot tell the Linux uid from the socket path; set ProbeSettings.UnixUserId.");
            return $"path={address.UnixPath} wine-path={address.WinePath} uid={unixUserId?.ToString() ?? "(process default)"}";
        }

        private static string CreateSocket()
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            return $"AF_UNIX stream socket created (handle {socket.Handle})";
        }

        /// <summary>Connect without any D-Bus on top, trying the Linux spelling of the path and then Wine's.</summary>
        private async Task<string> ConnectRawAsync(CancellationToken ct)
        {
            var failures = new List<string>();
            foreach (var path in new[] { address!.UnixPath!, address.WinePath! }.Distinct())
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct).ConfigureAwait(false);
                    return $"connected to {path}";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{path}: {Describe(ex)}");
                }
            }
            throw new SocketException((int)SocketError.SocketError, string.Join(" | ", failures));
        }

        private async Task<string> AuthenticateAsync(CancellationToken ct)
        {
            var options = new FcitxConnectionOptions
            {
                Address = address!.Raw,
                ExternalUserId = unixUserId,
                SupportsFdPassing = false,
            };
            connection = await FcitxConnection.ConnectAsync(options, ct).ConfigureAwait(false);
            AuthenticatedWith = options;
            return $"AUTH EXTERNAL as uid {unixUserId?.ToString() ?? "(process default)"} accepted";
        }

        private async Task<string> Fcitx5Async(CancellationToken ct)
        {
            if (!await connection!.IsFcitxAvailableAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("org.fcitx.Fcitx5 is not on the bus.");
            return $"InputMethod1.Version = {await connection.GetVersionAsync(ct).ConfigureAwait(false)}";
        }

        private async Task<string> CreateContextAsync(CancellationToken ct)
        {
            context = await connection!.CreateInputContextAsync(settings.Program, cancellationToken: ct).ConfigureAwait(false);
            context.PanelUpdated += _ => Interlocked.Increment(ref panelUpdates);
            return context.Path;
        }

        private async Task<string> FocusAsync(CancellationToken ct)
        {
            await context!.FocusInAsync(ct).ConfigureAwait(false);
            var current = await WaitFor(() => context.CurrentInputMethod, ct).ConfigureAwait(false);
            return $"focused; input method {current.UniqueName}";
        }

        /// <summary>Transport is proven by now; this rung fails only when the wanted input method is missing or not in the group.</summary>
        private async Task<string> SwitchInputMethodAsync(CancellationToken ct)
        {
            var current = context!.CurrentInputMethod!.UniqueName;
            if (current == settings.InputMethod) return $"{current} already active";

            previousInputMethod = current;
            await connection!.SetCurrentInputMethodAsync(settings.InputMethod, ct).ConfigureAwait(false);
            try
            {
                await WaitFor(() => context.CurrentInputMethod?.UniqueName == settings.InputMethod ? context.CurrentInputMethod : null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"fcitx5 did not switch to '{settings.InputMethod}' (installed and in the current group?). The transport rungs above passed.");
            }
            return $"{current} → {settings.InputMethod}";
        }

        private async Task<string> KeyAsync(CancellationToken ct)
        {
            var press = KeyEvent.Char('k');
            var handled = await context!.ProcessKeyEventAsync(press, ct).ConfigureAwait(false);
            await context.ProcessKeyEventAsync(press.AsRelease(), ct).ConfigureAwait(false);
            return $"ProcessKeyEvent(k) handled={handled}";
        }

        private string PreeditText()
        {
            var preedit = context!.State.Preedit.Text;
            if (preedit.Length == 0) throw new InvalidOperationException("No preedit after 'k'; is the input method really composing?");
            return preedit;
        }

        private string ClientSideUi()
        {
            var count = Volatile.Read(ref panelUpdates);
            if (count == 0) throw new InvalidOperationException("No UpdateClientSideUI signal arrived; is ClientSideInputPanel in the capabilities?");
            return $"UpdateClientSideUI received {count}×";
        }

        private async Task CleanUpAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(settings.StepTimeout);
                if (context is not null)
                {
                    await context.ResetAsync(timeout.Token).ConfigureAwait(false);
                    if (previousInputMethod is not null) await connection!.SetCurrentInputMethodAsync(previousInputMethod, timeout.Token).ConfigureAwait(false);
                    await context.FocusOutAsync(timeout.Token).ConfigureAwait(false);
                    await context.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Best effort: the report is about the ladder, and the bus reaps contexts of dead connections anyway.
            }
            finally
            {
                connection?.Dispose();
            }
        }

        private void Record(string name, ProbeOutcome outcome, string detail)
        {
            var step = new ProbeStep(name, outcome, detail);
            Steps.Add(step);
            onStep?.Invoke(step);
        }

        private static async Task<T> WaitFor<T>(Func<T?> probe, CancellationToken ct) where T : class
        {
            while (true)
            {
                if (probe() is { } value) return value;
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        private static string Describe(Exception ex) => ex switch
        {
            SocketException s => $"SocketException {s.SocketErrorCode} ({s.NativeErrorCode}): {s.Message}",
            OperationCanceledException => "timed out",
            _ => $"{ex.GetType().Name}: {ex.Message}",
        };

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern IntPtr wine_get_version();
    }
}
