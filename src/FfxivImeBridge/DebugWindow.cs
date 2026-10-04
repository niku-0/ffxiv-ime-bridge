using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx.Diagnostics;
using FfxivImeBridge.NativeWrite;
using FfxivImeBridge.Rendering;

namespace FfxivImeBridge;

/// <summary>The <c>/imebridge debug</c> window: the transport ladder (M0.2), the keyboard Gate (M0.3, M1.3) with the composition snapshot (M1.4), and the Chat Box with the last Native Write (M0.4, M1.5) and the Preedit's placement (M2.5).</summary>
internal sealed class DebugWindow : Window
{
    private static readonly Vector4 Passed = new(0.55f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 Failed = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Skipped = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Swallowed = new(1f, 0.8f, 0.4f, 1f);

    private readonly ProbeRunner probe;
    private readonly Bridge bridge;
    /// <summary>Null on Windows proper, where nothing is hooked (ticket 19); the Keyboard tab then says so and the Transport tab carries the answer.</summary>
    private readonly KeyboardCapture? capture;
    private readonly NativeWriter writer;
    private readonly CompositionOverlay overlay;
    private bool showCursorMarkers;

    public DebugWindow(ProbeRunner probe, Bridge bridge, KeyboardCapture? capture, NativeWriter writer, CompositionOverlay overlay) : base(Strings.DebugTitle + "###FfxivImeBridgeDebug")
    {
        this.probe = probe;
        this.bridge = bridge;
        this.capture = capture;
        this.writer = writer;
        this.overlay = overlay;
        Size = new Vector2(720, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs) return;

        using (var tab = ImRaii.TabItem("Transport"))
        {
            if (tab) DrawTransport();
        }

        using (var tab = ImRaii.TabItem("Keyboard"))
        {
            if (tab) DrawKeyboard();
        }

        using (var tab = ImRaii.TabItem("Chat Box"))
        {
            if (tab) DrawChatBox();
        }
    }

    private void DrawTransport()
    {
        if (ImGui.Button(probe.IsRunning ? "Running…" : "Run the transport ladder again")) probe.Start();
        ImGui.SameLine();
        // From the steps, not the report: a crashed climb has a "crash" step but no report. Never mid-climb, whose steps would read as a pass.
        using (ImRaii.Disabled(probe.IsRunning || probe.Steps.IsEmpty))
        {
            if (ImGui.Button("Copy ladder")) ImGui.SetClipboardText(new ProbeReport(probe.Steps).ToShareableText(WineHomeDirectory()));
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(probe.Report?.Summary ?? (probe.IsRunning ? "Climbing…" : "Not run yet."));
        ImGui.Separator();

        ImGui.PushTextWrapPos();
        foreach (var step in probe.Steps)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, step.Outcome switch
            {
                ProbeOutcome.Passed => Passed,
                ProbeOutcome.Failed => Failed,
                _ => Skipped,
            });
            ImGui.TextUnformatted($"[{step.Outcome}] {step.Name}");
            ImGui.PopStyleColor();
            ImGui.Indent();
            ImGui.TextUnformatted(step.Detail);
            ImGui.Unindent();
        }
        ImGui.PopTextWrapPos();
    }

    /// <summary>What <b>Copy ladder</b> shortens to <c>~</c>: the game does not see <c>HOME</c> under Wine, which passes the home on as <c>WINEHOMEDIR</c>.</summary>
    private static string? WineHomeDirectory() => ProbeReport.HomeFromWineHomeDir(Environment.GetEnvironmentVariable("WINEHOMEDIR"));

