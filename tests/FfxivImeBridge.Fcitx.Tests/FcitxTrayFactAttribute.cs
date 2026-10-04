using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>A fact that needs fcitx5's tray item with a named icon, on top of what <see cref="FcitxFactAttribute"/> needs; skipped otherwise.</summary>
public sealed class FcitxTrayFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> SkipReason = new(Probe);

    public FcitxTrayFactAttribute()
    {
        Skip = new FcitxFactAttribute().Skip ?? SkipReason.Value;
    }

    private static string? Probe()
    {
        using var connection = FcitxConnection.ConnectAsync().GetAwaiter().GetResult();
        return connection.GetTrayIconNameAsync().GetAwaiter().GetResult() is null
            ? "fcitx5 has no tray item with a named icon (no tray on the desktop, or PreferTextIcon)."
            : null;
    }
}
