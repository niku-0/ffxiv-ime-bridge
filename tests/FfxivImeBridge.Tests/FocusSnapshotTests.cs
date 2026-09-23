using FfxivImeBridge.Capture;
using Xunit;

namespace FfxivImeBridge.Tests;

public sealed class FocusSnapshotTests
{
    [Fact]
    public void The_chat_box_is_focused_when_the_active_text_input_belongs_to_chat_log()
    {
        Assert.True(new FocusSnapshot(TextInputActive: true, OwnerAddon: "ChatLog", UiHidden: false, WindowActive: true).ChatBoxFocused);
    }

    [Fact]
    public void The_chat_box_is_not_focused_while_the_game_window_is_inactive()
    {
        // Alt-tab keeps the box's native focus but the session must treat it as lost (ticket 11).
        Assert.False(new FocusSnapshot(TextInputActive: true, OwnerAddon: "ChatLog", UiHidden: false, WindowActive: false).ChatBoxFocused);
    }

    [Fact]
    public void Another_addons_text_input_is_not_the_chat_box()
    {
        Assert.False(new FocusSnapshot(TextInputActive: true, OwnerAddon: "LookingForGroupCondition", UiHidden: false, WindowActive: true).ChatBoxFocused);
    }

    [Fact]
    public void An_unresolved_owner_is_not_the_chat_box()
    {
        // The owner walk resolves for every native field tried in-game (ticket 03), so null means "not ChatLog".
        Assert.False(new FocusSnapshot(TextInputActive: true, OwnerAddon: null, UiHidden: false, WindowActive: true).ChatBoxFocused);
    }

    [Fact]
    public void Nothing_is_focused_without_an_active_text_input_or_with_the_ui_hidden()
    {
        Assert.False(new FocusSnapshot(TextInputActive: false, OwnerAddon: "ChatLog", UiHidden: false, WindowActive: true).ChatBoxFocused);
        Assert.False(new FocusSnapshot(TextInputActive: true, OwnerAddon: "ChatLog", UiHidden: true, WindowActive: true).ChatBoxFocused);
    }
}
