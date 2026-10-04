using System.Text;
using Dalamud.Plugin.Services;

namespace FfxivImeBridge.NativeWrite;

internal enum WriteOutcome
{
    /// <summary>The text is in the Chat Box with the Cursor after it.</summary>
    Written,
    /// <summary>Overflow: the text would not fit, nothing was written, the user was told.</summary>
    Refused,
    /// <summary>The Chat Box was not there or the write threw; the text is only in the log.</summary>
    Failed,
}

/// <summary>One Native Write's outcome, for the log and the Chat Box debug tab.</summary>
internal sealed record WriteReport(DateTime At, string Text, WriteOutcome Outcome, string Detail, SplicePlan? Plan)
{
    /// <summary>Nothing was written: the Chat Box was not there, or the write threw.</summary>
    public static WriteReport Failed(string text, string detail) =>
        new(DateTime.Now, text, WriteOutcome.Failed, detail, null);

    /// <summary>Overflow: the plan says by how much, and nothing was written.</summary>
    public static WriteReport Refused(string text, SplicePlan plan, ChatBoxLimits limits) =>
        new(DateTime.Now, text, WriteOutcome.Refused, Overflow.Describe(plan, limits), plan);

    /// <summary>The text went in as planned; the detail says where, in the Chat Box's own bytes.</summary>
    public static WriteReport Written(string text, SplicePlan plan)
    {
        var where = plan.AppendedAtEnd
            ? "appended at the end with SetText (cursor unusable)"
            : $"InsertText at byte {plan.CursorByteOffset - Encoding.UTF8.GetByteCount(plan.Committed)}";
        return new(DateTime.Now, text, WriteOutcome.Written, where, plan);
    }

    /// <summary>
    /// The committed text as it went in: what fcitx5 sent, minus the control
    /// characters the plan dropped. <see cref="Text"/> is what arrived, and the
    /// two differ only when <c>Plan.ControlsDropped</c> is set.
    /// </summary>
    public string Committed => Plan?.Committed ?? Text;

    /// <summary>
    /// What the log gets at Information: the outcome and the counts, without a
    /// character of what the user typed. <c>dalamud.log</c> is a file users
    /// upload to support channels.
    /// </summary>
    public string Summary
    {
        get
        {
            var text = Encoding.UTF8.GetBytes(Committed);
            var sb = new StringBuilder($"{Outcome}: {Detail}; text {text.Length} bytes / {CursorIndex.CountCodePoints(text)} code points");
            if (Plan is { ControlsDropped: > 0 } plan) sb.Append($" ({Overflow.Count(plan.ControlsDropped, "control character")} dropped)");
            return sb.ToString();
        }
    }

    public override string ToString()
    {
        var line = $"{At:HH:mm:ss.fff} \"{Text}\" {Outcome}: {Detail}";
        return Plan == null ? line : line + Environment.NewLine
            + $"plan: cursorByteOffset={Plan.CursorByteOffset} appendedAtEnd={Plan.AppendedAtEnd} controlsDropped={Plan.ControlsDropped} charsOver={Plan.CharsOver} bytesOver={Plan.BytesOver}";
    }
}

/// <summary>
/// The Native Write: puts committed text into the Chat Box at the Cursor and
/// leaves the Cursor after it — read text and cursor, plan the splice, let the
/// game's own <c>InsertText</c> do it at its cursor with the cursor index already
/// written after the text, tell the component the selection moved so it draws
/// the cursor there. The game's splice, not <c>SetText</c>, is used because
/// only it refreshes the input module's copy of the text — the before/after
/// split its keystrokes edit at, which the game also hands back to the
/// component on focus loss (a <c>SetText</c> write would vanish on clicking out
/// of the box) — and the splice rebuilds that split from the cursor index, so
/// the index is written before it; the drawn cursor follows only the module's
/// own <c>UpdateTextSelection</c> notice. Overflow is refused before anything
/// is written, never truncated. Focus is not required: a Commit that lands
/// after the Chat Box lost focus is still the user's text and is written if the
/// box exists; only a missing box loses it, to the log. Nothing is ever sent
/// (ADR-0001). Main thread only: called from the message hook right after the
/// waited reply that produced the Commit, and from the framework tick for one
/// that arrived out of band.
/// </summary>
internal sealed class NativeWriter
{
    private readonly IGameGui gui;
    private readonly IPluginLog log;
    private readonly IChatGui chat;

