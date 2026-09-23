using System.Text;
using FfxivImeBridge.NativeWrite;
using Xunit;

namespace FfxivImeBridge.Tests;

public sealed class ChatBoxStateTests
{
    private static ChatBoxState State(string raw, bool moduleTarget, int componentCursor, short moduleCursor, int beforeBytes) => new(
        RawText: Encoding.UTF8.GetBytes(raw), EvaluatedText: raw, IsActive: moduleTarget,
        ComponentCursor: componentCursor, ComponentSelectionStart: componentCursor, ComponentSelectionEnd: componentCursor,
        IsModuleTarget: moduleTarget, ModuleCursor: moduleCursor, ModuleTextLength: 0, ModuleSelectionStart: moduleCursor, ModuleSelectionEnd: moduleCursor,
        ModuleBeforeSelectionBytes: beforeBytes, ModuleSelectedBytes: 0, ModuleAfterSelectionBytes: 0, ModuleInputBytes: 0,
        InputMaxLength: 500, MaxChar: 500, MaxByte: 0, MaxLine: 1, MaxWidth: 0, HandlerMaxChar: null, HandlerMaxByte: null,
        TextNode: null, CursorNode: null, Style: default, MeasuredCursorX: null, MeasureError: null);

    [Fact]
    public void The_cursor_is_the_modules_while_the_chat_box_is_its_target()
    {
        var state = State("aあb", moduleTarget: true, componentCursor: 0, moduleCursor: 2, beforeBytes: 4);

        Assert.Equal(2, state.CursorIndex);
        Assert.Equal(4, state.CursorByteOffset);
    }

    [Fact]
    public void The_cursor_is_the_components_while_the_module_targets_something_else()
    {
        var state = State("aあb", moduleTarget: false, componentCursor: 3, moduleCursor: 1, beforeBytes: 1);

        Assert.Equal(3, state.CursorIndex);
        Assert.Equal(5, state.CursorByteOffset);
    }

    [Fact]
    public void A_cursor_past_the_text_has_no_byte_offset()
    {
        var state = State("aあb", moduleTarget: true, componentCursor: 0, moduleCursor: 4, beforeBytes: 0);

        Assert.Null(state.CursorByteOffset);
    }

    [Fact]
    public void Handler_limits_win_over_uld_limits_when_set()
    {
        var uld = State("", moduleTarget: false, componentCursor: 0, moduleCursor: 0, beforeBytes: 0);
        Assert.Equal(new ChatBoxLimits(500, 0), uld.Limits);

        var handler = uld with { HandlerMaxChar = 100, HandlerMaxByte = 300 };
        Assert.Equal(new ChatBoxLimits(100, 300), handler.Limits);
    }

    [Fact]
    public void Input_max_length_stands_in_when_the_uld_character_limit_is_zero()
    {
        var state = State("", moduleTarget: false, componentCursor: 0, moduleCursor: 0, beforeBytes: 0) with { MaxChar = 0, InputMaxLength = 200 };
        Assert.Equal(new ChatBoxLimits(200, 0), state.Limits);
    }

    [Fact]
    public void A_handler_value_of_zero_means_unset_not_a_limit_of_zero()
    {
        // What the Chat Box reads in-game (ticket 04): handler MaxChar=0, MaxByte=500, GetInputMaxLength=500.
        var state = State("", moduleTarget: false, componentCursor: 0, moduleCursor: 0, beforeBytes: 0)
            with { MaxChar = 0, MaxByte = 500, InputMaxLength = 500, HandlerMaxChar = 0, HandlerMaxByte = 500 };
        Assert.Equal(new ChatBoxLimits(500, 500), state.Limits);
    }
}
