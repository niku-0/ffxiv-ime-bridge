using System.Numerics;
using FfxivImeBridge.Capture;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>Mouse messages as Windows packs them (ticket 16): which are filtered, what a press, release and wheel look like.</summary>
public sealed class MouseMessageTests
{
    [Theory]
    [InlineData(MouseWindowMessage.LButtonDown, (int)MouseMessageKind.Press, (int)MouseButton.Left)]
    [InlineData(MouseWindowMessage.LButtonDblClk, (int)MouseMessageKind.Press, (int)MouseButton.Left)]
    [InlineData(MouseWindowMessage.LButtonUp, (int)MouseMessageKind.Release, (int)MouseButton.Left)]
    [InlineData(MouseWindowMessage.RButtonDown, (int)MouseMessageKind.Press, (int)MouseButton.Right)]
    [InlineData(MouseWindowMessage.RButtonUp, (int)MouseMessageKind.Release, (int)MouseButton.Right)]
    [InlineData(MouseWindowMessage.MButtonDown, (int)MouseMessageKind.Press, (int)MouseButton.Middle)]
    [InlineData(MouseWindowMessage.MButtonUp, (int)MouseMessageKind.Release, (int)MouseButton.Middle)]
    [InlineData(MouseWindowMessage.XButtonDown, (int)MouseMessageKind.Press, (int)MouseButton.X)]
    [InlineData(MouseWindowMessage.XButtonUp, (int)MouseMessageKind.Release, (int)MouseButton.X)]
    public void Buttons_decode_to_a_press_or_release_of_their_button(uint message, int kind, int button) // ints: the enums are internal, a public theory cannot name them
    {
        var m = new MouseMessage(message, 0, new Vector2(10, 20));

        Assert.Equal((MouseMessageKind)kind, m.Kind);
        Assert.Equal((MouseButton)button, m.Button);
    }

    [Fact]
    public void The_wheel_carries_its_signed_delta_in_the_high_word()
    {
        var up = new MouseMessage(MouseWindowMessage.MouseWheel, (nuint)(120 << 16), Vector2.Zero);
        var down = new MouseMessage(MouseWindowMessage.MouseWheel, unchecked((nuint)(uint)(-120 << 16)), Vector2.Zero);

        Assert.Equal(MouseMessageKind.Wheel, up.Kind);
        Assert.Equal(120, up.WheelDelta);
        Assert.Equal(-120, down.WheelDelta);
    }

    [Fact]
    public void A_point_is_the_signed_low_and_high_words_of_lparam()
    {
        Assert.Equal(new Vector2(300, 1200), MouseMessage.PointOf((1200 << 16) | 300));
        Assert.Equal(new Vector2(-5, 7), MouseMessage.PointOf(unchecked((nint)(uint)((7 << 16) | 0xFFFB))));
    }

    [Fact]
    public void Buttons_and_the_wheel_are_filtered_and_moves_are_not()
    {
        Assert.True(MouseWindowMessage.IsFiltered(MouseWindowMessage.LButtonDown));
        Assert.True(MouseWindowMessage.IsFiltered(MouseWindowMessage.XButtonDblClk));
        Assert.True(MouseWindowMessage.IsFiltered(MouseWindowMessage.MouseWheel));
        Assert.False(MouseWindowMessage.IsFiltered(MouseWindowMessage.MouseMove));
        Assert.False(MouseWindowMessage.IsFiltered(MouseWindowMessage.MouseHWheel));
        Assert.False(MouseWindowMessage.IsFiltered(WindowMessage.KeyDown));
    }

    [Fact]
    public void Prints_the_message_and_the_point()
    {
        Assert.Equal("LBUTTONDOWN (10,20)", new MouseMessage(MouseWindowMessage.LButtonDown, 0, new Vector2(10, 20)).ToString());
        Assert.Equal("MOUSEWHEEL +120 (0,0)", new MouseMessage(MouseWindowMessage.MouseWheel, (nuint)(120 << 16), Vector2.Zero).ToString());
    }
}
