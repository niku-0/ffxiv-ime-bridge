using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.Session;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// The Forwarding state machine against a fake fcitx5: which calls the Input
/// Context gets, in what order, as focus, the user's toggle and fcitx5's
/// presence change. Everything here is the main thread; nothing is Dalamud.
/// </summary>
public sealed class ForwardingSessionTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(20);
    private static readonly KeyEvent K = KeyEvent.Char('k');

    private readonly FakeFcitx fcitx = new();
    private readonly List<string> chat = new();
    private readonly ForwardingSession session;
    private FakeInputContext Context => fcitx.Created[^1];

    public ForwardingSessionTests()
    {
        session = Open();
    }

    private ForwardingSession Open() => ForwardingSession.OpenAsync(fcitx, chat.Add, _ => { }, askTimeout: ShortTimeout).GetAwaiter().GetResult();

    private static Task<bool> Silence() => new TaskCompletionSource<bool>().Task;

    /// <summary>Asks <paramref name="count"/> keys that fcitx5 never answers.</summary>
    private void AskUnanswered(int count, ForwardingSession? on = null)
    {
        for (var i = 0; i < count; i++)
        {
            Context.Replies.Enqueue(Silence());
            Assert.Equal(KeyOutcome.TimedOut, (on ?? session).Ask(K).Outcome);
        }
    }

    // --- The waited call (ADR-0002) ---

    [Fact]
    public void Three_consecutive_unanswered_asks_degrade_forwarding()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);

        AskUnanswered(2);
        Assert.False(session.Degraded);
        AskUnanswered(1);
        Assert.True(session.Degraded);
        Assert.False(session.GateActive);
        Assert.Equal(["IME Bridge: forwarding on", "IME Bridge: fcitx5 is not answering, forwarding degraded"], chat);
    }

    [Fact]
    public void An_answer_resets_the_timeout_count()
    {
        AskUnanswered(2);
        Context.Replies.Enqueue(Task.FromResult(true)); // not a timer: a slow CI runner once let a 1 ms delay outlast the 20 ms timeout
        Assert.Equal(KeyOutcome.Consumed, session.Ask(K).Outcome);
        AskUnanswered(2);
        Assert.False(session.Degraded);
    }

    [Fact]
    public void A_faulted_call_counts_as_no_answer()
    {
        for (var i = 0; i < 3; i++)
        {
            Context.Replies.Enqueue(Task.FromException<bool>(new IOException("connection closed")));
            Assert.Equal(KeyOutcome.TimedOut, session.Ask(K).Outcome);
        }
        Assert.True(session.Degraded);
    }

    [Fact]
    public void An_answer_says_whether_fcitx5_took_the_key_and_how_long_it_took()
    {
        Context.Replies.Enqueue(Task.FromResult(true));
        Context.Replies.Enqueue(Task.FromResult(false));

        var consumed = session.Ask(K);
        Assert.Equal(KeyOutcome.Consumed, consumed.Outcome);
        Assert.NotNull(consumed.WaitMs);
        Assert.Equal(KeyOutcome.Declined, session.Ask(K).Outcome);
        Assert.Equal(2, Context.Asked.Count);
    }

    /// <summary>fcitx5 left the bus, or the connection died: nothing on the other end to talk to (ticket 18).</summary>
    public static TheoryData<bool> FcitxGone => new() { false, true };

    private void LoseFcitx(bool connectionDies)
    {
        if (connectionDies) fcitx.Die("socket closed");
        else fcitx.Leave();
        session.Tick();
        Assert.True(session.Degraded);
    }

    [Theory]
    [MemberData(nameof(FcitxGone))]
    public void With_nothing_to_talk_to_ask_makes_no_call_and_declines_without_a_wait(bool connectionDies)
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        LoseFcitx(connectionDies);

        for (var i = 0; i < 3; i++) Assert.Equal(new KeyAnswer(KeyOutcome.Declined, null), session.Ask(K));
        Assert.Empty(Context.Asked);
    }

    [Theory]
    [MemberData(nameof(FcitxGone))]
    public void With_nothing_to_talk_to_tell_select_and_paging_reach_no_context(bool connectionDies)
    {
        session.ObserveFocus(chatBoxFocused: true);
        LoseFcitx(connectionDies);
        Context.Calls.Clear();

        session.Tell(K);
        session.Tell(K.AsRelease());
        session.SelectCandidate(1);
        session.NextPage();
        session.PreviousPage();
        Assert.Empty(Context.Sent);
        Assert.Empty(Context.Calls);
    }

    [Fact]
    public void Tell_select_and_paging_reach_the_context_while_fcitx5_is_there()
    {
        session.Tell(K);
        session.SelectCandidate(1);
        session.NextPage();
        session.PreviousPage();
        Assert.Equal([K], Context.Sent);
        Assert.Equal(["SelectCandidate 1", "NextPage", "PrevPage"], Context.Calls);
    }

    [Fact]
    public void The_timeout_count_does_not_carry_from_one_session_to_the_next()
    {
        AskUnanswered(2);
        var next = Open();
        AskUnanswered(1, next);
        Assert.False(next.Degraded);
    }

    [Fact]
    public void The_timeout_count_restarts_after_degraded_by_timeouts_lifts()
    {
        session.ObserveFocus(chatBoxFocused: true);
        AskUnanswered(3);
        Assert.True(session.Degraded);
        session.ObserveFocus(chatBoxFocused: false);
        session.ObserveFocus(chatBoxFocused: true);
        Assert.False(session.Degraded);

        AskUnanswered(1);
        Assert.False(session.Degraded);
    }

    [Fact]
    public void Chat_box_focus_gained_focuses_the_context_and_focus_lost_resets_then_unfocuses()
    {
        session.ObserveFocus(chatBoxFocused: true);
        Assert.Equal(["FocusIn"], Context.Calls);

        session.ObserveFocus(chatBoxFocused: false);
        Assert.Equal(["FocusIn", "Reset", "FocusOut"], Context.Calls);
    }

    [Fact]
    public void The_game_window_going_inactive_with_the_box_focused_is_a_focus_loss_and_coming_back_a_gain()
    {
        // Alt-tab: the box keeps its native focus; the session must still Reset (else Mozc commits on fcitx5's own focus-out) and refocus on return (ticket 11).
        static FocusSnapshot Snapshot(bool boxFocused, bool windowActive) => new(TextInputActive: boxFocused, OwnerAddon: "ChatLog", UiHidden: false, WindowActive: windowActive);

        session.ObserveFocus(Snapshot(boxFocused: true, windowActive: true).ChatBoxFocused);
        session.ObserveFocus(Snapshot(boxFocused: true, windowActive: false).ChatBoxFocused);
        Assert.Equal(["FocusIn", "Reset", "FocusOut"], Context.Calls);

        session.ObserveFocus(Snapshot(boxFocused: true, windowActive: true).ChatBoxFocused);
        Assert.Equal(["FocusIn", "Reset", "FocusOut", "FocusIn"], Context.Calls);

        // Unfocused box, window away and back: nothing to tell fcitx5.
        session.ObserveFocus(Snapshot(boxFocused: false, windowActive: true).ChatBoxFocused);
        Assert.Equal(["FocusIn", "Reset", "FocusOut", "FocusIn", "Reset", "FocusOut"], Context.Calls);
        Context.Calls.Clear();
        session.ObserveFocus(Snapshot(boxFocused: false, windowActive: false).ChatBoxFocused);
        session.ObserveFocus(Snapshot(boxFocused: false, windowActive: true).ChatBoxFocused);
        Assert.Empty(Context.Calls);
    }

    [Fact]
    public void Only_the_first_reader_to_see_an_edge_issues_the_call()
    {
        // The hook and the framework tick both read focus; a key can reach the hook before the tick has seen the gain.
        session.ObserveFocus(chatBoxFocused: true); // hook
        session.ObserveFocus(chatBoxFocused: true); // tick, same frame
        session.ObserveFocus(chatBoxFocused: true); // next tick
        Assert.Equal(["FocusIn"], Context.Calls);
    }

    [Fact]
    public void Focus_is_tracked_whether_or_not_forwarding_is_on()
    {
        Assert.False(session.Forwarding);
        session.ObserveFocus(chatBoxFocused: true);
        Assert.Equal(["FocusIn"], Context.Calls);
    }

    [Fact]
    public void Flipping_forwarding_prints_one_chat_line_per_change()
    {
        session.Forwarding = true;
        session.Forwarding = true;
        session.Forwarding = false;
        Assert.Equal(["IME Bridge: forwarding on", "IME Bridge: forwarding off"], chat);
    }

    [Fact]
    public void The_input_method_reported_on_the_reader_thread_is_visible_after_the_next_tick()
    {
        session.ObserveFocus(chatBoxFocused: true);
        Context.RaiseInputMethod("keyboard-us-intl");
        Assert.Null(session.CurrentInputMethod);

        session.Tick();
        Assert.Equal("keyboard-us-intl", session.CurrentInputMethod?.UniqueName);
    }

    [Theory]
    [InlineData("mozc", "あ")]
    [InlineData("keyboard-us-intl", "A")]
    [InlineData("keyboard-us", "A")]
    [InlineData("anthy", "A")]
    [InlineData("pinyin", "P")]
    public void The_indicator_glyph_names_the_contexts_input_method(string uniqueName, string glyph)
    {
        session.Forwarding = true;
        Context.RaiseInputMethod(uniqueName);
        session.Tick();
        Assert.Equal(glyph, session.IndicatorGlyph?.Text);
    }

    [Theory]
    [InlineData("mozc", true)]
    [InlineData("keyboard-us", false)]
    [InlineData("pinyin", false)]
    public void Only_mozc_is_shown_as_hiragana(string uniqueName, bool hiragana)
    {
        session.Forwarding = true;
        Context.RaiseInputMethod(uniqueName);
        session.Tick();
        Assert.Equal(hiragana, session.IndicatorGlyph?.State == IndicatorState.Hiragana);
    }

    [Fact]
    public void The_indicator_has_no_glyph_before_fcitx5_has_named_an_input_method()
    {
        session.Forwarding = true;
        Assert.Null(session.IndicatorGlyph);
    }

    [Fact]
    public void The_composition_snapshot_moves_only_when_taken_on_the_game_thread()
    {
        Context.Compose("か");
        Assert.True(session.IsComposing); // the context's own state, always fresh
        Assert.False(session.Composition.IsComposing); // the snapshot, not yet taken

        session.TakeSnapshot(); // the hook, right after a waited reply
        Assert.Equal("か", session.Composition.Preedit.Text);

        Context.StopComposing();
        session.Tick(); // an out-of-band change reaches the tick
        Assert.False(session.Composition.IsComposing);
    }

    [Fact]
    public void Committed_text_waits_in_the_commit_queue_for_the_native_write()
    {
        Context.RaiseCommit("か");
        Context.RaiseCommit("き");
        Assert.True(session.TryTakeCommit(out var first));
        Assert.Equal("か", first);
        Assert.True(session.TryTakeCommit(out var second));
        Assert.Equal("き", second);
        Assert.False(session.TryTakeCommit(out _));
    }

    [Fact]
    public void Fcitx5_leaving_the_bus_degrades_forwarding_with_one_chat_line_and_the_gate_stops_acting()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        Context.RaiseInputMethod("mozc");
        session.Tick();
        Assert.True(session.GateActive);

        fcitx.Leave();
        Assert.False(session.Degraded); // not until the tick carries it over
        session.Tick();
        Assert.True(session.Degraded);
        Assert.True(session.Forwarding); // the user's choice is kept underneath
        Assert.False(session.GateActive);
        Assert.Equal(new IndicatorGlyph(IndicatorState.Degraded, "!"), session.IndicatorGlyph);
        Assert.Equal(["IME Bridge: forwarding on", "IME Bridge: fcitx5 left the bus, forwarding degraded"], chat);

        fcitx.Leave();
        session.Tick();
        Assert.Equal(2, chat.Count);
    }

    [Fact]
    public void After_bus_loss_the_next_focus_gain_with_fcitx5_back_recreates_the_context_and_lifts_degraded()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        var old = Context;
        fcitx.Leave();
        session.Tick();

        // Focus lost while fcitx5 is gone: nothing to tell a dead context.
        session.ObserveFocus(chatBoxFocused: false);
        Assert.Equal(["FocusIn"], old.Calls);

        // Back on the bus, but Degraded only lifts on the next Chat Box focus gain.
        fcitx.Return();
        session.Tick();
        Assert.True(session.Degraded);
        Assert.Single(fcitx.Created);

        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.False(session.Degraded);
        Assert.Equal(2, fcitx.Created.Count);
        Assert.True(old.Disposed);
        Assert.Equal(["FocusIn"], Context.Calls);
        Assert.Null(session.CurrentInputMethod); // the new context starts on the keyboard layout; fcitx5 will say
        Assert.True(session.GateActive);
    }

    [Fact]
    public void A_focus_gain_while_fcitx5_is_still_gone_keeps_degraded_and_creates_nothing()
    {
        session.ObserveFocus(chatBoxFocused: true);
        fcitx.Leave();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: false);

        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.True(session.Degraded);
        Assert.Single(fcitx.Created);
    }

    [Fact]
    public void Recreation_finishes_on_a_later_tick_and_focuses_only_if_the_chat_box_is_still_focused()
    {
        session.ObserveFocus(chatBoxFocused: true);
        fcitx.Leave();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: false);
        fcitx.Return();
        session.Tick();

        var pending = new TaskCompletionSource<IInputContextClient>();
        fcitx.PendingCreation = pending;
        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.True(session.Degraded);

        session.ObserveFocus(chatBoxFocused: false); // the user clicked away before fcitx5 answered
        var replacement = new FakeInputContext();
        pending.SetResult(replacement);
        session.Tick();
        Assert.False(session.Degraded);
        Assert.Empty(replacement.Calls);

        session.ObserveFocus(chatBoxFocused: true);
        Assert.Equal(["FocusIn"], replacement.Calls);
    }

    [Fact]
    public void A_failed_recreation_stays_degraded_and_is_retried_on_the_next_focus_gain()
    {
        session.ObserveFocus(chatBoxFocused: true);
        fcitx.Leave();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: false);
        fcitx.Return();
        session.Tick();

        var pending = new TaskCompletionSource<IInputContextClient>();
        fcitx.PendingCreation = pending;
        session.ObserveFocus(chatBoxFocused: true);
        pending.SetException(new InvalidOperationException("no reply"));
        session.Tick();
        Assert.True(session.Degraded);

        session.ObserveFocus(chatBoxFocused: false);
        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.False(session.Degraded);
        Assert.Equal(["FocusIn"], Context.Calls);
    }

    [Fact]
    public void Degraded_by_timeouts_lifts_on_the_next_focus_gain_without_recreating_the_context()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        AskUnanswered(3);
        Assert.True(session.Degraded);
        Assert.False(session.GateActive);
        Assert.Equal(["IME Bridge: forwarding on", "IME Bridge: fcitx5 is not answering, forwarding degraded"], chat);

        session.ObserveFocus(chatBoxFocused: false);
        session.ObserveFocus(chatBoxFocused: true);
        Assert.False(session.Degraded);
        Assert.Single(fcitx.Created);
        Assert.Equal(["FocusIn", "Reset", "FocusOut", "FocusIn"], Context.Calls);
    }

    [Fact]
    public void Bus_loss_on_top_of_timeouts_still_needs_the_context_recreated()
    {
        session.ObserveFocus(chatBoxFocused: true);
        AskUnanswered(3);
        fcitx.Leave();
        session.Tick();
        Assert.Single(chat); // still one entry into Degraded
        fcitx.Return();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: false);
        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.Equal(2, fcitx.Created.Count);
    }

    [Fact]
    public async Task Disposing_cancels_unfocuses_and_destroys_the_context()
    {
        session.ObserveFocus(chatBoxFocused: true);
        await session.DisposeAsync();
        Assert.Equal(["FocusIn", "Reset", "FocusOut", "Dispose"], Context.Calls);
    }

    [Fact]
    public async Task Disposing_after_fcitx5_left_the_bus_lets_the_context_go_without_destroying_it()
    {
        // DestroyIC would be a method call to a name nobody owns, and the bus
        // would start fcitx5 again on the user's desktop (ticket 18).
        session.ObserveFocus(chatBoxFocused: true);
        var old = Context;
        fcitx.Leave();
        session.Tick();

        await session.DisposeAsync();

        Assert.Equal(["FocusIn", "Abandon"], old.Calls);
        Assert.False(old.Disposed);
        Assert.True(old.Abandoned);
    }

    [Fact]
    public async Task Disposing_after_the_connection_died_lets_the_context_go_without_destroying_it()
    {
        session.ObserveFocus(chatBoxFocused: true);
        var old = Context;
        fcitx.Die("socket closed");
        session.Tick();

        await session.DisposeAsync();

        Assert.Equal(["FocusIn", "Abandon"], old.Calls);
        Assert.False(old.Disposed);
        Assert.True(old.Abandoned);
    }

    [Fact]
    public void A_late_signal_from_the_replaced_context_is_ignored()
    {
        session.ObserveFocus(chatBoxFocused: true);
        var old = Context;
        fcitx.Leave();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: false);
        fcitx.Return();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.NotSame(old, Context);

        old.RaiseInputMethod("mozc");
        old.RaiseCommit("stale");
        session.Tick();
        Assert.Null(session.CurrentInputMethod);
        Assert.False(session.TryTakeCommit(out _));
    }

    // --- ConnectionLost (ticket 13): only a Reconnect lifts it ---

    [Fact]
    public void The_connection_dying_degrades_forwarding_with_one_chat_line_and_nothing_is_recreated()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Context.Calls.Clear();

        fcitx.Die("socket closed");
        session.Tick();
        Assert.True(session.Degraded);
        Assert.True(session.ConnectionLost);
        Assert.True(session.Forwarding);
        Assert.False(session.GateActive);
        Assert.Equal(new IndicatorGlyph(IndicatorState.Degraded, "!"), session.IndicatorGlyph);
        Assert.Equal(["IME Bridge: forwarding on", "IME Bridge: lost the connection to fcitx5, forwarding degraded"], chat);

        // Focus edges have nothing to tell a dead connection, and fcitx5 "returning" on it means nothing.
        session.ObserveFocus(chatBoxFocused: false);
        fcitx.Return();
        session.Tick();
        session.ObserveFocus(chatBoxFocused: true);
        session.Tick();
        Assert.Empty(Context.Calls);
        Assert.Single(fcitx.Created);
        Assert.True(session.ConnectionLost);
    }

    [Fact]
    public void The_connection_dying_on_top_of_bus_loss_or_timeouts_becomes_connection_lost_silently()
    {
        session.Forwarding = true;
        fcitx.Leave();
        session.Tick();
        Assert.Equal(2, chat.Count);

        fcitx.Die("socket closed");
        session.Tick();
        Assert.True(session.ConnectionLost);
        Assert.Equal(2, chat.Count);

        // And a bus loss reported after the connection is already gone changes nothing.
        fcitx.Leave();
        session.Tick();
        Assert.True(session.ConnectionLost);
        Assert.Equal(2, chat.Count);
    }

    [Fact]
    public async Task The_session_opens_with_the_forwarding_it_is_given_without_a_chat_line()
    {
        var on = await ForwardingSession.OpenAsync(fcitx, chat.Add, _ => { }, forwarding: true);
        Assert.True(on.Forwarding);
        Assert.Equal([], chat);
    }
}