    private void DrawKeyboard()
    {
        if (capture is not { } hook)
        {
            ImGui.TextWrapped(Strings.WindowsOnly);
            ImGui.TextDisabled("The Transport tab's ladder still runs on demand and says at which rung it stops.");
            return;
        }

        var session = bridge.Session;
        var forwarding = session?.Forwarding ?? false;
        using (ImRaii.Disabled(session is null))
        {
            if (ImGui.Checkbox(Strings.DebugForwardingLabel, ref forwarding)) bridge.SetForwarding(forwarding);
        }
        ImGui.TextUnformatted(SessionSummary(session));
        ImGui.PushTextWrapPos();
        ImGui.TextUnformatted(CompositionSummary(session));
        ImGui.PopTextWrapPos();

        var focus = hook.ReadFocus();
        ImGui.PushStyleColor(ImGuiCol.Text, focus.ChatBoxFocused ? Passed : Skipped);
        ImGui.TextUnformatted($"Focus: {focus}");
        ImGui.PopStyleColor();

        ImGui.TextUnformatted($"Messages seen: {hook.Seen}, swallowed: {hook.Swallowed}, asked: {hook.Asked} (wait max {hook.MaxWaitMs:0.0} ms, mean {hook.MeanWaitMs:0.0} ms)");
        ImGui.SameLine();
        if (ImGui.Button("Copy trace")) ImGui.SetClipboardText(CaptureTraceText(hook, focus));
        ImGui.SameLine();
        if (ImGui.Button("Clear trace")) hook.ClearTrace();
        ImGui.TextDisabled("Click the chat box (not this window) and type; the trace fills newest-last: verdict, class, rule, wait, message. Only messages the gate acted on are traced; clicks and wheel over the overlay are traced too.");
        ImGui.Separator();

        using var child = ImRaii.Child("##trace", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        if (!child) return;
        foreach (var entry in hook.Trace)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, entry.Decision switch
            {
                { Rule: GateRule.Toggle } => Passed,
                { Verdict: GateVerdict.Pass } => Skipped,
                _ => Swallowed,
            });
            ImGui.TextUnformatted(entry.ToString());
            ImGui.PopStyleColor();
        }
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1) ImGui.SetScrollHereY(1f);
    }

    private void DrawChatBox()
    {
        var state = writer.Read();

        var readout = state?.ToString() ?? (writer.ReadError != null ? $"Read failed: {writer.ReadError}" : "ChatLog addon or its text input not found.");
        ImGui.PushStyleColor(ImGuiCol.Text, state == null ? Failed : state.IsModuleTarget ? Passed : Skipped);
        ImGui.PushTextWrapPos();
        ImGui.TextUnformatted(readout);
        ImGui.PopStyleColor();
        ImGui.TextUnformatted(PreeditPlacementText());
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Copy readout")) ImGui.SetClipboardText(ChatBoxReadoutText(readout));
        ImGui.SameLine();
        ImGui.Checkbox("Show cursor markers", ref showCursorMarkers);
        ImGui.TextDisabled("Readout and the last Native Write as text (ImGui text cannot be selected). Red line: the input's cursor node; blue: text node + measured width.");
        if (showCursorMarkers && state != null) DrawCursorMarkers(state);
        ImGui.Separator();

        var report = writer.LastReport;
        if (report == null)
        {
            ImGui.TextDisabled("No Native Write yet: compose in the Chat Box and commit.");
            return;
        }
        if (ImGui.Button("Copy report")) ImGui.SetClipboardText(report.ToString());
        ImGui.SameLine();
        ImGui.TextUnformatted("Last Native Write:");
        using var child = ImRaii.Child("##report", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        if (child) ImGui.TextUnformatted(report.ToString());
    }

    /// <summary>Draws the two derived cursor positions over the game so they can be compared with the real caret.</summary>
    private static void DrawCursorMarkers(ChatBoxState state)
    {
        var draw = ImGui.GetForegroundDrawList();
        if (state.CursorNode is { } cursor)
        {
            var x = cursor.X;
            draw.AddLine(new Vector2(x, cursor.Y - 4), new Vector2(x, cursor.Y + Math.Max(cursor.Height, 4) + 4), ImGui.GetColorU32(Failed), 2f);
        }
        if (state.MeasuredCursorX is { } measuredX && state.TextNode is { } text)
        {
            draw.AddLine(new Vector2(measuredX, text.Y), new Vector2(measuredX, text.Y + Math.Max(text.Height, 4)), ImGui.GetColorU32(new Vector4(0.4f, 0.6f, 1f, 1f)), 2f);
        }
    }

    /// <summary>Where the overlay put the Preedit this frame against the text and cursor nodes (ticket 15), so a remaining offset can be read off.</summary>
    private string PreeditPlacementText() =>
        "preedit: " + (overlay.LastPreedit?.ToString() ?? "not drawn (compose in the Chat Box to place it)");

    /// <summary>The Chat Box tab as plain text: the live readout and the Preedit's placement, then the last Native Write's report, ready to paste into a ticket.</summary>
    private string ChatBoxReadoutText(string readout)
    {
        var sb = new StringBuilder();
        sb.AppendLine(readout);
        sb.AppendLine(PreeditPlacementText());
        if (writer.LastReport is { } report)
        {
            sb.AppendLine("last native write:");
            sb.Append(report);
        }
        return sb.ToString();
    }

    private string SessionSummary(Session.ForwardingSession? session) => session is null
        ? (bridge.Inert ? "Session: inert (fcitx5 not reachable)" : "Session: connecting…")
        : $"Session: forwarding={(session.Forwarding ? "on" : "off")} degraded={session.Degraded} gate={(session.GateActive ? "active" : "idle")} input method={session.CurrentInputMethod?.UniqueName ?? "-"} glyph={session.IndicatorGlyph ?? "-"}";

    /// <summary>The snapshot the overlay draws: what fcitx5 last sent, so a wrong drawing can be told from a wrong snapshot.</summary>
    private static string CompositionSummary(Session.ForwardingSession? session)
    {
        if (session is null) return "Composition: -";
        var c = session.Composition;
        var segments = string.Join(" | ", c.Preedit.Segments.Select(s => $"\"{s.Text}\" {s.Format}"));
        var candidates = string.Join(" | ", c.Candidates.Select(k => k.Label + k.Text));
        return $"Composition: preedit [{segments}] cursor byte={c.Preedit.CursorByteOffset} char={c.Preedit.CursorIndex}; candidates [{candidates}] selected={c.SelectedCandidate} layout={c.Layout} prev={c.HasPreviousPage} next={c.HasNextPage}; aux up=\"{string.Concat(c.AuxUp.Select(s => s.Text))}\" down=\"{string.Concat(c.AuxDown.Select(s => s.Text))}\"";
    }

    /// <summary>The trace as plain text, headed by the current state, ready to paste into a ticket.</summary>
    private string CaptureTraceText(KeyboardCapture capture, FocusSnapshot focus)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{SessionSummary(bridge.Session)} seen={capture.Seen} swallowed={capture.Swallowed} asked={capture.Asked} wait max={capture.MaxWaitMs:0.0}ms mean={capture.MeanWaitMs:0.0}ms");
        sb.AppendLine(CompositionSummary(bridge.Session));
        sb.AppendLine($"focus: {focus}");
        foreach (var entry in capture.Trace) sb.AppendLine(entry.ToString());
        return sb.ToString();
    }
}
