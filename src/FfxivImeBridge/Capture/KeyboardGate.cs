using System.Diagnostics;
using FfxivImeBridge.Fcitx;
using FfxivImeBridge.Session;

namespace FfxivImeBridge.Capture;

internal enum GateVerdict
{
    /// <summary>Let the game have the message.</summary>
    Pass,
    /// <summary>Drop the message before the game sees it.</summary>
    Swallow,
    /// <summary>The toggle chord: swallowed, and the owner should flip Forwarding.</summary>
    Toggle,
}

/// <summary>Which rule produced a verdict; what the trace shows next to it.</summary>
internal enum GateRule
{
    /// <summary>Not a keyboard message.</summary>
    NotKeyboard,
    /// <summary>The gate is not acting (Forwarding off, Degraded, the Chat Box not focused — for the mouse, no Composition shown): passed untouched, and not traced (ticket 18). A char or release that follows such a press is Inactive too.</summary>
    Inactive,
    /// <summary>The toggle chord, swallowed whole.</summary>
    Toggle,
    /// <summary>A modifier key: sent, never asked, always passed.</summary>
    Modifier,
    /// <summary>A printing key's keydown: swallowed until its char decides.</summary>
    Provisional,
    /// <summary>Asked, and fcitx5 answered in time.</summary>
    Answered,
    /// <summary>Asked, and fcitx5 did not answer in time: swallowed (ADR-0002).</summary>
    TimedOut,
    /// <summary>A key on the outside-Composition skip list: passed without asking.</summary>
    Skipped,
    /// <summary>Slash Bypass: passed without asking.</summary>
    Bypass,
    /// <summary>A char or release: goes where its press went.</summary>
    FollowsPress,
    /// <summary>A dead key's char: swallowed silently; the composed character follows.</summary>
    DeadChar,
    /// <summary>Half a UTF-16 surrogate pair: passed unasked (ticket 06's rule).</summary>
    SurrogateHalf,
    /// <summary>Toggle Key capture ("Press a key"): the key that ended it, Escape included, swallowed whole.</summary>
    Captured,
    /// <summary>Mouse (ticket 16): a press or the wheel off the overlay, passed untouched.</summary>
    Outside,
    /// <summary>Mouse: on the preedit, swallowed and inert.</summary>
    Preedit,
    /// <summary>Mouse: a left press on a candidate, swallowed; <c>SelectCandidate</c> called.</summary>
    Candidate,
    /// <summary>Mouse: a left press on <c>▲</c>/<c>▼</c> or a wheel notch over the box, swallowed; a page call made.</summary>
    Page,
    /// <summary>Mouse: elsewhere in the candidate box (another button, the padding), swallowed and inert.</summary>
    CandidateBox,
}

/// <summary>The verdict on one message and how it was reached. <see cref="WaitMs"/> is set iff fcitx5 was asked.</summary>
internal readonly record struct GateDecision(GateVerdict Verdict, GateRule Rule, KeyClass? Class = null, double? WaitMs = null)
{
    public bool Asked => WaitMs is not null;

    /// <summary>
    /// The gate acted on the message rather than letting it past untouched.
    /// What the trace and the Debug log keep: everything else is the user
    /// typing somewhere that is none of the plugin's business — mail, the FC
    /// board, market search, a tell in another window (ticket 18).
    /// </summary>
    public bool Acted => Rule != GateRule.Inactive;
}

