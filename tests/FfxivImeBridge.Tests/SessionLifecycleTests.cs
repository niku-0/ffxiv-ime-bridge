using System.Diagnostics;
using FfxivImeBridge.Session;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// Reconnect (ticket 13): the way out of Inert and of a dead connection, by
/// hand or automatically on a Chat Box focus gain at most once per 10 s, over
/// a scripted Transport Ladder. Nothing here is Dalamud.
/// </summary>
public sealed class SessionLifecycleTests
{
    private readonly FakeClock clock = new();
    private readonly List<string> chat = new();
    private readonly List<string> log = new();
    private readonly Configuration config = new() { Forwarding = true };
    private readonly FakeSessionSource source;
    private readonly SessionLifecycle lifecycle;

    public SessionLifecycleTests()
    {
        source = new FakeSessionSource(chat);
        lifecycle = new SessionLifecycle(source, config, chat.Add, log.Add, clock);
    }

    private async Task Settle()
    {
        if (lifecycle.Pending is { } pending) await pending;
    }

    private async Task LoadInert()
    {
        source.Reachable = false;
        lifecycle.Start();
        await Settle();
        Assert.True(lifecycle.Inert);
    }

    [Fact]
    public async Task A_failed_load_leaves_the_plugin_inert_and_says_so()
    {
        await LoadInert();

        Assert.Null(lifecycle.Session);
        Assert.Equal([(Silent: false, Forwarding: true)], source.Attempts);
        Assert.Equal(["IME Bridge: fcitx5 not reachable, disabled"], chat);
    }

    [Fact]
    public async Task A_focus_gain_while_inert_reconnects_silently_at_most_once_per_ten_seconds_and_success_restores_forwarding_from_config()
    {
        await LoadInert();
        chat.Clear();

        clock.Advance(TimeSpan.FromSeconds(3));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        await Settle();
        Assert.Single(source.Attempts); // the load was 3 s ago

        lifecycle.ObserveFocus(chatBoxFocused: false);
        clock.Advance(TimeSpan.FromSeconds(8));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        await Settle();
        Assert.Equal(2, source.Attempts.Count);
        Assert.True(source.Attempts[1].Silent);
        Assert.Empty(chat); // a failed automatic attempt says nothing

        lifecycle.ObserveFocus(chatBoxFocused: false);
        clock.Advance(TimeSpan.FromSeconds(5));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        await Settle();
        Assert.Equal(2, source.Attempts.Count); // inside 10 s of the last one

        source.Reachable = true;
        lifecycle.ObserveFocus(chatBoxFocused: false);
        clock.Advance(TimeSpan.FromSeconds(6));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        await Settle();
        Assert.Equal(3, source.Attempts.Count);
        Assert.NotNull(lifecycle.Session);
        Assert.False(lifecycle.Inert);
        Assert.True(lifecycle.Session!.Forwarding);
        Assert.Equal(["IME Bridge: fcitx5 reachable, forwarding on"], chat);
    }

    [Fact]
    public async Task A_focus_gain_with_a_live_session_reconnects_nothing()
    {
        source.Reachable = true;
        lifecycle.Start();
        await Settle();

        clock.Advance(TimeSpan.FromMinutes(1));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        await Settle();
        Assert.Single(source.Attempts);
    }

    [Fact]
    public async Task The_connection_dying_makes_the_next_focus_gain_reconnect_with_the_old_session_and_transport_gone()
    {
        source.Reachable = true;
        lifecycle.Start();
        await Settle();
        var first = lifecycle.Session!;
        var firstFcitx = source.LastFcitx!;
        var firstTransport = source.LastTransport!;
        chat.Clear();

        firstFcitx.Die("socket closed");
        first.Tick();
        Assert.True(first.ConnectionLost);
        Assert.Equal(["IME Bridge: lost the connection to fcitx5, forwarding degraded"], chat);

        clock.Advance(TimeSpan.FromSeconds(11));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        await Settle();
        Assert.Equal(2, source.Attempts.Count);
        Assert.True(source.Attempts[1].Silent);
        Assert.NotSame(first, lifecycle.Session);
        Assert.True(firstTransport.Disposed);
        Assert.False(firstFcitx.Created[0].Disposed); // nothing to destroy on a dead connection
        Assert.True(lifecycle.Session!.Forwarding);
        Assert.Equal(["IME Bridge: lost the connection to fcitx5, forwarding degraded", "IME Bridge: fcitx5 reachable, forwarding on"], chat);
    }

