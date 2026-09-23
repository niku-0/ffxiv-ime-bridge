using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.Session;
using Xunit;
using static FfxivImeBridge.Tests.Messages;

namespace FfxivImeBridge.Tests;

/// <summary>
/// The Gate over a scripted fcitx5 (ticket 05's seam): which messages are
/// asked, which are sent, and where each one goes. Everything is the game's
/// message pump; nothing is Dalamud.
/// </summary>
public sealed class KeyboardGateTests
{
    private const int VkK = 0x4B;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkV = 0x56;
    private const int VkOem5 = 0xDC; // what Windows calls § on a Nordic layout; the gate matches the scancode, not this
    private const int ScK = 0x25;
    private const int ScSection = 0x29; // key left of 1: § on a Nordic layout, ` on US

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(20);

    private readonly FakeFcitx fcitx = new();
    private readonly FakeKeyState keyState = new();
    private readonly List<string> chat = new();
    private readonly ForwardingSession session;
    private readonly KeyboardGate gate;
    private bool chatBoxEmpty = false; // set by the Slash Bypass tests

    public KeyboardGateTests()
    {
        session = ForwardingSession.OpenAsync(fcitx, chat.Add, _ => { }).GetAwaiter().GetResult();
        gate = new KeyboardGate(keyState, () => chatBoxEmpty) { Session = session, Timeout = ShortTimeout };
    }

    private FakeInputContext Fcitx => fcitx.Created[^1];

