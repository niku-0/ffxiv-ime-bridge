using System.Collections.Immutable;
using System.Numerics;
using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.Rendering;
using FfxivImeBridge.Session;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// The mouse over the overlay (ticket 16): a press, its release and the wheel
/// over the candidate box are Swallowed and act; over the preedit Swallowed
/// and inert; elsewhere passed. The plan is a fixed one, as the overlay would
/// have kept it from the last frame.
/// </summary>
public sealed class MouseGateTests
{
    private static readonly Rect PreeditBox = new(100, 700, 50, 20);
    private static readonly Rect CandidateBox = new(100, 600, 100, 80);
    private static readonly HitRegion Row0 = new(new Rect(104, 604, 92, 20), HitAction.SelectCandidate, 0);
    private static readonly HitRegion Row1 = new(new Rect(104, 626, 92, 20), HitAction.SelectCandidate, 1);
    private static readonly HitRegion Prev = new(new Rect(104, 648, 10, 20), HitAction.PreviousPage);
    private static readonly HitRegion Next = new(new Rect(126, 648, 10, 20), HitAction.NextPage);
    private static readonly CompositionPlan Shown = new([], PreeditBox, CandidateBox, [Row0, Row1, Prev, Next]);

    private static readonly Vector2 OnRow1 = new(150, 630);
    private static readonly Vector2 OnPrev = new(105, 650);
    private static readonly Vector2 OnNext = new(130, 650);
    private static readonly Vector2 OnPadding = new(101, 601);
    private static readonly Vector2 OnPreedit = new(120, 710);
    private static readonly Vector2 Elsewhere = new(50, 50);

    private readonly FakeFcitx fcitx = new();
    private readonly ForwardingSession session;
    private readonly MouseGate gate;
    private CompositionPlan? plan = Shown;

    public MouseGateTests()
    {
        session = ForwardingSession.OpenAsync(fcitx, _ => { }, _ => { }).GetAwaiter().GetResult();
        gate = new MouseGate(() => plan) { Session = session };
    }

    private FakeInputContext Fcitx => fcitx.Created[^1];

    /// <summary>Forwarding on, Chat Box focused, a Composition up and drawn.</summary>
    private void Compose()
    {
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        Fcitx.Compose("か");
        session.TakeSnapshot();
    }

    private static MouseMessage Press(Vector2 at, uint message = MouseWindowMessage.LButtonDown) => new(message, 0, at);
    private static MouseMessage Release(Vector2 at, uint message = MouseWindowMessage.LButtonUp) => new(message, 0, at);
    private static MouseMessage Wheel(Vector2 at, int delta) => new(MouseWindowMessage.MouseWheel, unchecked((nuint)(uint)(delta << 16)), at);

