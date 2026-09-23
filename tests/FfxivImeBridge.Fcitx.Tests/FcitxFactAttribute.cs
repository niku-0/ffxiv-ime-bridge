using System.Diagnostics;
using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>A fact that needs a live session bus with fcitx5 on it; skipped otherwise.</summary>
public sealed class FcitxFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> SkipReason = new(Probe);

    public FcitxFactAttribute()
    {
        Skip = SkipReason.Value;
    }

    private static string? Probe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            return "DBUS_SESSION_BUS_ADDRESS is not set.";
        try
        {
            using var connection = FcitxConnection.ConnectAsync().GetAwaiter().GetResult();
            return connection.IsFcitxAvailableAsync().GetAwaiter().GetResult() ? null : "fcitx5 is not on the session bus.";
        }
        catch (Exception ex)
        {
            return $"Session bus unreachable: {ex.Message}";
        }
    }
}
