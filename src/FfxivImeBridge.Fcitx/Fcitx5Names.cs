namespace FfxivImeBridge.Fcitx;

/// <summary>
/// Bus names, object paths and interface names of fcitx5's D-Bus frontend.
/// Verified by live introspection against fcitx5 5.1.22 (see README.md).
/// </summary>
public static class Fcitx5Names
{
    public const string BusName = "org.fcitx.Fcitx5";

    public const string InputMethodPath = "/org/freedesktop/portal/inputmethod";
    public const string InputMethodInterface = "org.fcitx.Fcitx.InputMethod1";

    public const string InputContextInterface = "org.fcitx.Fcitx.InputContext1";

    public const string ControllerPath = "/controller";
    public const string ControllerInterface = "org.fcitx.Fcitx.Controller1";
}
