using FfxivImeBridge.Fcitx.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>
/// The ladder the dev plugin climbs inside Wine, run on the host where every
/// rung is expected to hold. This pins down the report shape the plugin's
/// debug window shows; the interesting run is the one inside the game.
/// </summary>
public sealed class TransportProbeTests(ITestOutputHelper output)
{
    [FcitxFact]
    public async Task Every_rung_passes_on_the_host_and_k_yields_a_preedit()
    {
        var steps = new List<ProbeStep>();
        var report = await TransportProbe.RunAsync(onStep: steps.Add, cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        output.WriteLine(report.ToString());

        Assert.True(report.Succeeded, report.ToString());
        Assert.Equal(steps, report.Steps);
        Assert.Equal(
            ["platform", "environment", "address", "socket", "connect", "authenticate", "fcitx5", "input-context", "focus", "input-method", "key", "preedit", "client-side-ui"],
            report.Steps.Select(s => s.Name));
        Assert.All(report.Steps, s => Assert.Equal(ProbeOutcome.Passed, s.Outcome));
        Assert.Equal("ｋ", report.PreeditText); // Mozc shows pending romaji full-width

        // The settings that authenticated are handed out so the plugin can open its live connection the same way.
        var options = Assert.IsType<FcitxConnectionOptions>(report.ConnectionOptions);
        Assert.Equal(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"), options.Address);
        Assert.Equal(SessionBusAddress.Parse(options.Address!).UnixUserId, options.ExternalUserId);
        Assert.False(options.SupportsFdPassing);
        using var live = await FcitxConnection.ConnectAsync(options);
        Assert.True(await live.IsFcitxAvailableAsync());
    }

    [FcitxFact]
    public async Task Stops_at_the_first_failing_rung_and_names_it()
    {
        // A path under the real /run/user/<uid>/ so the uid still parses and the failure is the connect itself.
        var uid = SessionBusAddress.Parse(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!).UnixUserId;
        var report = await TransportProbe.RunAsync(new ProbeSettings { Address = $"unix:path=/run/user/{uid}/nonexistent-bus" });
        output.WriteLine(report.ToString());

        Assert.False(report.Succeeded);
        var failed = Assert.Single(report.Steps, s => s.Outcome == ProbeOutcome.Failed);
        Assert.Equal("connect", failed.Name);
        Assert.Contains("SocketException", failed.Detail);
        Assert.DoesNotContain(report.Steps, s => s.Name == "authenticate" && s.Outcome != ProbeOutcome.Skipped);
        Assert.Null(report.ConnectionOptions);
    }

    [FcitxFact]
    public async Task Leaves_no_input_context_behind()
    {
        var report = await TransportProbe.RunAsync();
        var contextPath = report.Steps.Single(s => s.Name == "input-context").Detail;
        Assert.StartsWith("/org/freedesktop/portal/inputcontext/", contextPath);

        using var connection = await FcitxConnection.ConnectAsync();
        var error = await Assert.ThrowsAsync<Tmds.DBus.Protocol.DBusErrorReplyException>(() => connection.IntrospectAsync(contextPath));
        Assert.Contains("UnknownObject", error.Message);
    }
}