/// <summary>
/// The Gate: decides, one message at a time and synchronously, whether a
/// keyboard message reaches the game — by asking fcitx5 (ADR-0002) while the
/// <see cref="Session"/>'s gate is active and the Chat Box is focused. What
/// holds regardless of fcitx5's answers:
/// <list type="bullet">
/// <item>the game sees a whole keystroke or none of it: a press's chars and its
/// release go where the press finally went (for a printing key, where its
/// <c>WM_CHAR</c> went — the keydown is swallowed provisionally until then);</item>
/// <item>every auto-repeat is a fresh press, decided afresh;</item>
/// <item>the <see cref="ToggleKey"/> (by default Alt + the key left of 1,
/// matched by scancode: § on a Nordic layout, ` on US) is reported as
/// <see cref="GateVerdict.Toggle"/> and swallowed whole before anything is
/// asked; the owner flips Forwarding;</item>
/// <item>while a capture is on (<see cref="BeginCapture"/>), the next
/// non-modifier keydown is the new chord — or Escape, which cancels — and is
/// swallowed whole, wherever focus is.</item>
/// </list>
/// Not thread-safe: it lives on the game's message pump.
/// </summary>
internal sealed class KeyboardGate
{
    /// <summary>PC set-1 scancode of the key left of 1, whatever the layout prints on it.</summary>
    public const int DefaultToggleScanCode = 0x29;

    /// <summary>ADR-0002: measured ~10 ms from inside Wine; a key past this is swallowed.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>This many timeouts in a row, with no reply between, mean fcitx5 is not answering: Forwarding goes Degraded.</summary>
    public const int TimeoutsBeforeDegraded = 3;

    private readonly IKeyStateReader keyState;
    private readonly Func<bool> chatBoxEmpty;
    private readonly Press[] presses = new Press[256];
    private int charsBelongTo = -1;
    private int consecutiveTimeouts;
    private bool bypass;
    private Action<ToggleKey?>? capture;

    /// <param name="keyState">The modifiers as they stand for the message in hand.</param>
    /// <param name="chatBoxEmpty">Whether the Chat Box holds no text, read at a <c>/</c> (Slash Bypass).</param>
    public KeyboardGate(IKeyStateReader keyState, Func<bool> chatBoxEmpty)
    {
        this.keyState = keyState;
        this.chatBoxEmpty = chatBoxEmpty;
    }

    /// <summary>The session whose context is asked; null while there is none (the gate then only knows the toggle chord).</summary>
    public ForwardingSession? Session { get; set; }

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>The chord that flips Forwarding, from the config; unbound matches nothing.</summary>
    public ToggleKey ToggleKey { get; set; } = ToggleKey.Default;

    /// <summary>A "Press a key" capture is waiting for its keydown.</summary>
    public bool Capturing => capture is not null;

    /// <summary>
    /// The next non-modifier keydown, with the Ctrl/Alt/Shift held at it, goes
    /// to <paramref name="onCaptured"/> instead of anywhere else — null when
    /// it was Escape. One keydown only; the chord's modifiers are the caller's
    /// to complete when none was held.
    /// </summary>
    public void BeginCapture(Action<ToggleKey?> onCaptured) => capture = onCaptured;

    /// <summary>Ends a capture without a key; the callback is not called.</summary>
    public void CancelCapture() => capture = null;

    /// <summary>The session and its context while the gate acts at all (<c>Forwarding &amp;&amp; !Degraded</c>); null otherwise.</summary>
    private (ForwardingSession Session, IInputContextClient Context)? Acting =>
        Session is { GateActive: true, Context: { } context } session ? (session, context) : null;

    public GateDecision Decide(KeyMessage message, bool chatBoxFocused)
    {
        var modifiers = keyState.Read();
        switch (message.Kind)
        {
            case KeyMessageKind.KeyDown:
                return DecidePress(message, modifiers, chatBoxFocused);
            case KeyMessageKind.KeyUp:
                return DecideRelease(message, modifiers);
            case KeyMessageKind.Char:
                return DecideChar(message, modifiers);
            case KeyMessageKind.DeadChar:
                return DecideDeadChar();
            default:
                return new GateDecision(GateVerdict.Pass, GateRule.NotKeyboard);
        }
    }

