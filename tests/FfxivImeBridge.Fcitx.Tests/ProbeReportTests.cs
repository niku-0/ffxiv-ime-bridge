using System.Collections.Immutable;
using FfxivImeBridge.Fcitx.Diagnostics;
using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>The ladder as the debug window's <b>Copy ladder</b> hands it out, for pasting into a public issue (ticket 25).</summary>
public sealed class ProbeReportTests
{
    private const string Home = "/home/someone";

    private static ProbeReport Report(params ProbeStep[] steps) => new(steps.ToImmutableArray());

    private static readonly ProbeReport StoppedAtConnect = Report(
        new ProbeStep("platform", ProbeOutcome.Passed, "Wine 10.8 (WINEPREFIX=/home/someone/.xlcore/wineprefix)"),
        new ProbeStep("address", ProbeOutcome.Passed, @"path=/run/user/1000/bus wine-path=Z:\run\user\1000\bus uid=1000"),
        new ProbeStep("connect", ProbeOutcome.Failed, "SocketException: connection refused"),
        new ProbeStep("authenticate", ProbeOutcome.Skipped, "not attempted"));

    [Fact]
    public void Carries_the_summary_and_every_step_with_its_outcome_and_detail()
    {
        var text = StoppedAtConnect.ToShareableText(Home);

        Assert.Contains("Ladder stopped at: connect", text);
        Assert.Contains("[Passed ] platform: Wine 10.8", text);
        Assert.Contains("[Failed ] connect: SocketException: connection refused", text);
        Assert.Contains("[Skipped] authenticate: not attempted", text);
    }

    [Fact]
    public void Shortens_the_home_directory_to_a_tilde()
    {
        var text = StoppedAtConnect.ToShareableText(Home);

        Assert.Contains("WINEPREFIX=~/.xlcore/wineprefix", text);
        Assert.DoesNotContain(Home, text);
    }

    [Fact]
    public void Shortens_the_home_directory_in_its_wine_spelling_too()
    {
        var text = Report(new ProbeStep("address", ProbeOutcome.Passed, @"path=/home/someone/bus wine-path=Z:\home\someone\bus")).ToShareableText(Home);

        Assert.Equal(@"[Passed ] address: path=~/bus wine-path=~\bus" + Environment.NewLine + "Session bus reachable directly; fcitx5 composes.", text);
    }

    [Fact]
    public void Leaves_a_longer_name_that_merely_starts_with_the_home_alone()
    {
        var text = Report(new ProbeStep("platform", ProbeOutcome.Passed, "WINEPREFIX=/home/someoneelse/pfx; backup=/home/someone.old")).ToShareableText(Home);

        Assert.Contains("WINEPREFIX=/home/someoneelse/pfx; backup=/home/someone.old", text);
    }

    [Fact]
    public void Leaves_a_path_that_merely_contains_the_home_further_in_alone()
    {
        var text = Report(new ProbeStep("platform", ProbeOutcome.Passed, "WINEPREFIX=/mnt/old/home/someone/pfx")).ToShareableText(Home);

        Assert.Contains("WINEPREFIX=/mnt/old/home/someone/pfx", text);
    }

    [Theory]
    [InlineData(@"z:\home\someone\pfx")]
    [InlineData("Z:/home/someone/pfx")]
    public void Shortens_the_wine_spelling_whatever_the_drive_letters_case_or_the_slashes(string path)
    {
        var text = Report(new ProbeStep("crash", ProbeOutcome.Failed, $"FileNotFoundException: '{path}'")).ToShareableText(Home);

        Assert.DoesNotContain("someone", text);
    }

    [Fact]
    public void Shortens_the_home_directory_at_the_end_of_a_detail_and_given_with_a_trailing_slash()
    {
        var text = Report(new ProbeStep("platform", ProbeOutcome.Passed, "WINEPREFIX=/home/someone")).ToShareableText(Home + "/");

        Assert.Contains("WINEPREFIX=~", text);
        Assert.DoesNotContain(Home, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public void Without_a_usable_home_directory_is_the_plain_report(string? home)
    {
        Assert.Equal(StoppedAtConnect.ToString(), StoppedAtConnect.ToShareableText(home));
    }

    [Theory]
    [InlineData(@"\??\Z:\home\someone", "/home/someone")]
    [InlineData(@"\??\Z:\var\home\someone\", "/var/home/someone")]
    [InlineData(@"\??\unix\home\someone", "/home/someone")]
    [InlineData(@"\??\unix/home/someone", "/home/someone")]
    public void Reads_the_home_directory_from_wines_WINEHOMEDIR(string wineHomeDir, string expected)
    {
        Assert.Equal(expected, ProbeReport.HomeFromWineHomeDir(wineHomeDir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\??\C:\users\someone")]
    [InlineData("/home/someone")]
    public void Reads_no_home_directory_from_anything_else(string? wineHomeDir)
    {
        Assert.Null(ProbeReport.HomeFromWineHomeDir(wineHomeDir));
    }
}
