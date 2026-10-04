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
    private static readonly ChatBoxLimits FiveHundredBytes = new(0, 500);

    private static SplicePlan Plan(string draft, int? cursorByteOffset, string committed) =>
        ChatBoxSplice.Plan(Encoding.UTF8.GetBytes(draft), cursorByteOffset, committed, FiveHundredBytes);

    private static WriteReport Written(string committed = Committed) =>
        WriteReport.Written(committed, Plan(Draft, Draft.Length, committed));

    [Fact]
    public void The_summary_of_a_write_names_neither_the_committed_text_nor_the_chat_box()
    {
        var summary = Written().Summary;

        Assert.DoesNotContain(Committed, summary, StringComparison.Ordinal);
        Assert.DoesNotContain(Draft, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_carries_the_outcome_where_the_text_went_and_its_counts_instead()
    {
        var summary = Written().Summary;

        Assert.Equal("Written: InsertText at byte 20; text 15 bytes / 5 code points", summary);
    }

    [Fact]
    public void A_write_with_an_unusable_cursor_says_it_was_appended()
    {
        var report = WriteReport.Written(Committed, Plan(Draft, cursorByteOffset: null, Committed));

        Assert.Equal("appended at the end with SetText (cursor unusable)", report.Detail);
    }

    [Theory]
    [InlineData("こん\nばんは", "15 bytes / 5 code points (1 control character dropped)")]
    [InlineData("こん\nばん\tは", "15 bytes / 5 code points (2 control characters dropped)")]
    public void The_summary_counts_the_text_as_it_went_in_and_says_what_was_dropped(string committed, string expected)
    {
        var report = Written(committed);

        Assert.Equal(Committed, report.Committed); // the controls are gone; the kana are not
        Assert.Contains($"text {expected}", report.Summary, StringComparison.Ordinal);
        Assert.Contains("InsertText at byte 20", report.Summary, StringComparison.Ordinal); // measured against what went in
    }

    [Fact]
    public void A_refusal_says_by_how_much_without_the_text()
    {
        var plan = Plan(new string('a', 495), 495, Committed);
        var report = WriteReport.Refused(Committed, plan, FiveHundredBytes);

        Assert.True(plan.IsOverflow);
        Assert.DoesNotContain(Committed, report.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(Committed, report.Detail, StringComparison.Ordinal); // the Warning line and the chat line are the Detail
        Assert.Contains("over the limit of 500 bytes", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_report_carries_the_text_for_debug_and_the_debug_tab()
    {
        var report = Written().ToString();

        Assert.Contains($"\"{Committed}\" Written: InsertText at byte 20", report, StringComparison.Ordinal);
    }
}