    private GateDecision DecidePress(KeyMessage message, Modifiers modifiers, bool chatBoxFocused)
    {
        var vk = message.VirtualKey;
        var keyClass = KeyTranslation.Classify(message, modifiers);
        var held = presses[vk];
        charsBelongTo = vk;

        if (capture is { } onCaptured && keyClass != KeyClass.Modifier)
        {
            capture = null;
            presses[vk] = new Press(GateVerdict.Swallow);
            onCaptured(vk == VirtualKey.Escape ? null : new ToggleKey(ToggleKey.FromHeld(modifiers.For(message)), message.Set1ScanCode));
            return new GateDecision(GateVerdict.Swallow, GateRule.Captured, keyClass);
        }

        if (chatBoxFocused && ToggleKey.Matches(modifiers.For(message), message.Set1ScanCode))
        {
            // Held, the chord is one toggle, not a flicker; the repeats, chars and release are still eaten.
            var verdict = held.Toggled && message.IsRepeat ? GateVerdict.Swallow : GateVerdict.Toggle;
            presses[vk] = new Press(GateVerdict.Swallow, Toggled: true);
            return new GateDecision(verdict, GateRule.Toggle, keyClass);
        }

        if (!chatBoxFocused || Acting is not var (session, context))
        {
            presses[vk] = new Press(GateVerdict.Pass, Untouched: true);
            return new GateDecision(GateVerdict.Pass, GateRule.Inactive, keyClass);
        }

        if (vk is VirtualKey.Return or VirtualKey.Escape) bypass = false;

        switch (keyClass)
        {
            case KeyClass.Modifier:
                var modifier = KeyTranslation.FromKey(message, modifiers);
                context.SendKey(modifier);
                presses[vk] = new Press(GateVerdict.Pass, Sent: modifier);
                return new GateDecision(GateVerdict.Pass, GateRule.Modifier, keyClass);

            case KeyClass.Printing:
                presses[vk] = new Press(GateVerdict.Swallow, Provisional: true);
                return new GateDecision(GateVerdict.Swallow, GateRule.Provisional, keyClass);

            case KeyClass.Fixed when KeyTranslation.SkipsOutsideComposition(message, modifiers) && !session.IsComposing:
                presses[vk] = new Press(GateVerdict.Pass);
                return new GateDecision(GateVerdict.Pass, GateRule.Skipped, keyClass);

            case KeyClass.Fixed:
                var key = KeyTranslation.FromKey(message, modifiers);
                var (verdict, rule, wait) = Ask(session, context, key);
                presses[vk] = new Press(verdict, Sent: key);
                return new GateDecision(verdict, rule, keyClass, wait);

            default:
                throw new UnreachableException($"{message} classified as {keyClass}"); // a keydown is never DeadChar
        }
    }

    private GateDecision DecideRelease(KeyMessage message, Modifiers modifiers)
    {
        var vk = message.VirtualKey;
        if (charsBelongTo == vk) charsBelongTo = -1;
        var press = presses[vk];
        presses[vk] = default;

        // Told only if its press was: fcitx5 never heard of a skipped or unforwarded keydown.
        if (press.Sent is { } sent) Session?.Context?.SendKey(sent.AsRelease());
        return new GateDecision(press.Verdict, Following(press), KeyTranslation.Classify(message, modifiers));
    }

    private GateDecision DecideChar(KeyMessage message, Modifiers modifiers)
    {
        if (charsBelongTo < 0) return new GateDecision(GateVerdict.Pass, GateRule.Inactive); // no press to follow: the gate did nothing to it
        var vk = charsBelongTo;
        var press = presses[vk];

        if (message.IsSurrogateHalf)
        {
            // A surrogate half cannot be asked or passed alone (ticket 06): both halves pass, as if declined.
            if (press.Provisional) presses[vk] = new Press(GateVerdict.Pass);
            return new GateDecision(GateVerdict.Pass, Following(press, GateRule.SurrogateHalf), KeyClass.Printing);
        }

        if (!press.Provisional) return new GateDecision(press.Verdict, Following(press));

        // The press is swallowed so far; this char decides it for good — and can only be asked while the gate still acts.
        if (Acting is not var (session, context))
        {
            presses[vk] = new Press(GateVerdict.Swallow);
            return new GateDecision(GateVerdict.Swallow, GateRule.FollowsPress, KeyClass.Printing);
        }

        if (Bypasses(message, session))
        {
            presses[vk] = new Press(GateVerdict.Pass);
            return new GateDecision(GateVerdict.Pass, GateRule.Bypass, KeyClass.Printing);
        }

        var key = KeyTranslation.FromChar(message, modifiers)!.Value;
        var (verdict, rule, wait) = Ask(session, context, key);
        presses[vk] = new Press(verdict, Sent: key);
        return new GateDecision(verdict, rule, KeyClass.Printing, wait);
    }

