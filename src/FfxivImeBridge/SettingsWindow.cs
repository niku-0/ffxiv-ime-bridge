using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FfxivImeBridge.Capture;

namespace FfxivImeBridge;

/// <summary>
/// The settings window (ticket 12): the cog on Dalamud's plugin page, bare
/// <c>/imebridge</c> and <c>/imebridge config</c>. Every control writes the
/// <see cref="Configuration"/> and saves at once; Forwarding goes through the
/// <see cref="Bridge"/> like the chord and the command do. On Windows proper
/// (not Wine) nothing is hooked, so there is no capture and the window only
/// explains that the plugin does nothing there. Main thread.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const float FontSizeMin = 8f;
    private const float FontSizeMax = 48f;
    /// <summary>What the slider starts at when "match" is unchecked: about the chat box's own size.</summary>
    private const float FontSizeFirstOverride = 16f;
    /// <summary>Where the help text wraps, in font heights: the window auto-resizes, so it needs a fixed width.</summary>
    private const float WrapWidthInFontHeights = 28f;

    private readonly Bridge bridge;
    private readonly KeyboardCapture? capture;
    private readonly ConfigStore config;

    /// <param name="capture">The message-pump hook, or null on Windows proper where nothing is hooked (ticket 19): the window then has nothing to set.</param>
    public SettingsWindow(Bridge bridge, KeyboardCapture? capture, ConfigStore config) : base(Strings.SettingsTitle + "###FfxivImeBridgeSettings")
    {
        this.bridge = bridge;
        this.capture = capture;
        this.config = config;
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    private Configuration Current => config.Current;

    public override void OnClose()
    {
        if (capture is { CapturingToggleKey: true }) capture.CancelToggleKeyCapture();
    }

    public override void Draw()
    {
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * WrapWidthInFontHeights);
        if (capture is { } hook) DrawSettings(hook);
        else ImGui.TextUnformatted(Strings.WindowsOnly);
        ImGui.PopTextWrapPos();
    }

    private void DrawSettings(KeyboardCapture hook)
    {
        DrawForwarding();
        ImGui.Separator();
        DrawToggleKey(hook);
        ImGui.Separator();
        DrawFontSize();
        ImGui.Separator();
        DrawIndicator();
        ImGui.Separator();
        DrawStartup();
    }

    private void DrawForwarding()
    {
        var session = bridge.Session;
        var forwarding = session?.Forwarding ?? false;
        using (ImRaii.Disabled(session is null))
        {
            if (ImGui.Checkbox(Strings.ForwardingLabel, ref forwarding)) bridge.SetForwarding(forwarding);
        }
        var note = session switch
        {
            null when bridge.Inert => Strings.ForwardingInert,
            null => Strings.ForwardingConnecting,
            { ConnectionLost: true } => Strings.ForwardingConnectionLost,
            _ => null,
        };
        if (note is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(note);
        }
        ImGui.TextDisabled(Strings.ForwardingHelp);

        using (ImRaii.Disabled(bridge.Reconnecting))
        {
            if (ImGui.Button(bridge.Reconnecting ? Strings.Reconnecting : Strings.Reconnect)) bridge.Reconnect();
        }
        ImGui.TextDisabled(Strings.ReconnectHelp);
    }

    private void DrawToggleKey(KeyboardCapture hook)
    {
        ImGui.TextUnformatted($"{Strings.ToggleKeyHeading}: {Current.ToggleKey.Describe(Win32KeyNames.NameFor)}");
        ImGui.TextDisabled(Strings.ToggleKeyHelp);

        var modifiers = Current.ToggleKey.Modifiers;
        var changed = false;
        changed |= ModifierCheckbox("Ctrl", ChordModifiers.Ctrl, ref modifiers);
        ImGui.SameLine();
        changed |= ModifierCheckbox("Alt", ChordModifiers.Alt, ref modifiers);
        ImGui.SameLine();
        changed |= ModifierCheckbox("Shift", ChordModifiers.Shift, ref modifiers);
        ImGui.SameLine();
        if (ImGui.Button(hook.CapturingToggleKey ? Strings.PressingAKey : Strings.PressAKey))
        {
            if (hook.CapturingToggleKey) hook.CancelToggleKeyCapture();
            else hook.BeginToggleKeyCapture(OnToggleKeyCaptured);
        }
        ImGui.TextDisabled(Strings.ToggleKeyNeedsModifier);

        // Unchecking the last modifier is refused: a bare key can never be the chord (ADR-0004).
        if (changed && modifiers != ChordModifiers.None) SetToggleKey(Current.ToggleKey with { Modifiers = modifiers });
    }

    private static bool ModifierCheckbox(string label, ChordModifiers flag, ref ChordModifiers modifiers)
    {
        var on = modifiers.HasFlag(flag);
        if (!ImGui.Checkbox(label, ref on)) return false;
        modifiers = on ? modifiers | flag : modifiers & ~flag;
        return true;
    }

    /// <summary>
    /// The pressed key's scancode with the modifiers held at it, else the
    /// checkboxes', else the default's — the chord never leaves here unbound
    /// (ADR-0004). Escape (null) leaves the chord alone.
    /// </summary>
    private void OnToggleKeyCaptured(ToggleKey? pressed)
    {
        if (pressed is not { } key) return;
        var modifiers = key.Modifiers != ChordModifiers.None ? key.Modifiers
            : Current.ToggleKey.Modifiers != ChordModifiers.None ? Current.ToggleKey.Modifiers
            : ToggleKey.Default.Modifiers;
        SetToggleKey(new ToggleKey(modifiers, key.ScanCode));
    }

    private void SetToggleKey(ToggleKey key)
    {
        Current.ToggleKey = key;
        config.Save();
    }

    private void DrawFontSize()
    {
        ImGui.TextUnformatted(Strings.FontSizeHeading);
        ImGui.TextDisabled(Strings.FontSizeHelp);
        var match = Current.FontSizeOverride == 0f;
        if (ImGui.Checkbox(Strings.FontSizeMatch, ref match))
        {
            Current.FontSizeOverride = match ? 0f : FontSizeFirstOverride;
            config.Save();
        }
        if (match) return;

        var size = Current.FontSizeOverride;
        if (ImGui.SliderFloat(Strings.FontSizeSlider, ref size, FontSizeMin, FontSizeMax, "%.0f")) Current.FontSizeOverride = size;
        if (ImGui.IsItemDeactivatedAfterEdit()) config.Save(); // once per drag, not once per frame
    }

    private void DrawIndicator()
    {
        var show = Current.ShowIndicator;
        if (ImGui.Checkbox(Strings.ShowIndicator, ref show))
        {
            Current.ShowIndicator = show;
            config.Save();
        }
        ImGui.TextDisabled(Strings.ShowIndicatorHelp);
        using (ImRaii.Disabled(!show))
        {
            IndicatorStyleRadio(Strings.IndicatorStyleLabel, IndicatorStyle.Label);
            ImGui.SameLine();
            IndicatorStyleRadio(Strings.IndicatorStyleBadge, IndicatorStyle.Badge);
        }
    }

    private void IndicatorStyleRadio(string label, IndicatorStyle value)
    {
        if (!ImGui.RadioButton(label, Current.IndicatorStyle == value) || Current.IndicatorStyle == value) return;
        Current.IndicatorStyle = value;
        config.Save();
    }

    private void DrawStartup()
    {
        ImGui.TextUnformatted(Strings.StartupHeading);
        StartupRadio(Strings.StartupRememberLast, StartupBehaviour.RememberLast);
        ImGui.SameLine();
        StartupRadio(Strings.StartupAlwaysOff, StartupBehaviour.AlwaysOff);
        ImGui.SameLine();
        StartupRadio(Strings.StartupAlwaysOn, StartupBehaviour.AlwaysOn);
    }

    private void StartupRadio(string label, StartupBehaviour value)
    {
        if (!ImGui.RadioButton(label, Current.Startup == value) || Current.Startup == value) return;
        Current.Startup = value;
        config.Save();
    }
}
