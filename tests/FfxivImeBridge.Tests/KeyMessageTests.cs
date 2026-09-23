using FfxivImeBridge.Capture;
using Xunit;

namespace FfxivImeBridge.Tests;

public sealed class KeyMessageTests
{
    private static nint LParam(int scanCode, bool extended = false, bool alt = false, bool wasDown = false, int repeat = 1)
        => Messages.LParam(scanCode, extended, alt, wasDown, repeat);

    [Fact]
    public void Parses_a_key_down_with_its_virtual_key_and_scancode()
    {
        var m = new KeyMessage(WindowMessage.KeyDown, 0x4B /* K */, LParam(0x25));

        Assert.Equal(KeyMessageKind.KeyDown, m.Kind);
        Assert.Equal(0x4B, m.VirtualKey);
        Assert.Equal(0x25, m.ScanCode);
        Assert.False(m.IsRepeat);
        Assert.False(m.AltHeld);
        Assert.False(m.IsExtended);
    }

    [Fact]
    public void Reads_repeat_alt_and_extended_bits()
    {
        var m = new KeyMessage(WindowMessage.SysKeyDown, 0xDC, LParam(0x29, extended: true, alt: true, wasDown: true, repeat: 3));

        Assert.True(m.IsRepeat);
        Assert.True(m.AltHeld);
        Assert.True(m.IsExtended);
        Assert.Equal(3, m.RepeatCount);
        Assert.True(m.IsSystem);
    }

    [Fact]
    public void Classifies_every_keyboard_message()
    {
        Assert.Equal(KeyMessageKind.KeyDown, new KeyMessage(WindowMessage.KeyDown, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.KeyDown, new KeyMessage(WindowMessage.SysKeyDown, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.KeyUp, new KeyMessage(WindowMessage.KeyUp, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.KeyUp, new KeyMessage(WindowMessage.SysKeyUp, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.Char, new KeyMessage(WindowMessage.Char, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.Char, new KeyMessage(WindowMessage.SysChar, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.Char, new KeyMessage(WindowMessage.UniChar, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.DeadChar, new KeyMessage(WindowMessage.DeadChar, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.DeadChar, new KeyMessage(WindowMessage.SysDeadChar, 0, 0).Kind);
        Assert.Equal(KeyMessageKind.NotKeyboard, new KeyMessage(0x0200 /* WM_MOUSEMOVE */, 0, 0).Kind);
    }

    [Fact]
    public void A_char_message_carries_its_code_point()
    {
        var m = new KeyMessage(WindowMessage.Char, 0x00E4 /* ä */, LParam(0x28));

        Assert.Equal(0x00E4u, m.CodePoint);
        Assert.Equal(0x28, m.ScanCode);
    }

    [Fact]
    public void Describes_itself_for_the_trace()
    {
        Assert.Equal("KEYDOWN vk=0x4B sc=0x25", new KeyMessage(WindowMessage.KeyDown, 0x4B, LParam(0x25)).ToString());
        Assert.Equal("SYSKEYDOWN vk=0xDC sc=0x29 alt repeat", new KeyMessage(WindowMessage.SysKeyDown, 0xDC, LParam(0x29, alt: true, wasDown: true)).ToString());
        Assert.Equal("CHAR U+00E4 'ä' sc=0x28", new KeyMessage(WindowMessage.Char, 0x00E4, LParam(0x28)).ToString());
        Assert.Equal("CHAR U+000D sc=0x1C", new KeyMessage(WindowMessage.Char, 0x0D, LParam(0x1C)).ToString());
    }
}
