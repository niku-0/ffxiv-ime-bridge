using System.Collections.Immutable;
using System.Text;

namespace FfxivImeBridge.Fcitx.Diagnostics;

/// <summary>What happened to one rung of the ladder.</summary>
public enum ProbeOutcome
{
    Passed,
    Failed,
    /// <summary>Not attempted because an earlier rung failed.</summary>
    Skipped,
}

/// <summary>One rung of the transport ladder: what was tried and what came back (a value, or the exception).</summary>
public sealed record ProbeStep(string Name, ProbeOutcome Outcome, string Detail)
{
    public override string ToString() => $"[{Outcome,-7}] {Name}: {Detail}";
}

/// <summary>The outcome of <see cref="TransportProbe.RunAsync"/>, in the order the rungs were tried.</summary>
/// <param name="ConnectionOptions">The transport settings the "authenticate" rung passed with, for opening a live connection the same way; null if it never did.</param>
public sealed record ProbeReport(ImmutableArray<ProbeStep> Steps, FcitxConnectionOptions? ConnectionOptions = null)
{
    public bool Succeeded => Steps.All(s => s.Outcome == ProbeOutcome.Passed);

    /// <summary>The preedit fcitx5 sent back for the test key, when the ladder reached the top.</summary>
    public string? PreeditText => Steps.SingleOrDefault(s => s is { Name: TransportProbe.PreeditRung, Outcome: ProbeOutcome.Passed })?.Detail;

    /// <summary>One line for chat and logs: the verdict, or the rung that failed.</summary>
    public string Summary => Succeeded
        ? "Session bus reachable directly; fcitx5 composes."
        : "Ladder stopped at: " + Steps.First(s => s.Outcome == ProbeOutcome.Failed).Name;

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var step in Steps) text.AppendLine(step.ToString());
        text.Append(Summary);
        return text.ToString();
    }
}
