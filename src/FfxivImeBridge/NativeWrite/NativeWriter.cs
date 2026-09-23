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

/// <summary>One Native Write, with everything read around it, for the log and the Chat Box debug tab.</summary>
internal sealed record WriteReport(
    DateTime At,
    string Text,
    WriteOutcome Outcome,
    string Detail,
    ChatBoxState? Before,
    SplicePlan? Plan,
    int? ExpectedCursorIndex,
    ChatBoxState? AfterWrite)
{
    private const string Indent = "\n        ";

    /// <summary>Read again a couple of frames later: whether the write held. Null until then, and for a write that never happened.</summary>
    public ChatBoxState? NextFrame { get; init; }

    /// <summary>Nothing more to learn: refused, failed, or <see cref="NextFrame"/> is in. Until then the detail is provisional.</summary>
    public bool Complete { get; init; }

    /// <summary>Nothing was written and nothing more will be read: the Chat Box was not there, or the write threw.</summary>
    public static WriteReport Failed(string text, string detail) =>
        new(DateTime.Now, text, WriteOutcome.Failed, detail, null, null, null, null) { Complete = true };

    /// <summary>Overflow: the plan says by how much, and nothing was written.</summary>
    public static WriteReport Refused(string text, ChatBoxState before, SplicePlan plan) =>
        new(DateTime.Now, text, WriteOutcome.Refused, Overflow.Describe(plan, before.Limits), before, plan, null, null) { Complete = true };

    /// <summary>
    /// The committed text as it went in: what fcitx5 sent, minus the control
    /// characters the plan dropped (ticket 19). <see cref="Text"/> is what
    /// arrived, and the two differ only when <c>Plan.ControlsDropped</c> is set.
    /// </summary>
    public string Committed => Plan?.Committed ?? Text;

    /// <summary>
    /// Where the text was written, in the Chat Box's own bytes; null when
    /// nothing was written. Its own property rather than part of
    /// <see cref="Detail"/>, because the next-frame read replaces Detail with
    /// its verdict and both log lines need it.
    /// </summary>
    public string? Where => Outcome == WriteOutcome.Written && Plan is { } plan
        ? plan.AppendedAtEnd
            ? "appended at the end with SetText (cursor unusable)"
            : $"InsertText at byte {plan.CursorByteOffset - Encoding.UTF8.GetByteCount(plan.Committed)}"
        : null;

    /// <summary>
    /// What the log gets at Information: the same outcome and numbers as
    /// <see cref="ToString"/>, without a character of what the user typed.
    /// The report carries whole Chat Box contents, and <c>dalamud.log</c> is a
    /// file users upload to support channels (ticket 18).
    /// </summary>
    public string Summary
    {
        get
        {
            var text = Encoding.UTF8.GetBytes(Committed);
            var sb = new StringBuilder($"{Outcome}: {Detail}");
            if (Where is { } where) sb.Append($"; {where}");
            sb.Append($"; text {Counts(text.Length, CursorIndex.CountCodePoints(text))}");
            if (Plan is { ControlsDropped: > 0 } plan) sb.Append($" ({Overflow.Count(plan.ControlsDropped, "control character")} dropped)");
            if (Before != null) sb.Append($"; before {Counts(Before)}");
            if (AfterWrite != null) sb.Append($"; after write {Counts(AfterWrite)}");
            if (NextFrame != null) sb.Append($"; next frame {Counts(NextFrame)}");
            return sb.ToString();
        }
    }

    private static string Counts(int bytes, int codePoints) => $"{bytes} bytes / {codePoints} code points";

    private static string Counts(ChatBoxState state) => $"{Counts(state.RawText.Length, state.RawCodePoints)}, cursor {state.CursorIndex}";

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{At:HH:mm:ss.fff} \"{Text}\" {Outcome}: {Detail}{(Where is { } where ? $"; {where}" : "")}");
        if (Before != null) sb.AppendLine("before: " + Before.ToString().ReplaceLineEndings(Indent));
        if (Plan != null) sb.AppendLine($"plan: cursorByteOffset={Plan.CursorByteOffset} appendedAtEnd={Plan.AppendedAtEnd} controlsDropped={Plan.ControlsDropped} charsOver={Plan.CharsOver} bytesOver={Plan.BytesOver} expectedCursorIndex={ExpectedCursorIndex?.ToString() ?? "-"}");
        if (AfterWrite != null) sb.AppendLine("after write: " + AfterWrite.ToString().ReplaceLineEndings(Indent));
        if (NextFrame != null) sb.AppendLine("next frame: " + NextFrame.ToString().ReplaceLineEndings(Indent));
        return sb.ToString();
    }
}

