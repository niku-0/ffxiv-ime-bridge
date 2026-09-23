using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FfxivImeBridge.Capture;
using FfxivImeBridge.NativeWrite;
using FfxivImeBridge.Rendering;
using FfxivImeBridge.Session;

namespace FfxivImeBridge;

/// <summary>
/// On load the plugin climbs the session-bus transport ladder from inside the
/// game (M0.2) and, if it holds, keeps one fcitx5 Input Context tied to the Chat
/// Box's focus (M1.1) with the Indicator showing its input method. While
/// Forwarding, the message pump hook's Gate asks fcitx5 about each key (M1.3)
/// and the Preedit and candidates are drawn at the Cursor (M1.4); a Commit is
/// written into the native Chat Box at the Cursor (M1.5) for the user's own
/// Enter to send. The config file and the settings window (M2.2) hold the
/// Toggle Key, the startup behaviour and the rest; <c>/imebridge debug</c>
/// shows all of it. On Windows proper nothing is hooked and no session is
/// opened (ticket 19): the windows open and say so, and <c>/imebridge probe</c>
/// still climbs the ladder to show at which rung it stops.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/imebridge";
    private const string CommandAlias = "/ime";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly WindowSystem windows = new("FfxivImeBridge");
    private readonly ConfigStore config;
    private readonly ProbeRunner probe;
    private readonly Bridge bridge;
    /// <summary>Null on Windows proper: the message pump is not hooked there.</summary>
    private readonly KeyboardCapture? capture;
    private readonly NativeWriter writer;
    private readonly OverlayFont overlayFont;
    private readonly Indicator indicator;
    private readonly CompositionOverlay overlay;
    private readonly SettingsWindow settingsWindow;
    private readonly DebugWindow debugWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IPluginLog log,
        IChatGui chat,
        IGameInteropProvider interop,
        IGameGui gui,
        IFramework framework,
        IToastGui toast,
        ITextureProvider textures)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;

        config = ConfigStore.Load(pluginInterface, log);
        probe = new ProbeRunner(log, chat);
        writer = new NativeWriter(gui, framework, log, chat);
        bridge = new Bridge(framework, gui, log, chat, toast, probe, writer, config);
        overlayFont = new OverlayFont(pluginInterface.UiBuilder, config, log);
        indicator = new Indicator(bridge, gui, config, overlayFont, textures, Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, "Assets"));
        overlay = new CompositionOverlay(bridge, gui, overlayFont);
        // Wine is the whole point of the plugin; on Windows proper the manifest
        // promises it loads and does nothing, so nothing is hooked and no
        // session is opened (ticket 19). Without the hook the Toggle Key would
        // only be swallowed for a "not reachable" toast, and the ladder would
        // be re-run on every Chat Box focus gain to fail at its first rung.
        var wine = Util.IsWine();
        if (wine) capture = new KeyboardCapture(bridge, interop, gui, framework, log, config, () => overlay.LastPlan);
        settingsWindow = new SettingsWindow(bridge, capture, config);
        debugWindow = new DebugWindow(probe, bridge, capture, writer, overlay, gui, log);
        windows.AddWindow(settingsWindow);
        windows.AddWindow(debugWindow);

        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.Draw += indicator.Draw;
        pluginInterface.UiBuilder.Draw += overlay.Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenSettingsWindow;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;

        var info = new CommandInfo(OnCommand) { HelpMessage = Strings.CommandHelp };
        commands.AddHandler(CommandName, info);
        commands.AddHandler(CommandAlias, new CommandInfo(OnCommand) { ShowInHelp = false });

        if (wine) bridge.Start();
    }

    private void OnCommand(string command, string arguments)
    {
        var (word, rest) = SplitCommand(arguments);

        switch (word, rest.ToLowerInvariant())
        {
            case ("probe", _):
                probe.Start();
                OpenDebugWindow();
                break;
            case ("reconnect", _):
                bridge.Reconnect();
                break;
            case ("toggle", var state):
                bridge.SetForwarding(OnOff(state));
                break;
            case ("indicator", var state):
                config.Current.ShowIndicator = OnOff(state) ?? !config.Current.ShowIndicator;
                config.Save();
                break;
            case ("im", var name):
                bridge.SwitchInputMethodAfter(name.Length > 0 ? name : "mozc", TimeSpan.FromSeconds(3));
                break;
            case ("debug", _):
                OpenDebugWindow();
                break;
            default: // bare, "config", or anything unknown
                OpenSettingsWindow();
                break;
        }
    }

    /// <summary><c>on</c>/<c>off</c> as a bool; anything else (usually nothing) means "flip".</summary>
    private static bool? OnOff(string argument) => argument switch { "on" => true, "off" => false, _ => null };

    /// <summary>The first word lower-cased, and the rest verbatim (an input method name keeps its case).</summary>
    private static (string Word, string Arguments) SplitCommand(string arguments)
    {
        var trimmed = arguments.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0
            ? (trimmed.ToLowerInvariant(), "")
            : (trimmed[..space].ToLowerInvariant(), trimmed[(space + 1)..].Trim());
    }

    private void OpenSettingsWindow() => settingsWindow.IsOpen = true;

    private void OpenDebugWindow() => debugWindow.IsOpen = true;

    /// <summary>
    /// One budget for the whole unload, spent in order: the session's teardown
    /// first, then whatever is left for a climbing ladder. Each used to wait
    /// five seconds of its own, so an unload with fcitx5 hung froze the game
    /// for ten (ticket 19).
    /// </summary>
    public void Dispose()
    {
        var spent = Stopwatch.StartNew();
        commands.RemoveHandler(CommandName);
        commands.RemoveHandler(CommandAlias);
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.Draw -= indicator.Draw;
        pluginInterface.UiBuilder.Draw -= overlay.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenSettingsWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsWindow;
        windows.RemoveAllWindows();
        capture?.Dispose();
        bridge.Dispose(SessionLifecycle.Grace - spent.Elapsed); // before the ladder: a climb it is waiting on is cancelled quietly instead of reporting failure
        probe.Dispose(SessionLifecycle.Grace - spent.Elapsed);
        overlayFont.Dispose();
    }
}
