using System.Diagnostics;
using Tmds.DBus.Protocol;
using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>
/// Every method call the library writes carries <see cref="MethodCall.Flags"/>,
/// so that one made after fcitx5 has left the bus is refused instead of having
/// <c>dbus-daemon</c> exec <c>fcitx5</c> on the user's desktop (ticket 18).
/// fcitx5's own name is never used here — the developer's session bus is the
/// real one: the test starts a private bus with an activatable name of its
/// own, whose service file runs <c>touch</c>, and looks for the file.
/// </summary>
public sealed class NoAutoStartTests
{
    [PrivateBusFact]
    public async Task A_call_carrying_the_flag_is_refused_and_starts_nothing()
    {
        await using var bus = await PrivateBus.StartAsync();
        using var connection = await bus.ConnectAsync();

        var error = await Assert.ThrowsAsync<DBusErrorReplyException>(() => bus.CallAsync(connection, MethodCall.Flags));

        // dbus-daemon 1.16.2 answers a NoAutoStart call to an activatable name nobody owns with
        // NameHasNoOwner, not the ServiceUnknown it uses for a name it has never heard of.
        Assert.Equal("org.freedesktop.DBus.Error.NameHasNoOwner", error.ErrorName);
        Assert.False(await bus.WasStartedAsync());
    }

    [PrivateBusFact]
    public async Task Without_the_flag_the_same_call_starts_the_service()
    {
        // The control that makes the first test mean something: the service file really is activatable.
        await using var bus = await PrivateBus.StartAsync();
        using var connection = await bus.ConnectAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => bus.CallAsync(connection, MessageFlags.None)); // touch exits without claiming the name

        Assert.True(await bus.WasStartedAsync());
    }
}

/// <summary>A session bus of the test's own, with one activatable name on it.</summary>
internal sealed class PrivateBus : IAsyncDisposable
{
    private const string ActivatableName = "org.ffxivimebridge.Startable";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly string root;
    private readonly Process daemon = new();

    private PrivateBus(string root) => this.root = root;

    public static string? DaemonPath => Which("dbus-daemon");
    public static string? TouchPath => Which("touch");

    private string SocketPath => Path.Combine(root, "bus");
    private string MarkerPath => Path.Combine(root, "started");

    public static async Task<PrivateBus> StartAsync()
    {
        var bus = new PrivateBus(Path.Combine(Path.GetTempPath(), $"imebridge-nostart-{Environment.ProcessId}-{Guid.NewGuid():N}"));
        try
        {
            await bus.WriteFilesAndStartAsync();
            return bus;
        }
        catch
        {
            await bus.DisposeAsync();
            throw;
        }
    }

    public async Task<DBusConnection> ConnectAsync()
    {
        var connection = new DBusConnection($"unix:path={SocketPath}");
        await connection.ConnectAsync();
        return connection;
    }

    /// <summary>A method call to the activatable name, written the way the library writes its own but for the flags under test.</summary>
    public Task CallAsync(DBusConnection connection, MessageFlags flags)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(ActivatableName, Fcitx5Names.InputMethodPath, Fcitx5Names.InputMethodInterface, "Version", flags: flags);
        return connection.CallMethodAsync(writer.CreateMessage()).WaitAsync(Limit);
    }

    /// <summary>Whether the bus exec'd the service file. Waits for it, so that "no" means it never came rather than "not yet".</summary>
    public async Task<bool> WasStartedAsync()
    {
        try
        {
            await WaitForAsync(() => File.Exists(MarkerPath), "the service was never started");
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { daemon.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* never started, or already gone */ }
        await daemon.WaitForExitAsync().WaitAsync(Limit);
        daemon.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private async Task WriteFilesAndStartAsync()
    {
        var services = Directory.CreateDirectory(Path.Combine(root, "services")).FullName;
        await File.WriteAllTextAsync(Path.Combine(services, $"{ActivatableName}.service"),
            $"[D-BUS Service]\nName={ActivatableName}\nExec={TouchPath} {MarkerPath}\n");

        // The permissive default policy of a real session bus; a narrower one drops the replies too.
        var config = Path.Combine(root, "bus.conf");
        await File.WriteAllTextAsync(config, $"""
            <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-BUS Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
            <busconfig>
              <type>session</type>
              <listen>unix:path={SocketPath}</listen>
              <servicedir>{services}</servicedir>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true"/>
                <allow eavesdrop="true"/>
                <allow own="*"/>
              </policy>
            </busconfig>
            """);

        daemon.StartInfo = new ProcessStartInfo(DaemonPath!, ["--config-file=" + config, "--nofork"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        daemon.Start();
        // Drained, or the daemon blocks on a full pipe and waiting for its exit never returns.
        _ = daemon.StandardOutput.ReadToEndAsync();
        _ = daemon.StandardError.ReadToEndAsync();
        await WaitForAsync(() => File.Exists(SocketPath), $"{DaemonPath} did not listen on {SocketPath}");
    }

    private static async Task WaitForAsync(Func<bool> done, string message)
    {
        var deadline = DateTime.UtcNow + Limit;
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(message);
            await Task.Delay(20);
        }
    }

    private static string? Which(string name) => Environment.GetEnvironmentVariable("PATH")?.Split(':')
        .Select(dir => Path.Combine(dir, name))
        .FirstOrDefault(File.Exists);
}

/// <summary>A fact that starts a <c>dbus-daemon</c> of its own, with a <c>touch</c> for it to activate; skipped where there is neither.</summary>
public sealed class PrivateBusFactAttribute : FactAttribute
{
    public PrivateBusFactAttribute()
    {
        if (PrivateBus.DaemonPath is null) Skip = "dbus-daemon is not installed.";
        else if (PrivateBus.TouchPath is null) Skip = "touch is not installed.";
    }
}