/// <summary>
/// The Native Write: puts committed text into the Chat Box at the Cursor and
/// leaves the Cursor after it — read text and cursor, plan the splice, let the
/// game's own <c>InsertText</c> do it at its cursor with the cursor index already
/// written after the text, tell the component the selection moved so it draws
/// the cursor there. Ticket 04 proved the cursor write holds; the game's splice,
/// not <c>SetText</c>, is used because only it refreshes the input module's copy
/// of the text — the before/after split its keystrokes edit at, which the game
/// also hands back to the component on focus loss (a <c>SetText</c> write
/// vanished on clicking out of the box) — and the splice rebuilds that split
/// from the cursor index, so the index is written before it; the drawn cursor
/// follows only the module's own <c>UpdateTextSelection</c> notice (ticket 20).
/// Overflow is
/// refused before anything is written, never truncated. Focus is not required: a Commit that
/// lands after the Chat Box lost focus is still the user's text and is written
/// if the box exists; only a missing box loses it, to the log. Nothing is ever
/// sent (ADR-0001). Main thread only: called from the message hook right after
/// the waited reply that produced the Commit, and from the framework tick for
/// one that arrived out of band.
/// </summary>
internal sealed class NativeWriter
{
    private const int ReadbackTicks = 2;

    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly IChatGui chat;

    public NativeWriter(IGameGui gui, IFramework framework, IPluginLog log, IChatGui chat)
    {
        this.gui = gui;
        this.framework = framework;
        this.log = log;
        this.chat = chat;
    }

    /// <summary>The last write's report, for the debug tab; a newer write replaces it even while its next-frame read is pending.</summary>
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
        if (report.Complete) return;

        framework.RunOnTick(() =>
        {
            if (!ReferenceEquals(LastReport, report)) return; // a newer write has its own readback
            var later = Read();
            Publish(report with { NextFrame = later, Detail = Verdict(report, later), Complete = true });
        }, delayTicks: ReadbackTicks);
    }

    private unsafe WriteReport Run(string text)
    {
        var input = ChatBoxAccess.Find(gui);
        if (input == null) return WriteReport.Failed(text, "ChatLog addon or its text input not found");

        var before = ChatBoxAccess.Read(input);
        var plan = ChatBoxSplice.Plan(before.RawText, before.CursorByteOffset, text, before.Limits);
        if (plan.IsOverflow) return WriteReport.Refused(text, before, plan);

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
            // CursorPos once done (ticket 20) — so the cursor goes in first, so that
            // the rebuilt split is after the text; the cursor write alone moves the
            // index and leaves the split, and the drawn cursor, where the splice began.
            ChatBoxAccess.SetCursor(input, expected);
            ChatBoxAccess.InsertText(input, plan.Committed);
            ChatBoxAccess.NotifySelection(input, expected); // draws the cursor there; nothing else does
            ChatBoxAccess.SetCursor(input, expected);
        }
        var after = ChatBoxAccess.Read(input);

        var focus = before.IsModuleTarget ? "" : " (Chat Box not focused)";
        return new WriteReport(DateTime.Now, text, WriteOutcome.Written,
            $"cursor index {before.CursorIndex} → {after.CursorIndex} right after writing {expected}{focus}; waiting for the next frame",
            before, plan, expected, after);
    }

    /// <summary>
    /// Compares in byte offsets. The blind spot: nothing has been typed between
    /// the write and this read, so a cursor the game only re-derives on the next
    /// keystroke would still look right here — ticket 04's run typed one more
    /// letter to check, and it held.
    /// </summary>
    private static string Verdict(WriteReport report, ChatBoxState? later)
    {
        if (later == null) return "written, but the Chat Box was gone at the next frame";
        var contains = later.RawString.Contains(report.Committed, StringComparison.Ordinal);
        var cursor = later.CursorByteOffset?.ToString() ?? $"unusable ({later.CursorIndex})";
        var cursorNote = report.Plan is { } plan
            ? cursor == plan.CursorByteOffset.ToString() ? $"cursor at byte {cursor} as expected" : $"cursor at byte {cursor}, expected {plan.CursorByteOffset}"
            : $"cursor at byte {cursor}";
        var textNote = !contains ? "text is NOT in the Chat Box"
            : report.Plan is { } p && !later.RawText.AsSpan().SequenceEqual(p.Text) ? "text is in the Chat Box but not where planned"
            : "text is in the Chat Box";
        var appended = report.Plan is { AppendedAtEnd: true } ? "; fell back to append-at-end" : "";
        return $"{textNote}; {cursorNote}{appended}";
    }

    private void Publish(WriteReport report)
    {
        LastReport = report;
        // The user's draft never reaches the log above Debug (ticket 18): the
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