    /// <summary>Forwarding on, Chat Box focused, fcitx5 answering: the gate acts.</summary>
    private void Activate()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
    }

    private GateVerdict Verdict(KeyMessage message, bool chatBoxFocused = true) => gate.Decide(message, chatBoxFocused).Verdict;

    [Fact]
    public void A_printing_key_is_asked_at_its_char_and_swallowed_whole_when_fcitx5_takes_it()
    {
        Activate();
        Fcitx.Handled = true;

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkK, ScK)));
        Assert.Empty(Fcitx.Asked); // provisional: the keysym is only known at the char
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
        Assert.Equal([KeyEvent.Char('k') with { Time = Fcitx.Asked[0].Time }], Fcitx.Asked);
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK)));
        Assert.Equal([KeyEvent.Char('k').AsRelease() with { Time = Fcitx.Sent[0].Time }], Fcitx.Sent);
    }

    [Fact]
    public void A_printing_key_fcitx5_declines_passes_its_char_and_release_but_its_keydown_is_already_gone()
    {
        Activate();
        Fcitx.Handled = false;

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkK, ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK)));
        Assert.Single(Fcitx.Asked);
        Assert.Single(Fcitx.Sent); // the release is still told
        Assert.True(Fcitx.Sent[0].IsRelease);
    }

    [Fact]
    public void A_provisional_keydown_whose_char_never_comes_is_gone_with_its_release()
    {
        Activate();

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkK, ScK)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK)));
        Assert.Empty(Fcitx.Asked);
        Assert.Empty(Fcitx.Sent);
    }

    [Fact]
    public void Modifier_keys_are_sent_unasked_and_pass_while_the_printing_key_under_them_is_asked_with_their_state()
    {
        Activate();
        Fcitx.Handled = true;

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkShift, 0x2A)));
        keyState.Modifiers = new Modifiers { Shift = true };
        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkK, ScK)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('K', ScK)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK)));
        keyState.Modifiers = default;
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkShift, 0x2A)));

        Assert.Equal([KeySym.ShiftL], Fcitx.Sent.Where(k => !k.IsRelease).Select(k => k.KeySym));
        Assert.Equal([(uint)'K'], Fcitx.Asked.Select(k => k.KeySym));
        Assert.Equal(KeyState.Shift, Fcitx.Asked[0].State);
        Assert.Equal([(uint)'K', KeySym.ShiftL], Fcitx.Sent.Where(k => k.IsRelease).Select(k => k.KeySym));
    }

    [Fact]
    public void Enter_and_escape_pass_unasked_with_their_chars_while_no_composition_is_active()
    {
        Activate();
        Fcitx.Handled = true; // would swallow if asked

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkReturn, 0x1C)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('\r', 0x1C)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkReturn, 0x1C)));
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkEscape, 0x01)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char(0x1B, 0x01)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkEscape, 0x01)));
        Assert.Empty(Fcitx.Asked);
        Assert.Empty(Fcitx.Sent);
    }

    [Fact]
    public void Enter_inside_a_composition_is_asked_at_its_keydown_and_its_char_follows()
    {
        Activate();
        Fcitx.Handled = true;
        Fcitx.Compose("こんにちは");

        var decision = gate.Decide(Down(VkReturn, 0x1C), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Swallow, decision.Verdict);
        Assert.Equal(KeyClass.Fixed, decision.Class);
        Assert.True(decision.Asked);
        Assert.Equal([KeySym.Return], Fcitx.Asked.Select(k => k.KeySym));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('\r', 0x1C)));
        Assert.Single(Fcitx.Asked); // the char is never asked at: it follows the keydown
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkReturn, 0x1C)));
        Assert.Equal([KeySym.Return], Fcitx.Sent.Select(k => k.KeySym));
    }

    [Fact]
    public void Ctrl_space_is_asked_even_with_no_composition()
    {
        // fcitx5's trigger key must reach the context (ticket 05 could not switch input methods without it).
        Activate();
        Fcitx.Handled = true;
        Verdict(Down(VkControl, 0x1D));
        keyState.Modifiers = new Modifiers { Ctrl = true };

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(0x20, 0x39)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char(' ', 0x39)));
        Assert.Equal(new KeyEvent(KeySym.Space, KeyCode.FromAsciiChar(' '), KeyState.Ctrl) with { Time = Fcitx.Asked[0].Time }, Fcitx.Asked.Single());
    }

    private static Task<bool> Silence() => new TaskCompletionSource<bool>().Task;

    [Fact]
    public void A_key_fcitx5_does_not_answer_in_time_is_swallowed_and_its_release_follows()
    {
        Activate();
        Fcitx.Replies.Enqueue(Silence());

        Verdict(Down(VkK, ScK));
        var decision = gate.Decide(Char('k', ScK), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Swallow, decision.Verdict);
        Assert.Equal(GateRule.TimedOut, decision.Rule);
        Assert.True(decision.WaitMs >= ShortTimeout.TotalMilliseconds - 1);
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK)));
        Assert.False(session.Degraded);
    }

    [Fact]
    public void Three_consecutive_timeouts_degrade_forwarding_and_the_next_key_passes_untouched()
    {
        Activate();
        for (var i = 0; i < 3; i++) Fcitx.Replies.Enqueue(Silence());

        for (var i = 0; i < 3; i++)
        {
            Verdict(Down(VkK, ScK));
            Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
            Verdict(Up(VkK, ScK));
        }
        Assert.True(session.Degraded);
        Assert.Equal(["IME Bridge: forwarding on", "IME Bridge: fcitx5 is not answering, forwarding degraded"], chat);

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK)));
        Assert.Equal(3, Fcitx.Asked.Count);
    }

    [Fact]
    public void A_reply_resets_the_timeout_count()
    {
        Activate();
        Fcitx.Replies.Enqueue(Silence());
        Fcitx.Replies.Enqueue(Silence());
        Fcitx.Replies.Enqueue(Task.Delay(1).ContinueWith(_ => true)); // late but in time
        Fcitx.Replies.Enqueue(Silence());
        Fcitx.Replies.Enqueue(Silence());

        for (var i = 0; i < 5; i++)
        {
            Verdict(Down(VkK, ScK));
            Verdict(Char('k', ScK));
            Verdict(Up(VkK, ScK));
        }
        Assert.False(session.Degraded);
    }

    [Fact]
    public void A_faulted_call_counts_as_no_answer()
    {
        Activate();
        for (var i = 0; i < 3; i++) Fcitx.Replies.Enqueue(Task.FromException<bool>(new IOException("connection closed")));

        for (var i = 0; i < 3; i++)
        {
            Verdict(Down(VkK, ScK));
            Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
            Verdict(Up(VkK, ScK));
        }
        Assert.True(session.Degraded);
    }

    [Fact]
    public void Every_auto_repeat_is_a_fresh_press_decided_afresh()
    {
        Activate();
        Fcitx.Replies.Enqueue(Task.FromResult(true));
        Fcitx.Replies.Enqueue(Task.FromResult(false));

        Verdict(Down(VkK, ScK));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkK, ScK, repeat: true)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK)));
        Assert.Equal(2, Fcitx.Asked.Count);
    }

    [Fact]
    public void A_repeat_after_the_gate_stopped_acting_passes_though_the_first_press_was_swallowed()
    {
        Activate();
        Fcitx.Handled = true;

        Verdict(Down(VkK, ScK));
        Verdict(Char('k', ScK));
        session.Forwarding = false;
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK, repeat: true)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK)));
    }

    [Fact]
    public void Alt_plus_the_toggle_key_reports_toggle_and_is_itself_swallowed_whether_the_gate_acts_or_not()
    {
        // Forwarding is the session's to flip; the gate only names the chord and eats it.
        var gateAlone = new KeyboardGate(keyState, () => false);
        keyState.Modifiers = new Modifiers { Alt = true };

        Assert.Equal(GateVerdict.Toggle, gateAlone.Decide(SysDown(VkOem5, ScSection), chatBoxFocused: true).Verdict);
        Assert.Equal(GateVerdict.Swallow, gateAlone.Decide(SysChar('§', ScSection), chatBoxFocused: true).Verdict);
        Assert.Equal(GateVerdict.Swallow, gateAlone.Decide(SysUp(VkOem5, ScSection), chatBoxFocused: true).Verdict);

        Activate();
        Fcitx.Handled = false;
        Assert.Equal(GateVerdict.Toggle, Verdict(SysDown(VkOem5, ScSection)));
        Assert.Equal(GateVerdict.Swallow, Verdict(SysChar('§', ScSection)));
        // Alt may be released first, in which case the key up is a plain WM_KEYUP.
        keyState.Modifiers = default;
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkOem5, ScSection)));
        Assert.Empty(Fcitx.Asked);
    }

    [Fact]
    public void Holding_the_toggle_key_does_not_report_toggle_again()
    {
        keyState.Modifiers = new Modifiers { Alt = true };

        Assert.Equal(GateVerdict.Toggle, Verdict(SysDown(VkOem5, ScSection)));
        Assert.Equal(GateVerdict.Swallow, Verdict(SysDown(VkOem5, ScSection, repeat: true)));
    }

    [Fact]
    public void The_toggle_key_is_matched_by_scancode_so_the_layouts_vk_does_not_matter()
    {
        keyState.Modifiers = new Modifiers { Alt = true };

        Assert.Equal(GateVerdict.Toggle, Verdict(SysDown(0xC0 /* VK_OEM_3, US ` */, ScSection)));
    }

    [Fact]
    public void The_toggle_key_only_acts_while_the_chat_box_is_focused()
    {
        keyState.Modifiers = new Modifiers { Alt = true };

        Assert.Equal(GateVerdict.Pass, Verdict(SysDown(VkOem5, ScSection), chatBoxFocused: false));
    }

    // --- The Toggle Key from config (ticket 12) ---

    private const int VkJ = 0x4A;
    private const int ScJ = 0x24;

    [Fact]
    public void The_chord_is_whatever_the_config_says_and_the_default_no_longer_flips()
    {
        gate.ToggleKey = new ToggleKey(ChordModifiers.Ctrl, ScJ);

        keyState.Modifiers = new Modifiers { Ctrl = true };
        Assert.Equal(GateVerdict.Toggle, Verdict(Down(VkJ, ScJ)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char(0x0A /* ^J */, ScJ)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkJ, ScJ)));

        keyState.Modifiers = new Modifiers { Alt = true };
        Assert.Equal(GateVerdict.Pass, Verdict(SysDown(VkOem5, ScSection)));
    }

    [Fact]
    public void Each_modifier_set_matches_only_when_exactly_those_modifiers_are_held()
    {
        static Modifiers Held(ChordModifiers m) => new() { Ctrl = m.HasFlag(ChordModifiers.Ctrl), Alt = m.HasFlag(ChordModifiers.Alt), Shift = m.HasFlag(ChordModifiers.Shift) };
        var sets = new[]
        {
            ChordModifiers.Ctrl, ChordModifiers.Alt, ChordModifiers.Shift,
            ChordModifiers.Ctrl | ChordModifiers.Alt, ChordModifiers.Ctrl | ChordModifiers.Shift, ChordModifiers.Alt | ChordModifiers.Shift,
            ChordModifiers.Ctrl | ChordModifiers.Alt | ChordModifiers.Shift,
        };

        foreach (var bound in sets)
        {
            var key = new ToggleKey(bound, ScJ);
            foreach (var held in sets)
            {
                Assert.Equal(held == bound, key.Matches(Held(held), ScJ));
            }
            Assert.False(key.Matches(Held(bound), ScK));
        }
    }

    [Fact]
    public void The_chord_with_an_extra_modifier_held_is_not_the_toggle()
    {
        // Alt+§ bound; Ctrl+Alt+§ pressed: an ordinary chord for the gate (asked while it acts).
        Activate();
        Fcitx.Handled = false;
        keyState.Modifiers = new Modifiers { Ctrl = true, Alt = true };

        var decision = gate.Decide(SysDown(VkOem5, ScSection), chatBoxFocused: true);
        Assert.NotEqual(GateVerdict.Toggle, decision.Verdict);
        Assert.NotEqual(GateRule.Toggle, decision.Rule);
    }

    [Fact]
    public void An_unbound_chord_never_toggles()
    {
        gate.ToggleKey = new ToggleKey(ChordModifiers.None, ScSection);
        Assert.False(gate.ToggleKey.IsBound);

        keyState.Modifiers = new Modifiers { Alt = true };
        Assert.Equal(GateVerdict.Pass, Verdict(SysDown(VkOem5, ScSection)));
        keyState.Modifiers = default;
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkOem5, ScSection)));
    }

    private const int VkHome = 0x24;
    private const int VkNumpad7 = 0x67;
    private const int ScNumpad7 = 0x47; // Home is the same scancode with the extended bit: E0 47 in set 1

    [Fact]
    public void An_extended_key_is_a_different_chord_from_its_numpad_twin_and_is_captured_as_such()
    {
        ToggleKey? captured = null;
        gate.BeginCapture(key => captured = key);
        keyState.Modifiers = new Modifiers { Alt = true };
        Verdict(new KeyMessage(WindowMessage.SysKeyDown, VkHome, LParam(ScNumpad7, extended: true, alt: true)));
        Assert.Equal(new ToggleKey(ChordModifiers.Alt, 0xE047), captured);
        Verdict(new KeyMessage(WindowMessage.SysKeyUp, VkHome, LParam(ScNumpad7, extended: true, alt: true, wasDown: true)));

        gate.ToggleKey = captured!.Value;
        Assert.Equal(GateVerdict.Pass, Verdict(SysDown(VkNumpad7, ScNumpad7)));
        Verdict(SysUp(VkNumpad7, ScNumpad7));
        Assert.Equal(GateVerdict.Toggle, Verdict(new KeyMessage(WindowMessage.SysKeyDown, VkHome, LParam(ScNumpad7, extended: true, alt: true))));
        Assert.Equal("Alt+sc 0xE047", captured.Value.Describe(_ => null));
    }

    [Fact]
    public void The_chord_describes_itself_as_modifiers_plus_the_layouts_character_or_the_scancode()
    {
        Assert.Equal("Alt+§", ToggleKey.Default.Describe(sc => sc == ScSection ? "§" : null));
        Assert.Equal("Alt+sc 0x29", ToggleKey.Default.Describe(_ => null));
        Assert.Equal("Alt+sc 0x29", ToggleKey.Default.Describe(_ => ""));
        Assert.Equal("Ctrl+Alt+Shift+J", new ToggleKey(ChordModifiers.Ctrl | ChordModifiers.Alt | ChordModifiers.Shift, ScJ).Describe(_ => "J"));
        Assert.Equal("unbound", new ToggleKey(ChordModifiers.None, ScJ).Describe(_ => "J"));
    }

    // --- Capturing a new Toggle Key ("Press a key") ---

    [Fact]
    public void Capture_returns_the_next_non_modifier_keydown_with_the_modifiers_held_and_swallows_that_key_whole()
    {
        Activate();
        Fcitx.Handled = false;
        ToggleKey? captured = null;
        var calls = 0;
        gate.BeginCapture(key => { captured = key; calls++; });
        Assert.True(gate.Capturing);

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkControl, 0x1D))); // a modifier is not the key
        Assert.Null(captured);
        Assert.True(gate.Capturing);
        keyState.Modifiers = new Modifiers { Ctrl = true };
        var decision = gate.Decide(Down(VkJ, ScJ), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Swallow, decision.Verdict);
        Assert.Equal(GateRule.Captured, decision.Rule);
        Assert.Equal(new ToggleKey(ChordModifiers.Ctrl, ScJ), captured);
        Assert.False(gate.Capturing);
        Assert.Equal(GateVerdict.Swallow, Verdict(Char(0x0A, ScJ)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkJ, ScJ)));
        Assert.Empty(Fcitx.Asked); // the captured key never reaches fcitx5

        keyState.Modifiers = default;
        Verdict(Up(VkControl, 0x1D));
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK), chatBoxFocused: false)); // capture is over: an ordinary key again
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Capture_works_with_the_chat_box_unfocused_and_reports_no_modifiers_when_none_are_held()
    {
        ToggleKey? captured = null;
        gate.BeginCapture(key => captured = key);

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkJ, ScJ), chatBoxFocused: false));
        Assert.Equal(new ToggleKey(ChordModifiers.None, ScJ), captured);
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('j', ScJ), chatBoxFocused: false));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkJ, ScJ), chatBoxFocused: false));
    }

    [Fact]
    public void Escape_cancels_capture_and_is_swallowed_with_its_char_and_release()
    {
        Activate();
        var done = false;
        ToggleKey? captured = new ToggleKey(ChordModifiers.Alt, 1);
        gate.BeginCapture(key => { captured = key; done = true; });

        var decision = gate.Decide(Down(VkEscape, 0x01), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Swallow, decision.Verdict);
        Assert.Equal(GateRule.Captured, decision.Rule);
        Assert.True(done);
        Assert.Null(captured);
        Assert.False(gate.Capturing);
        Assert.Equal(GateVerdict.Swallow, Verdict(Char(0x1B, 0x01)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkEscape, 0x01)));
        Assert.Empty(Fcitx.Asked);
    }

    [Fact]
    public void The_bound_chord_pressed_during_capture_is_captured_not_toggled()
    {
        var before = session.Forwarding;
        ToggleKey? captured = null;
        gate.BeginCapture(key => captured = key);
        keyState.Modifiers = new Modifiers { Alt = true };

        Assert.Equal(GateVerdict.Swallow, Verdict(SysDown(VkOem5, ScSection)));
        Assert.Equal(ToggleKey.Default, captured);
        Assert.Equal(before, session.Forwarding);
    }

    [Fact]
    public void Cancelling_capture_from_outside_reports_nothing_and_the_next_key_is_ordinary()
    {
        var calls = 0;
        gate.BeginCapture(_ => calls++);
        gate.CancelCapture();

        Assert.False(gate.Capturing);
        Assert.Equal(0, calls);
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkJ, ScJ)));
    }

    [Fact]
    public void Section_without_alt_is_ordinary_text()
    {
        Activate();
        Fcitx.Handled = true;

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(VkOem5, ScSection)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('§', ScSection)));
        Assert.Equal([0x00A7u], Fcitx.Asked.Select(k => k.KeySym));
    }

    // --- Slash Bypass ---

    private const int VkOem2 = 0xBF; // / on US; on a Nordic layout / is Shift+7, the vk does not matter here
    private const int ScSlash = 0x35;

    /// <summary>One printing key through the gate: keydown, char, release; the char's verdict.</summary>
    private GateDecision Type(char c, int vk, int sc)
    {
        Verdict(Down(vk, sc));
        var decision = gate.Decide(Char(c, sc), chatBoxFocused: true);
        Verdict(Up(vk, sc));
        return decision;
    }

    [Fact]
    public void A_slash_into_an_empty_chat_box_starts_bypass_and_the_command_word_passes_unasked_until_the_space()
    {
        Activate();
        Fcitx.Handled = true; // Mozc would take every letter
        chatBoxEmpty = true;

        var slash = Type('/', VkOem2, ScSlash);
        Assert.Equal(GateVerdict.Pass, slash.Verdict);
        Assert.Equal(GateRule.Bypass, slash.Rule);
        chatBoxEmpty = false;
        Assert.Equal(GateVerdict.Pass, Type('e', 0x45, 0x12).Verdict);
        Assert.Equal(GateVerdict.Pass, Type('c', 0x43, 0x2E).Verdict);
        var space = Type(' ', 0x20, 0x39);
        Assert.Equal(GateVerdict.Pass, space.Verdict);
        Assert.Equal(GateRule.Bypass, space.Rule);
        Assert.Empty(Fcitx.Asked);

        Assert.Equal(GateVerdict.Swallow, Type('k', VkK, ScK).Verdict);
        Assert.Single(Fcitx.Asked);
    }

    [Fact]
    public void A_slash_mid_text_is_asked_like_any_char()
    {
        Activate();
        Fcitx.Handled = true;
        chatBoxEmpty = false;

        Assert.Equal(GateVerdict.Swallow, Type('/', VkOem2, ScSlash).Verdict);
        Assert.Single(Fcitx.Asked);
    }

    [Fact]
    public void A_slash_inside_a_composition_is_asked_even_with_an_empty_chat_box()
    {
        Activate();
        Fcitx.Handled = true;
        Fcitx.Compose("か");
        chatBoxEmpty = true;

        Assert.Equal(GateVerdict.Swallow, Type('/', VkOem2, ScSlash).Verdict);
        Assert.Single(Fcitx.Asked);
    }

    [Fact]
    public void Enter_and_escape_end_bypass()
    {
        Activate();
        Fcitx.Handled = true;
        chatBoxEmpty = true;
        Type('/', VkOem2, ScSlash);
        chatBoxEmpty = false;

        Assert.Equal(GateRule.Skipped, gate.Decide(Down(VkReturn, 0x1C), chatBoxFocused: true).Rule);
        Verdict(Char('\r', 0x1C));
        Verdict(Up(VkReturn, 0x1C));
        Assert.Equal(GateVerdict.Swallow, Type('k', VkK, ScK).Verdict);

        chatBoxEmpty = true;
        Type('/', VkOem2, ScSlash);
        chatBoxEmpty = false;
        Verdict(Down(VkEscape, 0x01));
        Verdict(Char(0x1B, 0x01));
        Verdict(Up(VkEscape, 0x01));
        Assert.Equal(GateVerdict.Swallow, Type('k', VkK, ScK).Verdict);
    }

    [Fact]
    public void The_chat_box_reading_empty_again_ends_bypass()
    {
        // Backspace over the slash: the box is empty at the next char, which is asked again — unless it is another slash.
        Activate();
        Fcitx.Handled = true;
        chatBoxEmpty = true;
        Type('/', VkOem2, ScSlash);
        chatBoxEmpty = false;
        Assert.Equal(GateVerdict.Pass, Type('e', 0x45, 0x12).Verdict);

        chatBoxEmpty = true;
        Assert.Equal(GateVerdict.Swallow, Type('k', VkK, ScK).Verdict);
        Assert.Equal(GateVerdict.Pass, Type('/', VkOem2, ScSlash).Verdict);
    }

    // --- Dead keys, surrogates, AltGr ---

    [Fact]
    public void A_dead_key_is_swallowed_silently_and_the_composed_char_that_follows_is_asked()
    {
        // Nordic layout: ¨ (dead) then a → ä as one WM_CHAR after a's keydown.
        Activate();
        Fcitx.Handled = false;
        const int vkDead = 0xBA, scDead = 0x1B;

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(vkDead, scDead)));
        var dead = gate.Decide(new KeyMessage(WindowMessage.DeadChar, 0x00A8, LParam(scDead)), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Swallow, dead.Verdict);
        Assert.Equal(GateRule.DeadChar, dead.Rule);
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(vkDead, scDead)));
        Assert.Empty(Fcitx.Asked);

        Assert.Equal(GateVerdict.Swallow, Verdict(Down(0x41, 0x1E)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char(0x00E4 /* ä */, 0x1E)));
        Assert.Equal([0x00E4u], Fcitx.Asked.Select(k => k.KeySym));
    }

    [Fact]
    public void A_dead_char_passes_while_the_gate_is_not_acting()
    {
        Assert.Equal(GateVerdict.Pass, Verdict(Down(0xBA, 0x1B)));
        Assert.Equal(GateVerdict.Pass, Verdict(new KeyMessage(WindowMessage.DeadChar, 0x00A8, LParam(0x1B))));
    }

    [Fact]
    public void A_surrogate_half_passes_unasked_and_is_reported_as_such()
    {
        Activate();
        Fcitx.Handled = true;

        Verdict(Down(VkK, ScK));
        var high = gate.Decide(Char(0xD83D, ScK), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Pass, high.Verdict);
        Assert.Equal(GateRule.SurrogateHalf, high.Rule);
        var low = gate.Decide(Char(0xDE00, ScK), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Pass, low.Verdict);
        Assert.Equal(GateRule.SurrogateHalf, low.Rule);
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK)));
        Assert.Empty(Fcitx.Asked);
    }

    [Fact]
    public void AltGr_2_is_a_printing_key_asked_at_its_char_with_the_state_as_read()
    {
        Activate();
        Fcitx.Handled = false;
        keyState.Modifiers = new Modifiers { Ctrl = true, Alt = true }; // Windows reports AltGr as Ctrl+Alt

        var down = gate.Decide(Down(0x32, 0x03), chatBoxFocused: true);
        Assert.Equal(KeyClass.Printing, down.Class);
        Assert.Equal(GateVerdict.Swallow, down.Verdict);
        Assert.Equal(GateVerdict.Pass, Verdict(Char('@', 0x03)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(0x32, 0x03)));
        Assert.Equal((uint)'@', Fcitx.Asked.Single().KeySym);
        Assert.Equal(KeyState.Ctrl | KeyState.Alt, Fcitx.Asked.Single().State);
    }

    // --- Pairing invariants (M0.3), now under fcitx5's answers ---

    [Fact]
    public void Passes_everything_while_forwarding_is_off()
    {
        session.ObserveFocus(chatBoxFocused: true);

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK)));
        Assert.Empty(Fcitx.Asked);
        Assert.Empty(Fcitx.Sent);
    }

    [Fact]
    public void Passes_everything_when_the_chat_box_is_not_focused()
    {
        Activate();
        session.ObserveFocus(chatBoxFocused: false);

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK), chatBoxFocused: false));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK), chatBoxFocused: false));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK), chatBoxFocused: false));
        Assert.Empty(Fcitx.Asked);
    }

    [Fact]
    public void A_release_goes_where_its_press_went_even_after_focus_moved()
    {
        Activate();
        Fcitx.Handled = true;

        Verdict(Down(VkK, ScK));
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK), chatBoxFocused: false));

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK), chatBoxFocused: false));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkK, ScK)));
    }

    [Fact]
    public void A_char_is_only_swallowed_when_it_follows_a_swallowed_press()
    {
        Activate();
        Fcitx.Handled = true;

        Type('k', VkK, ScK);
        // Enter passes unasked, and so does the '\r' it produces, even though a swallowed 'k' came earlier.
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkReturn, 0x1C)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('\r', 0x1C)));
        Verdict(Up(VkReturn, 0x1C));

        // A char with no press at all (e.g. injected) passes.
        Assert.Equal(GateVerdict.Pass, Verdict(Char('x', 0)));
    }

    [Fact]
    public void Ctrl_v_is_a_chord_asked_at_its_keydown_and_its_control_char_follows()
    {
        Activate();
        Fcitx.Handled = false;
        Verdict(Down(VkControl, 0x1D));
        keyState.Modifiers = new Modifiers { Ctrl = true };

        var down = gate.Decide(Down(VkV, 0x2F), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Pass, down.Verdict);
        Assert.Equal(KeyClass.Fixed, down.Class);
        Assert.True(down.Asked);
        Assert.Equal(GateVerdict.Pass, Verdict(Char(0x16 /* ^V */, 0x2F)));
        Assert.Equal(GateVerdict.Pass, Verdict(Up(VkV, 0x2F)));
        Assert.Equal([(uint)'v'], Fcitx.Asked.Select(k => k.KeySym));
    }

    [Fact]
    public void Turning_forwarding_off_still_swallows_the_release_of_a_swallowed_press()
    {
        Activate();
        Fcitx.Handled = true;

        Verdict(Down(VkK, ScK));
        Verdict(Char('k', ScK));
        session.Forwarding = false;
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK)));
    }

    [Fact]
    public void A_char_arriving_after_forwarding_went_off_is_swallowed_with_its_provisional_press()
    {
        // The game never saw the keydown; it must not see the char alone.
        Activate();

        Verdict(Down(VkK, ScK));
        session.Forwarding = false;
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
        Assert.Equal(GateVerdict.Swallow, Verdict(Up(VkK, ScK)));
        Assert.Empty(Fcitx.Asked);
    }

    [Fact]
    public void Non_keyboard_messages_pass_and_do_not_disturb_the_pairing()
    {
        Activate();
        Fcitx.Handled = true;

        Verdict(Down(VkK, ScK));
        var mouse = gate.Decide(new KeyMessage(0x0200 /* WM_MOUSEMOVE */, 0, 0), chatBoxFocused: true);
        Assert.Equal(GateVerdict.Pass, mouse.Verdict);
        Assert.Equal(GateRule.NotKeyboard, mouse.Rule);
        Assert.Equal(GateVerdict.Swallow, Verdict(Char('k', ScK)));
    }

    [Theory]
    [InlineData(false, true)]  // Forwarding off
    [InlineData(true, false)]  // typing somewhere that is not the Chat Box
    public void A_keystroke_the_gate_does_not_act_on_is_not_recorded_anywhere(bool forwarding, bool chatBoxFocused)
    {
        // What the trace and the Debug log keep is GateDecision.Acted; an
        // Inactive keystroke leaves no entry, so the ring buffer is never a
        // keylogger of mail, the FC board or a tell elsewhere (ticket 18).
        Activate();
        session.Forwarding = forwarding;

        foreach (var message in new[] { Down(VkK, ScK), Char('k', ScK), Up(VkK, ScK) })
        {
            var decision = gate.Decide(message, chatBoxFocused);
            Assert.Equal(GateRule.Inactive, decision.Rule);
            Assert.False(decision.Acted);
        }
        Assert.Empty(Fcitx.Asked);
    }

    [Fact]
    public void The_toggle_chord_and_a_capture_are_the_gate_acting_and_stay_recorded()
    {
        keyState.Modifiers = new Modifiers { Alt = true };
        var toggle = gate.Decide(SysDown(VkOem5, ScSection), chatBoxFocused: true);
        Assert.Equal(GateRule.Toggle, toggle.Rule);
        Assert.True(toggle.Acted);

        gate.BeginCapture(_ => { });
        var captured = gate.Decide(Down(VkK, ScK), chatBoxFocused: false); // "Press a key" catches it wherever focus is
        Assert.Equal(GateRule.Captured, captured.Rule);
        Assert.True(captured.Acted);
    }

    [Fact]
    public void With_no_session_the_gate_passes_everything_but_still_names_the_toggle_chord()
    {
        gate.Session = null;

        Assert.Equal(GateVerdict.Pass, Verdict(Down(VkK, ScK)));
        Assert.Equal(GateVerdict.Pass, Verdict(Char('k', ScK)));
        keyState.Modifiers = new Modifiers { Alt = true };
        Assert.Equal(GateVerdict.Toggle, Verdict(SysDown(VkOem5, ScSection)));
    }
}