    /// <summary>A dead key's own char: swallowed silently with its (provisional) press; the composed character follows the next key.</summary>
    private GateDecision DecideDeadChar()
    {
        if (charsBelongTo < 0) return new GateDecision(GateVerdict.Pass, GateRule.Inactive, KeyClass.DeadChar);
        var press = presses[charsBelongTo];
        if (press.Provisional) presses[charsBelongTo] = new Press(GateVerdict.Swallow);
        return new GateDecision(press.Verdict, press.Provisional ? GateRule.DeadChar : Following(press), KeyClass.DeadChar);
    }

    /// <summary>
    /// The rule for a message that goes where its press went. A press the gate
    /// never touched keeps its chars and its release <see cref="GateRule.Inactive"/>:
    /// a <c>WM_CHAR</c> carries the character typed, and one typed past an
    /// inactive gate is none of the plugin's business (ticket 18).
    /// </summary>
    private static GateRule Following(Press press, GateRule rule = GateRule.FollowsPress) => press.Untouched ? GateRule.Inactive : rule;

    /// <summary>
    /// Slash Bypass: a <c>/</c> into an empty Chat Box with no Composition starts
    /// it, and the command word passes unasked up to and including the space
    /// that ends it; the box reading empty again ends it too (Enter and Escape
    /// end it at their keydown). A <c>/</c> mid-text is ordinary text.
    /// </summary>
    private bool Bypasses(KeyMessage character, ForwardingSession session)
    {
        var empty = chatBoxEmpty();
        if (bypass && empty) bypass = false;
        if (!bypass)
        {
            if (character.CodePoint != '/' || !empty || session.IsComposing) return false;
            bypass = true;
            return true;
        }
        if (character.CodePoint == ' ') bypass = false;
        return true;
    }

    /// <summary>The waited call (ADR-0002): handled ⇒ swallow, declined ⇒ pass, no answer in time ⇒ swallow.</summary>
    private (GateVerdict Verdict, GateRule Rule, double WaitMs) Ask(ForwardingSession session, IInputContextClient context, KeyEvent key)
    {
        var call = context.ProcessKeyAsync(key);
        var started = Stopwatch.GetTimestamp();
        bool answered;
        try
        {
            answered = call.Wait(Timeout);
        }
        catch (AggregateException)
        {
            answered = false; // a fault is logged by the client; to the gate it is silence
        }
        var waited = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (!answered)
        {
            if (++consecutiveTimeouts >= TimeoutsBeforeDegraded)
            {
                consecutiveTimeouts = 0;
                session.MarkDegraded(DegradedCause.Timeouts);
            }
            return (GateVerdict.Swallow, GateRule.TimedOut, waited);
        }
        consecutiveTimeouts = 0;
        return (call.Result ? GateVerdict.Swallow : GateVerdict.Pass, GateRule.Answered, waited);
    }

    /// <summary>What is known about a key that is down: where it went, and the press fcitx5 was told about, if any.</summary>
    /// <param name="Provisional">A printing key whose char has not come yet: swallowed so far, the char decides.</param>
    /// <param name="Toggled">The toggle chord: its repeats must not toggle again.</param>
    /// <param name="Untouched">The gate was not acting at the press: its chars and its release are <see cref="GateRule.Inactive"/> too, so nothing typed past the gate is traced (ticket 18).</param>
    private readonly record struct Press(GateVerdict Verdict, bool Provisional = false, KeyEvent? Sent = null, bool Toggled = false, bool Untouched = false);
}