    [Fact]
    public async Task A_manual_reconnect_runs_inside_ten_seconds_tears_the_live_session_down_and_reports_both_ways()
    {
        source.Reachable = true;
        lifecycle.Start();
        await Settle();
        var first = lifecycle.Session!;
        var firstContext = source.LastFcitx!.Created[0];
        var firstTransport = source.LastTransport!;
        chat.Clear();

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(lifecycle.Reconnect());
        await Settle();
        Assert.Equal(2, source.Attempts.Count);
        Assert.False(source.Attempts[1].Silent);
        Assert.NotSame(first, lifecycle.Session);
        Assert.True(firstContext.Disposed);
        Assert.True(firstTransport.Disposed);
        Assert.Equal(["IME Bridge: fcitx5 reachable, forwarding on"], chat);

        source.Reachable = false;
        chat.Clear();
        Assert.True(lifecycle.Reconnect());
        await Settle();
        Assert.True(lifecycle.Inert);
        Assert.Equal(["IME Bridge: fcitx5 not reachable, disabled"], chat);
    }

    [Fact]
    public async Task A_reconnect_while_one_is_running_is_ignored_and_the_session_is_gone_meanwhile()
    {
        source.Reachable = true;
        lifecycle.Start();
        await Settle();
        var pending = new TaskCompletionSource<OpenedSession?>();
        source.Pending = pending;

        Assert.True(lifecycle.Reconnect());
        Assert.Null(lifecycle.Session);
        Assert.True(lifecycle.Opening);
        Assert.False(lifecycle.Inert);
        Assert.False(lifecycle.Reconnect());
        clock.Advance(TimeSpan.FromSeconds(30));
        lifecycle.ObserveFocus(chatBoxFocused: true);
        Assert.Equal(2, source.Attempts.Count);

        pending.SetResult(await source.Open(forwarding: false));
        await Settle();
        Assert.NotNull(lifecycle.Session);
        Assert.False(lifecycle.Opening);
    }

    [Fact]
    public async Task Startup_behaviour_is_reapplied_on_every_open()
    {
        config.Startup = StartupBehaviour.AlwaysOff;
        source.Reachable = true;
        lifecycle.Start();
        await Settle();
        Assert.False(lifecycle.Session!.Forwarding);
        Assert.Equal(["IME Bridge: fcitx5 reachable, forwarding off"], chat);

        config.Startup = StartupBehaviour.AlwaysOn;
        lifecycle.Reconnect();
        await Settle();
        Assert.True(lifecycle.Session!.Forwarding);
    }

    [Fact]
    public async Task Disposing_tears_the_session_and_transport_down_and_nothing_reconnects_after()
    {
        source.Reachable = true;
        lifecycle.Start();
        await Settle();
        var context = source.LastFcitx!.Created[0];
        var transport = source.LastTransport!;

        lifecycle.Dispose();
        Assert.True(context.Disposed);
        Assert.True(transport.Disposed);
        Assert.Null(lifecycle.Session);
        Assert.False(lifecycle.Reconnect());
    }

    /// <summary>
    /// The same unload with fcitx5 hung: it gives up at the budget it was
    /// handed, so the ladder gets the rest of the plugin's five seconds
    /// instead of waiting five more of its own (ticket 19).
    /// </summary>
    [Fact]
    public async Task An_unload_whose_teardown_hangs_gives_up_at_the_budget_it_was_given()
    {
        source.Reachable = true;
        lifecycle.Start();
        await Settle();
        var context = source.LastFcitx!.Created[0];
        var hung = new TaskCompletionSource();
        context.BlockDisposal = hung;

        var budget = TimeSpan.FromMilliseconds(200);
        var spent = Stopwatch.StartNew();
        lifecycle.Dispose(budget);
        spent.Stop();

        // The lower bound allows for the timer's granularity; the upper is the point: not the whole five seconds.
        Assert.InRange(spent.Elapsed, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(2));
        Assert.False(context.Disposed); // still waiting on fcitx5
        Assert.Contains("unload budget spent; fcitx5 reaps the context when the connection drops", log);
        Assert.Null(lifecycle.Session);
        Assert.False(lifecycle.Reconnect());

        hung.SetResult(); // let the cut-short teardown finish so nothing outlives the test
    }
}
