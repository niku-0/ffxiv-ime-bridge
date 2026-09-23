using FfxivImeBridge.Capture;
using Newtonsoft.Json;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// The config file's shape and the startup rule, over the same Newtonsoft
/// serializer Dalamud writes and reads plugin configs with (ticket 12):
/// <c>SavePluginConfig</c> serializes with <c>TypeNameHandling.Objects</c>,
/// <c>GetPluginConfig</c> deserializes with the defaults, so <c>$type</c> and
/// unknown fields are ignored and missing ones keep their initializers.
/// </summary>
public sealed class ConfigurationTests
{
    private static readonly JsonSerializerSettings DalamudSaves = new()
    {
        TypeNameHandling = TypeNameHandling.Objects,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
    };

    private static string Save(Configuration config) => JsonConvert.SerializeObject(config, Formatting.Indented, DalamudSaves);
    private static Configuration? Load(string json) => JsonConvert.DeserializeObject<Configuration>(json);

    [Fact]
    public void Defaults_are_alt_section_match_chat_box_indicator_on_remember_last_forwarding_off()
    {
        var config = new Configuration();

        Assert.Equal(Configuration.CurrentVersion, config.Version);
        Assert.Equal(ToggleKey.Default, config.ToggleKey);
        Assert.Equal(new ToggleKey(ChordModifiers.Alt, 0x29), config.ToggleKey);
        Assert.Equal(0f, config.FontSizeOverride);
        Assert.True(config.ShowIndicator);
        Assert.Equal(IndicatorStyle.Label, config.IndicatorStyle);
        Assert.Equal(StartupBehaviour.RememberLast, config.Startup);
        Assert.False(config.Forwarding);
    }

    [Fact]
    public void Every_field_survives_a_round_trip_through_dalamuds_serializer()
    {
        var config = new Configuration
        {
            ToggleKey = new ToggleKey(ChordModifiers.Ctrl | ChordModifiers.Shift, 0x24),
            FontSizeOverride = 18.5f,
            ShowIndicator = false,
            IndicatorStyle = IndicatorStyle.Badge,
            Startup = StartupBehaviour.AlwaysOn,
            Forwarding = true,
        };

        var back = Load(Save(config))!;

        Assert.Equal(config.Version, back.Version);
        Assert.Equal(config.ToggleKey, back.ToggleKey);
        Assert.Equal(config.FontSizeOverride, back.FontSizeOverride);
        Assert.Equal(config.ShowIndicator, back.ShowIndicator);
        Assert.Equal(config.IndicatorStyle, back.IndicatorStyle);
        Assert.Equal(config.Startup, back.Startup);
        Assert.Equal(config.Forwarding, back.Forwarding);
    }

    [Fact]
    public void The_file_reads_at_a_glance_enums_by_name_and_nothing_derived()
    {
        var json = Save(new Configuration { ToggleKey = new ToggleKey(ChordModifiers.Ctrl | ChordModifiers.Shift, 0x24), Startup = StartupBehaviour.AlwaysOn });

        Assert.Contains("\"Modifiers\": \"Ctrl, Shift\"", json);
        Assert.Contains("\"Startup\": \"AlwaysOn\"", json);
        Assert.Contains("\"IndicatorStyle\": \"Label\"", json);
        Assert.DoesNotContain("IsBound", json);
        Assert.DoesNotContain("InitialForwarding", json);
    }

    [Fact]
    public void Unknown_and_missing_fields_are_ignored_and_the_rest_keep_their_defaults()
    {
        var back = Load($$"""{"Version": {{Configuration.CurrentVersion}}, "Forwarding": true, "SomethingFromTheFuture": 5}""")!;

        Assert.True(back.Forwarding);
        Assert.Equal(ToggleKey.Default, back.ToggleKey);
        Assert.True(back.ShowIndicator);
        Assert.Equal(IndicatorStyle.Label, back.IndicatorStyle);
        Assert.Equal(StartupBehaviour.RememberLast, back.Startup);
    }

    [Fact]
    public void A_missing_or_older_file_loads_the_defaults()
    {
        Assert.Equal(Save(new Configuration()), Save(Configuration.Upgrade(null)));

        var older = new Configuration { Version = Configuration.CurrentVersion - 1, Forwarding = true, ShowIndicator = false };
        Assert.Equal(Save(new Configuration()), Save(Configuration.Upgrade(older)));

        var current = new Configuration { Forwarding = true };
        Assert.Same(current, Configuration.Upgrade(current));
    }

    [Fact]
    public void Startup_behaviour_decides_the_forwarding_the_session_opens_with()
    {
        static bool Initial(StartupBehaviour startup, bool saved) => new Configuration { Startup = startup, Forwarding = saved }.InitialForwarding;

        Assert.False(Initial(StartupBehaviour.RememberLast, saved: false));
        Assert.True(Initial(StartupBehaviour.RememberLast, saved: true));
        Assert.False(Initial(StartupBehaviour.AlwaysOff, saved: false));
        Assert.False(Initial(StartupBehaviour.AlwaysOff, saved: true));
        Assert.True(Initial(StartupBehaviour.AlwaysOn, saved: false));
        Assert.True(Initial(StartupBehaviour.AlwaysOn, saved: true));
    }
}