    public NativeWriter(IGameGui gui, IPluginLog log, IChatGui chat)
    {
        this.gui = gui;
        this.log = log;
        this.chat = chat;
    }

    /// <summary>The last write's report, for the debug tab.</summary>
    public WriteReport? LastReport { get; private set; }
    public string? ReadError { get; private set; }

    /// <summary>The live state for the debug readout; null if the Chat Box is not there (or reading threw, see <see cref="ReadError"/>).</summary>
    public ChatBoxState? Read()
    {
        try
        {
            var state = ChatBoxAccess.Read(gui);
            ReadError = null;
            return state;
        }
        catch (Exception ex)
        {
            ReadError = ChatBoxAccess.Describe(ex);
            return null;
        }
    }

    /// <summary>Writes one Commit now. Never throws: the caller may be the game's message loop.</summary>
    public void Write(string text)
    {
        WriteReport report;
        try
        {
            report = Run(text);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Native Write: threw");
            report = WriteReport.Failed(text, "threw: " + ChatBoxAccess.Describe(ex));
        }

        Publish(report);
    }

    private unsafe WriteReport Run(string text)
    {
        var input = ChatBoxAccess.Find(gui);
        if (input == null) return WriteReport.Failed(text, "ChatLog addon or its text input not found");

        var box = ChatBoxAccess.ReadText(input);
        var plan = ChatBoxSplice.Plan(box.RawText, box.CursorByteOffset, text, box.Limits);
        if (plan.IsOverflow) return WriteReport.Refused(text, plan, box.Limits);

        var expected = CursorIndex.FromByteOffset(plan.Text, plan.CursorByteOffset);
        if (plan.AppendedAtEnd)
        {
            ChatBoxAccess.SetText(input, plan.Text); // the cursor cannot be trusted: write the whole planned text
            ChatBoxAccess.SetCursor(input, expected);
        }
        else
        {
            // The game's splice lands at the module's before/after split, which is
            // where its next keystroke goes too, and rebuilds that split from
            // CursorPos once done — so the cursor goes in first, so that
            // the rebuilt split is after the text; the cursor write alone moves the
            // index and leaves the split, and the drawn cursor, where the splice began.
            ChatBoxAccess.SetCursor(input, expected);
            ChatBoxAccess.InsertText(input, plan.Committed);
            ChatBoxAccess.NotifySelection(input, expected); // draws the cursor there; nothing else does
            ChatBoxAccess.SetCursor(input, expected);
        }
        return WriteReport.Written(text, plan);
    }

    private void Publish(WriteReport report)
    {
        LastReport = report;
        // The user's draft never reaches the log above Debug: the
        // lines on by default carry outcomes and counts, the text-bearing ones
        // only appear once the log level is lowered.
        switch (report.Outcome)
        {
            case WriteOutcome.Written:
                log.Information("Native Write: {Summary}", report.Summary);
                log.Debug("Native Write: {Report}", report.ToString());
                break;
            case WriteOutcome.Refused:
                // Local client-side line, not a sent message (ADR-0001).
                chat.Print(Strings.Overflow(report.Detail));
                log.Warning("Native Write: refused, {Detail}", report.Detail);
                log.Debug("Native Write: refused text: {Text}", report.Text);
                break;
            case WriteOutcome.Failed:
                log.Warning("Native Write: failed, {Detail}; the committed text is lost ({Bytes} bytes)", report.Detail, Encoding.UTF8.GetByteCount(report.Text));
                log.Debug("Native Write: lost text: {Text}", report.Text);
                break;
        }
    }
}
