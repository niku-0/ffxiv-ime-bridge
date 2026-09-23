namespace FfxivImeBridge;

/// <summary>Every line the user reads — chat, toasts, the command help and the settings window — in one place (spec "Codebase shape"). English only in v1.</summary>
internal static class Strings
{
    private const string Prefix = "IME Bridge: ";

    // Commands
    public const string CommandHelp = "Opens the settings window.\n"
        + "/imebridge toggle [on|off] – turn Forwarding on or off (same as the Toggle Key, Alt+§ by default)\n"
        + "/imebridge indicator [on|off] – show or hide the Indicator\n"
        + "/imebridge reconnect – reconnect to fcitx5 from scratch\n"
        + "/imebridge probe – test the connection to fcitx5 without changing anything\n"
        + "/imebridge debug – open the debug window\n"
        + "/imebridge im [name] – debug: switch the chat box's input method after 3 s (default: mozc)";

    // Chat lines and toasts
    public static string ForwardingFlipped(bool on) => Prefix + (on ? "forwarding on" : "forwarding off");
    public const string NotReachable = Prefix + "fcitx5 not reachable, disabled";
    public static string Reachable(bool forwarding) => Prefix + $"fcitx5 reachable, forwarding {(forwarding ? "on" : "off")}";
    public const string StillConnecting = Prefix + "still connecting to fcitx5";
    public const string DegradedBusLost = Prefix + "fcitx5 left the bus, forwarding degraded";
    public const string DegradedNotAnswering = Prefix + "fcitx5 is not answering, forwarding degraded";
    public const string DegradedConnectionLost = Prefix + "lost the connection to fcitx5, forwarding degraded";
    public static string Overflow(string detail) => Prefix + "the committed text does not fit: " + detail;
    public static string ProbeResult(string summary) => Prefix + summary;
    public const string NoLiveConnection = Prefix + "no live connection";
    public static string SwitchingInputMethod(string uniqueName, TimeSpan delay) => Prefix + $"switching the focused context to {uniqueName} in {delay.TotalSeconds:0}s — click into the Chat Box";
    public const string NothingSwitched = Prefix + "the Chat Box was not focused; nothing switched";

    // Settings window
    public const string SettingsTitle = "IME Bridge settings";
    public const string WindowsOnly = "This plugin only works on Linux, where it connects to the fcitx5 input method. On Windows it stays loaded but does nothing.";
    public const string ForwardingLabel = "Forwarding";
    public const string ForwardingHelp = "While on, your typing in the chat box goes to fcitx5, so you can compose Japanese there.";
    public const string ForwardingInert = "(can't reach fcitx5 — check it is running, then press Reconnect)";
    public const string ForwardingConnecting = "(connecting to fcitx5…)";
    public const string ForwardingConnectionLost = "(lost the connection to fcitx5 — press Reconnect)";
    public const string Reconnect = "Reconnect";
    public const string Reconnecting = "Reconnecting…";
    public const string ReconnectHelp = "Connects to fcitx5 again from scratch.\nThis also happens by itself when you click into the chat box while disconnected.";
    public const string ToggleKeyHeading = "Toggle Key";
    public const string ToggleKeyHelp = "Turns Forwarding on or off while the chat box is focused.\nThe key is recognised by where it sits on the keyboard, not by the character it types.";
    public const string ToggleKeyUnbound = "unbound";
    public const string ToggleKeyNeedsModifier = "The shortcut needs at least one of Ctrl, Alt or Shift.";
    public const string PressAKey = "Press a key";
    public const string PressingAKey = "Press a key… (Escape cancels)";
    public const string FontSizeHeading = "Text size";
    public const string FontSizeHelp = "Size of the text you are composing and the candidate list. \n\"Match chat box\" follows the chat box's font size.";
    public const string FontSizeMatch = "Match chat box";
    public const string FontSizeSlider = "px";
    public const string ShowIndicator = "Show Indicator";
    public const string ShowIndicatorHelp = "Shows a small symbol for the active input method before the channel name while Forwarding is on.";
    public const string IndicatorStyleLabel = "Plain symbol";
    public const string IndicatorStyleBadge = "Badge (like on Windows)";
    public const string StartupHeading = "When the plugin loads, Forwarding is:";
    public const string StartupRememberLast = "as it was last time";
    public const string StartupAlwaysOff = "always off";
    public const string StartupAlwaysOn = "always on";

    // Debug window
    public const string DebugTitle = "IME Bridge debug";
    public const string DebugForwardingLabel = "Forwarding (the Toggle Key in the Chat Box flips it too)";
}