    [Fact]
    public void A_left_press_on_a_row_selects_that_candidate_and_is_swallowed_with_its_release()
    {
        Compose();

        var press = gate.Decide(Press(OnRow1));
        var release = gate.Decide(Release(OnRow1));

        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.Candidate), press);
        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.FollowsPress), release);
        Assert.Equal(["FocusIn", "SelectCandidate 1"], Fcitx.Calls);
    }

    [Fact]
    public void A_left_press_on_a_mark_pages()
    {
        Compose();

        Assert.Equal(GateRule.Page, gate.Decide(Press(OnPrev)).Rule);
        gate.Decide(Release(OnPrev));
        Assert.Equal(GateRule.Page, gate.Decide(Press(OnNext)).Rule);

        Assert.Equal(["FocusIn", "PrevPage", "NextPage"], Fcitx.Calls);
    }

    [Fact]
    public void The_wheel_over_the_box_pages_and_is_swallowed()
    {
        Compose();

        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.Page), gate.Decide(Wheel(OnPadding, 120)));
        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.Page), gate.Decide(Wheel(OnRow1, -120)));

        Assert.Equal(["FocusIn", "PrevPage", "NextPage"], Fcitx.Calls);
    }

    [Theory]
    [InlineData(MouseWindowMessage.RButtonDown, MouseWindowMessage.RButtonUp)]
    [InlineData(MouseWindowMessage.MButtonDown, MouseWindowMessage.MButtonUp)]
    [InlineData(MouseWindowMessage.XButtonDown, MouseWindowMessage.XButtonUp)]
    public void Other_buttons_over_the_box_are_swallowed_and_do_nothing(uint down, uint up)
    {
        Compose();

        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.CandidateBox), gate.Decide(Press(OnRow1, down)));
        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.FollowsPress), gate.Decide(Release(Elsewhere, up)));
        Assert.Equal(["FocusIn"], Fcitx.Calls);
    }

    [Fact]
    public void A_left_press_on_the_padding_is_swallowed_and_does_nothing()
    {
        Compose();

        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.CandidateBox), gate.Decide(Press(OnPadding)));
        Assert.Equal(["FocusIn"], Fcitx.Calls);
    }

    [Fact]
    public void Over_the_preedit_everything_is_swallowed_and_nothing_is_called()
    {
        Compose();

        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.Preedit), gate.Decide(Press(OnPreedit)));
        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.FollowsPress), gate.Decide(Release(OnPreedit)));
        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.Preedit), gate.Decide(Press(OnPreedit, MouseWindowMessage.RButtonDown)));
        Assert.Equal(new GateDecision(GateVerdict.Swallow, GateRule.Preedit), gate.Decide(Wheel(OnPreedit, 120)));
        Assert.Equal(["FocusIn"], Fcitx.Calls);
    }

    [Fact]
    public void Elsewhere_everything_passes()
    {
        Compose();

        Assert.Equal(new GateDecision(GateVerdict.Pass, GateRule.Outside), gate.Decide(Press(Elsewhere)));
        Assert.Equal(new GateDecision(GateVerdict.Pass, GateRule.FollowsPress), gate.Decide(Release(OnRow1))); // released over the box: still the press's
        Assert.Equal(new GateDecision(GateVerdict.Pass, GateRule.Outside), gate.Decide(Wheel(Elsewhere, 120)));
        Assert.Equal(["FocusIn"], Fcitx.Calls);
    }

    [Fact]
    public void A_release_follows_its_own_button_only()
    {
        Compose();

        gate.Decide(Press(OnRow1));
        gate.Decide(Press(Elsewhere, MouseWindowMessage.RButtonDown));

        Assert.Equal(GateVerdict.Pass, gate.Decide(Release(OnRow1, MouseWindowMessage.RButtonUp)).Verdict);
        Assert.Equal(GateVerdict.Swallow, gate.Decide(Release(Elsewhere)).Verdict);
        Assert.Equal(GateVerdict.Pass, gate.Decide(Release(OnRow1)).Verdict); // a release with no press on record passes
    }

    [Fact]
    public void Nothing_acts_without_a_composition_shown()
    {
        var inert = new GateDecision(GateVerdict.Pass, GateRule.Inactive);

        // Forwarding off.
        Assert.Equal(inert, gate.Decide(Press(OnRow1)));

        // Forwarding on, but no Composition in the snapshot.
        session.Forwarding = true;
        session.ObserveFocus(chatBoxFocused: true);
        Assert.Equal(inert, gate.Decide(Press(OnRow1)));

        // A Composition, but the overlay drew nothing last frame.
        Fcitx.Compose("か");
        session.TakeSnapshot();
        plan = null;
        Assert.Equal(inert, gate.Decide(Press(OnRow1)));
        Assert.Equal(inert, gate.Decide(Wheel(OnRow1, 120)));

        // No session at all.
        gate.Session = null;
        plan = Shown;
        Assert.Equal(inert, gate.Decide(Press(OnRow1)));

        // Drawn, but the Chat Box has since lost focus (the session's own Reset and FocusOut are not the mouse's).
        gate.Session = session;
        session.ObserveFocus(chatBoxFocused: false);
        Assert.Equal(inert, gate.Decide(Press(OnRow1)));

        Assert.Equal(["FocusIn", "Reset", "FocusOut"], Fcitx.Calls);
    }
}
