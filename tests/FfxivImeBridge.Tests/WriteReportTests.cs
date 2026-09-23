using System.Text;
using FfxivImeBridge.NativeWrite;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// The report is logged twice: <see cref="WriteReport.Summary"/> at
/// Information, which is on for everyone, and <see cref="WriteReport.ToString"/>
/// at Debug. Only the second may name what the user typed (ticket 18).
/// </summary>
public sealed class WriteReportTests
{
    private const string Draft = "/tell Someone@World ";
    private const string Committed = "こんばんは";

    private static ChatBoxState State(string raw, int cursor) => new(
        RawText: Encoding.UTF8.GetBytes(raw), EvaluatedText: raw, IsActive: true,
        ComponentCursor: cursor, ComponentSelectionStart: cursor, ComponentSelectionEnd: cursor,
        IsModuleTarget: true, ModuleCursor: (short)cursor, ModuleTextLength: 0, ModuleSelectionStart: (short)cursor, ModuleSelectionEnd: (short)cursor,
        ModuleBeforeSelectionBytes: 0, ModuleSelectedBytes: 0, ModuleAfterSelectionBytes: 0, ModuleInputBytes: 0,
        InputMaxLength: 500, MaxChar: 0, MaxByte: 500, MaxLine: 1, MaxWidth: 0, HandlerMaxChar: null, HandlerMaxByte: null,
        TextNode: null, CursorNode: null, Style: default, MeasuredCursorX: null, MeasureError: null);

    /// <summary>A write as <c>NativeWriter</c> publishes it the first time: no next-frame read yet.</summary>
    private static WriteReport Written()
    {
        var before = State(Draft, Draft.Length);
        var after = State(Draft + Committed, Draft.Length + Committed.Length);
        var plan = ChatBoxSplice.Plan(before.RawText, before.CursorByteOffset, Committed, before.Limits);
        return new WriteReport(DateTime.Now, Committed, WriteOutcome.Written,
            $"cursor index {before.CursorIndex} → {after.CursorIndex} right after writing {after.CursorIndex}; waiting for the next frame",
            before, plan, after.CursorIndex, after);
    }

    /// <summary>And as it publishes it again two frames later, with the verdict in place of the first detail.</summary>
    private static WriteReport Held()
    {
        var report = Written();
        return report with { NextFrame = report.AfterWrite, Detail = "text is in the Chat Box; cursor at byte 35 as expected", Complete = true };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_summary_of_a_write_names_neither_the_committed_text_nor_the_chat_box(bool afterTheReadback)
    {
        var summary = (afterTheReadback ? Held() : Written()).Summary;

        Assert.DoesNotContain(Committed, summary, StringComparison.Ordinal);
        Assert.DoesNotContain(Draft, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_carries_the_outcome_and_the_counts_instead()
    {
        var summary = Written().Summary;

        Assert.Contains("Written", summary, StringComparison.Ordinal);
        Assert.Contains("InsertText at byte 20", summary, StringComparison.Ordinal); // where it went
        Assert.Contains("cursor index 20 → 25", summary, StringComparison.Ordinal);
        Assert.Contains("text 15 bytes / 5 code points", summary, StringComparison.Ordinal);
        Assert.Contains("before 20 bytes / 20 code points, cursor 20", summary, StringComparison.Ordinal);
        Assert.Contains("after write 35 bytes / 25 code points, cursor 25", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("; next frame ", summary, StringComparison.Ordinal); // not read yet; the detail still says it is waiting for one
    }

    [Fact]
    public void The_summary_after_the_readback_still_says_where_the_text_went()
    {
        // The next-frame read replaces Detail with its verdict, so "where it was
        // written" has to survive it: both facts belong on the same line.
        var summary = Held().Summary;

        Assert.Contains("InsertText at byte 20", summary, StringComparison.Ordinal);
        Assert.Contains("text is in the Chat Box", summary, StringComparison.Ordinal);
        Assert.Contains("next frame 35 bytes / 25 code points, cursor 25", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("こん\nばんは", "15 bytes / 5 code points (1 control character dropped)")]
    [InlineData("こん\nばん\tは", "15 bytes / 5 code points (2 control characters dropped)")]
    public void The_summary_counts_the_text_as_it_went_in_and_says_what_was_dropped(string committed, string expected)
    {
        var before = State(Draft, Draft.Length);
        var plan = ChatBoxSplice.Plan(before.RawText, before.CursorByteOffset, committed, before.Limits);
        var report = new WriteReport(DateTime.Now, committed, WriteOutcome.Written, "written", before, plan, null, null);

        Assert.Equal(Committed, plan.Committed); // the controls are gone; the kana are not
        Assert.Contains($"text {expected}", report.Summary, StringComparison.Ordinal);
        Assert.Contains("InsertText at byte 20", report.Summary, StringComparison.Ordinal); // measured against what went in
    }

    [Fact]
    public void A_write_that_never_happened_says_nothing_about_where_it_went()
    {
        Assert.Null(WriteReport.Failed(Committed, "ChatLog addon or its text input not found").Where);

        var before = State(new string('a', 495), 495);
        Assert.Null(WriteReport.Refused(Committed, before, ChatBoxSplice.Plan(before.RawText, before.CursorByteOffset, Committed, before.Limits)).Where);
    }

    [Fact]
    public void A_refusal_says_by_how_much_without_the_text()
    {
        var before = State(new string('a', 495), 495);
        var plan = ChatBoxSplice.Plan(before.RawText, before.CursorByteOffset, Committed, before.Limits);
        var report = WriteReport.Refused(Committed, before, plan);

        Assert.True(plan.IsOverflow);
        Assert.DoesNotContain(Committed, report.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(Committed, report.Detail, StringComparison.Ordinal); // the Warning line and the chat line are the Detail
        Assert.Contains("over the limit of 500 bytes", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_report_still_carries_everything_for_debug_and_the_debug_tab()
    {
        var report = Held().ToString();

        Assert.Contains(Committed, report, StringComparison.Ordinal);
        Assert.Contains(Draft, report, StringComparison.Ordinal);
    }
}
