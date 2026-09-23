using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FfxivImeBridge.Capture;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FfxivImeBridge;

/// <summary>What Forwarding starts as when the session opens.</summary>
[JsonConverter(typeof(StringEnumConverter))]
internal enum StartupBehaviour
{
    /// <summary>The value saved at the last flip.</summary>
    RememberLast,
    AlwaysOff,
    AlwaysOn,
}

/// <summary>How the Indicator is drawn (ticket 14).</summary>
[JsonConverter(typeof(StringEnumConverter))]
internal enum IndicatorStyle
{
    /// <summary>The bare glyph in the channel label's colours, its edge as an outline.</summary>
    Label,
    /// <summary>The Windows client's badge as a texture: its own <c>あ</c> badge, or any other glyph in dark gold on the same badge with the <c>あ</c> painted out.</summary>
    Badge,
}

/// <summary>
/// The config file (v1, spec "Codebase shape"): the Toggle Key, the font size
/// (0 = match the Chat Box; consumed by ticket 14), the Indicator, the startup
/// behaviour and the last Forwarding value, which is written on every flip.
/// A plain object: Dalamud serializes the public properties and reads them back
/// ignoring anything it does not know, so a missing field keeps its initializer.
/// Enums are written by name so the file reads at a glance.
/// </summary>
internal sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public ToggleKey ToggleKey { get; set; } = ToggleKey.Default;

    /// <summary>Preedit font size in pixels; <c>0</c> matches the Chat Box (ticket 14 draws with it).</summary>
    public float FontSizeOverride { get; set; }

    public bool ShowIndicator { get; set; } = true;

    public IndicatorStyle IndicatorStyle { get; set; } = IndicatorStyle.Label;

    public StartupBehaviour Startup { get; set; } = StartupBehaviour.RememberLast;

    /// <summary>The value at the last flip; what <see cref="StartupBehaviour.RememberLast"/> restores.</summary>
    public bool Forwarding { get; set; }

    /// <summary>The Forwarding the session opens with, from <see cref="Startup"/> and the saved value.</summary>
    [JsonIgnore]
    public bool InitialForwarding => Startup switch
    {
        StartupBehaviour.AlwaysOn => true,
        StartupBehaviour.AlwaysOff => false,
        _ => Forwarding,
    };

    /// <summary>What a loaded file becomes: a missing or older one is the defaults; the current version is taken as it is.</summary>
    public static Configuration Upgrade(Configuration? loaded) =>
        loaded is { Version: >= CurrentVersion } ? loaded : new Configuration();
}

/// <summary>
/// The loaded <see cref="Configuration"/> and the way back to disk. Everything
/// that changes a setting writes the object and calls <see cref="Save"/>; a
/// failed save is logged and never breaks the change. Main thread.
/// </summary>
internal sealed class ConfigStore
{
    private readonly Action<Configuration> save;
    private readonly IPluginLog log;

    public ConfigStore(Configuration current, Action<Configuration> save, IPluginLog log)
    {
        Current = current;
        this.save = save;
        this.log = log;
    }

    public Configuration Current { get; }

    /// <summary>Dalamud's <c>pluginConfigs/FfxivImeBridge.json</c>; unreadable counts as missing.</summary>
    public static ConfigStore Load(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        Configuration? loaded = null;
        try
        {
            loaded = pluginInterface.GetPluginConfig() as Configuration;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Config: the file could not be read; using the defaults");
        }
        var current = Configuration.Upgrade(loaded);
        if (loaded is not null && !ReferenceEquals(current, loaded)) log.Information("Config: version {Version} file replaced by the defaults", loaded.Version);
        return new ConfigStore(current, pluginInterface.SavePluginConfig, log);
    }

    public void Save()
    {
        try
        {
            save(Current);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Config: saving failed");
        }
    }
}
