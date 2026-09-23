using FfxivImeBridge.Rendering;
using FfxivImeBridge.Session;

namespace FfxivImeBridge.Capture;

/// <summary>
/// The Gate for the mouse (ticket 16, ADR-0003): decides, per button message
/// and wheel notch and before the game sees it, by hit-testing the plan the
/// overlay drew last frame. While the <see cref="Session"/>'s gate is active,
/// the Chat Box focused and a Composition shown: over the candidate box every press, its release
/// and the wheel are Swallowed — a left press on a row selects that candidate,
/// on <c>▲</c>/<c>▼</c> pages, the wheel pages, other buttons and the padding
/// do nothing; over the preedit Swallowed and inert; anywhere else passed
/// untouched. A release goes where its button's press went, wherever it is
/// released. The calls are fire-and-forget: fcitx5's <c>UpdateClientSideUI</c>
/// arrives out of band and the tick renders it. Not thread-safe: it lives on
/// the game's message pump.
/// </summary>
internal sealed class MouseGate(Func<CompositionPlan?> lastPlan)
{
    /// <summary>Where each button's press went; null while the button is up (or its press went by before the gate saw it).</summary>
    private readonly GateVerdict?[] presses = new GateVerdict?[Enum.GetValues<MouseButton>().Length];

    /// <summary>The session whose context is called; null while there is none.</summary>
    public ForwardingSession? Session { get; set; }

    public GateDecision Decide(MouseMessage message)
    {
        if (message.Kind == MouseMessageKind.Release)
        {
            var verdict = presses[(int)message.Button] ?? GateVerdict.Pass;
            presses[(int)message.Button] = null;
            return new GateDecision(verdict, GateRule.FollowsPress);
        }

        var decision = DecidePressOrWheel(message);
        if (message.Kind == MouseMessageKind.Press) presses[(int)message.Button] = decision.Verdict;
        return decision;
    }

    private GateDecision DecidePressOrWheel(MouseMessage message)
    {
        if (Session is not { GateActive: true, ChatBoxFocused: true, Context: { } context } session || session.Composition.Preedit.IsEmpty || lastPlan() is not { } plan)
            return new GateDecision(GateVerdict.Pass, GateRule.Inactive);

        switch (plan.ZoneAt(message.Point))
        {
            case HitZone.Outside:
                return new GateDecision(GateVerdict.Pass, GateRule.Outside);
            case HitZone.Preedit:
                return new GateDecision(GateVerdict.Swallow, GateRule.Preedit);
        }

        if (message.Kind == MouseMessageKind.Wheel)
        {
            if (message.WheelDelta > 0) context.PreviousPage();
            else context.NextPage();
            return new GateDecision(GateVerdict.Swallow, GateRule.Page);
        }

        if (message.Button != MouseButton.Left || plan.RegionAt(message.Point) is not { } region)
            return new GateDecision(GateVerdict.Swallow, GateRule.CandidateBox);

        switch (region.Action)
        {
            case HitAction.SelectCandidate:
                context.SelectCandidate(region.Candidate);
                return new GateDecision(GateVerdict.Swallow, GateRule.Candidate);
            case HitAction.PreviousPage:
                context.PreviousPage();
                return new GateDecision(GateVerdict.Swallow, GateRule.Page);
            default:
                context.NextPage();
                return new GateDecision(GateVerdict.Swallow, GateRule.Page);
        }
    }
}
