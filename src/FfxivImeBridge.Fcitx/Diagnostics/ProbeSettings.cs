namespace FfxivImeBridge.Fcitx.Diagnostics;

/// <summary>Knobs for <see cref="TransportProbe"/>; the defaults are what the plugin uses on load.</summary>
public sealed class ProbeSettings
{
    /// <summary>Bus address; null reads <c>DBUS_SESSION_BUS_ADDRESS</c> from the process environment.</summary>
    public string? Address { get; init; }

    /// <summary>Linux uid for <c>AUTH EXTERNAL</c>; null derives it from a <c>/run/user/&lt;uid&gt;/</c> path.</summary>
    public uint? UnixUserId { get; init; }

    /// <summary>Client name fcitx5 shows for the probe's input context.</summary>
    public string Program { get; init; } = "ffxiv-ime-bridge";

    /// <summary>Input method to compose the test key with; Mozc turns <c>k</c> into a visible preedit.</summary>
    public string InputMethod { get; init; } = "mozc";

    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(3);
}
