using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FfxivImeBridge.Capture;

/// <summary>What the game says about its text input at one instant.</summary>
/// <param name="TextInputActive"><c>AtkModule.IsTextInputActive()</c>: some native text field has keyboard focus.</param>
/// <param name="OwnerAddon">Name of the addon owning the active text input (<c>ChatLog</c> for the Chat Box), or null if it could not be resolved.</param>
/// <param name="UiHidden">The game UI is hidden (Scroll Lock); nothing native can take keys.</param>
/// <param name="WindowActive">The game window is the active window (<c>Framework.WindowInactive</c> negated); alt-tab clears it while the box keeps its native focus.</param>
internal readonly record struct FocusSnapshot(bool TextInputActive, string? OwnerAddon, bool UiHidden, bool WindowActive)
{
    public const string ChatLogAddon = "ChatLog";

    /// <summary>
    /// The Chat Box has focus for the session's purposes: the active text
    /// input's owner addon is ChatLog <em>and</em> the game window is active.
    /// The walk resolved for every field tried in-game (ticket 03), so an
    /// unresolved owner means "some other field", never the Chat Box. Window
    /// activity is part of it because the box keeps its native focus through
    /// an alt-tab, while fcitx5 focuses our context out for the other window's
    /// and Mozc commits (ticket 11): the session must see that as a loss.
    /// </summary>
    public bool ChatBoxFocused => TextInputActive && !UiHidden && WindowActive && OwnerAddon == ChatLogAddon;

    /// <summary>Short form for the trace: the owner addon, <c>?</c> if unresolved, <c>-</c> if no text input is active.</summary>
    public string OwnerLabel => OwnerAddon ?? (TextInputActive ? "?" : "-");

    public override string ToString() =>
        $"textInputActive={TextInputActive} owner={OwnerLabel} uiHidden={UiHidden} windowActive={WindowActive} → chatBoxFocused={ChatBoxFocused}";
}

/// <summary>Reads the focus state from <c>RaptureAtkModule</c> and the window state from <c>Framework</c>. Main thread only.</summary>
internal static unsafe class ChatBoxFocus
{
    public static FocusSnapshot Read(IGameGui gui)
    {
        var uiHidden = gui.GameUiHidden;
        // Window activation is a *sent* message, so the message pump hook never sees it; the game's own flag is polled instead.
        // Unreadable ⇒ active: the flag only ever *adds* a focus loss, and inventing one here would unfocus the context for good,
        // whereas a missing ATK module below means there really is nothing focused.
        var framework = Framework.Instance();
        var windowActive = framework == null || !framework->WindowInactive;
        var atk = RaptureAtkModule.Instance();
        if (atk == null) return new FocusSnapshot(false, null, uiHidden, windowActive);

        var active = atk->AtkModule.IsTextInputActive();
        return new FocusSnapshot(active, active ? OwnerAddonName(atk) : null, uiHidden, windowActive);
    }

    /// <summary>
    /// Active text input → its event interface → the component node that owns it →
    /// the text-input component → its addon. Each hop is null-checked because
    /// the game clears them in an order of its own while focus moves.
    /// </summary>
    private static string? OwnerAddonName(RaptureAtkModule* atk)
    {
        var eventInterface = atk->AtkModule.TextInput.TargetTextInputEventInterface;
        if (eventInterface == null) return null;

        var node = eventInterface->GetOwnerNode();
        if (node == null || (ushort)node->Type < 1000) return null; // not a component node

        var component = ((AtkComponentNode*)node)->Component;
        if (component == null || component->GetComponentType() != ComponentType.TextInput) return null;

        var addon = ((AtkComponentInputBase*)component)->OwnerAddon;
        return addon == null ? null : addon->NameString;
    }
}
