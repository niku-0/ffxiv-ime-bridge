using System.Text;
using FfxivImeBridge.NativeWrite;
using Xunit;

namespace FfxivImeBridge.Tests;

public sealed class ChatBoxSpliceTests
{
    private static readonly ChatBoxLimits NoLimits = new(MaxChars: 0, MaxBytes: 0);

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    private static string Text(SplicePlan plan) => Encoding.UTF8.GetString(plan.Text);

    [Fact]
    public void Inserts_mid_string_and_puts_the_cursor_after_the_inserted_text()
    {
        var plan = ChatBoxSplice.Plan(Utf8("ab"), cursorByteOffset: 1, "こんにちは", NoLimits);

        Assert.False(plan.IsOverflow);
        Assert.Equal("aこんにちはb", Text(plan));
        Assert.Equal(1 + Utf8("こんにちは").Length, plan.CursorByteOffset);
        Assert.False(plan.AppendedAtEnd);
    }

    [Fact]
    public void Inserts_at_the_end()
    {
        var plan = ChatBoxSplice.Plan(Utf8("ab"), cursorByteOffset: 2, "x", NoLimits);

        Assert.Equal("abx", Text(plan));
        Assert.Equal(3, plan.CursorByteOffset);
        Assert.False(plan.AppendedAtEnd);
    }

    [Fact]
    public void Inserts_into_an_empty_box()
    {
        var plan = ChatBoxSplice.Plan([], cursorByteOffset: 0, "こ", NoLimits);

        Assert.Equal("こ", Text(plan));
        Assert.Equal(3, plan.CursorByteOffset);
    }

    [Fact]
    public void Appends_at_the_end_when_the_cursor_is_unknown()
    {
        var plan = ChatBoxSplice.Plan(Utf8("ab"), cursorByteOffset: null, "x", NoLimits);

        Assert.Equal("abx", Text(plan));
        Assert.Equal(3, plan.CursorByteOffset);
        Assert.True(plan.AppendedAtEnd);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Appends_at_the_end_when_the_cursor_is_out_of_range(int cursor)
    {
        var plan = ChatBoxSplice.Plan(Utf8("ab"), cursor, "x", NoLimits);

        Assert.Equal("abx", Text(plan));
        Assert.True(plan.AppendedAtEnd);
    }

    [Fact]
    public void Appends_at_the_end_when_the_cursor_is_inside_a_multibyte_character()
    {
        // "あ" is 3 bytes; offset 1 would split it.
        var plan = ChatBoxSplice.Plan(Utf8("あb"), cursorByteOffset: 1, "x", NoLimits);

        Assert.Equal("あbx", Text(plan));
        Assert.True(plan.AppendedAtEnd);
    }

    [Fact]
    public void Appends_at_the_end_when_the_buffer_holds_a_payload()
    {
        // An auto-translate entry is a 0x02 … 0x03 payload; the cursor index no longer maps onto bytes.
        byte[] current = [(byte)'a', 0x02, 0x2E, 0x03, (byte)'b'];
        var plan = ChatBoxSplice.Plan(current, cursorByteOffset: 1, "x", NoLimits);

        Assert.True(plan.AppendedAtEnd);
        Assert.Equal(current.Length, plan.CursorByteOffset - 1);
        Assert.Equal((byte)'x', plan.Text[^1]);
    }

    [Fact]
    public void Refuses_when_the_character_limit_would_be_exceeded()
    {
        var plan = ChatBoxSplice.Plan(Utf8("abc"), cursorByteOffset: 3, "こんにちは", new ChatBoxLimits(MaxChars: 5, MaxBytes: 0));

        Assert.True(plan.IsOverflow);
        Assert.Equal(3, plan.CharsOver); // 3 + 5 = 8 code points, limit 5
        Assert.Equal(0, plan.BytesOver);
        Assert.Equal("abc", Text(plan)); // untouched
        Assert.Equal(3, plan.CursorByteOffset);
    }

    [Fact]
    public void Refuses_when_the_byte_limit_would_be_exceeded()
    {
        var plan = ChatBoxSplice.Plan(Utf8("abc"), cursorByteOffset: 3, "こんにちは", new ChatBoxLimits(MaxChars: 0, MaxBytes: 16));

        Assert.True(plan.IsOverflow);
        Assert.Equal(0, plan.CharsOver);
        Assert.Equal(2, plan.BytesOver); // 3 + 15 = 18 bytes, limit 16
        Assert.Equal("abc", Text(plan));
    }

    [Fact]
    public void Fits_exactly_at_the_limit()
    {
        var plan = ChatBoxSplice.Plan(Utf8("abc"), cursorByteOffset: 3, "こん", new ChatBoxLimits(MaxChars: 5, MaxBytes: 9));

        Assert.False(plan.IsOverflow);
        Assert.Equal("abcこん", Text(plan));
    }

    [Theory]
    [InlineData("ab\nc", "abc", 1)]
    [InlineData("a\u0002b\u007Fc\n", "abc", 3)] // U+0002 is the SeString payload start byte
    [InlineData("\r\n", "", 2)]
    public void Drops_control_characters_from_the_commit_before_counting_and_splicing(string committed, string expected, int dropped)
    {
        var plan = ChatBoxSplice.Plan(Utf8("x"), cursorByteOffset: 1, committed, NoLimits);

        Assert.Equal(expected, plan.Committed);
        Assert.Equal(dropped, plan.ControlsDropped);
        Assert.Equal("x" + expected, Text(plan));
        Assert.Equal(1 + Utf8(expected).Length, plan.CursorByteOffset);
    }

    [Fact]
    public void Leaves_a_plain_commit_alone()
    {
        var plan = ChatBoxSplice.Plan(Utf8("ab"), cursorByteOffset: 1, "こんにちは😀", NoLimits);

        Assert.Equal("こんにちは😀", plan.Committed); // the surrogate pair survives whole
        Assert.Equal(0, plan.ControlsDropped);
        Assert.Equal("aこんにちは😀b", Text(plan));
    }

    [Fact]
    public void Counts_the_limits_against_the_commit_as_it_goes_in()
    {
        // "\n\nxy" is 5 bytes as it arrives and 2 as it is written: it fits.
        var plan = ChatBoxSplice.Plan(Utf8("abc"), cursorByteOffset: 3, "\n\nxy", new ChatBoxLimits(MaxChars: 0, MaxBytes: 5));

        Assert.False(plan.IsOverflow);
        Assert.Equal("abcxy", Text(plan));
    }

    [Fact]
    public void Counts_code_points_not_utf16_units()
    {
        // 😀 is one code point (4 bytes, 2 UTF-16 units).
        var plan = ChatBoxSplice.Plan([], cursorByteOffset: 0, "😀😀", new ChatBoxLimits(MaxChars: 2, MaxBytes: 0));

        Assert.False(plan.IsOverflow);
    }
}

public sealed class CursorIndexTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 4)] // after "aあ"
    [InlineData(3, 5)] // end of "aあb"
    public void Code_point_index_to_byte_offset(int index, int expectedOffset)
    {
        Assert.Equal(expectedOffset, CursorIndex.ToByteOffset(Utf8("aあb"), index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Code_point_index_out_of_range_is_null(int index)
    {
        Assert.Null(CursorIndex.ToByteOffset(Utf8("aあb"), index));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void Byte_offset_to_code_point_index(int offset, int expectedIndex)
    {
        Assert.Equal(expectedIndex, CursorIndex.FromByteOffset(Utf8("aあb"), offset));
    }

    [Fact]
    public void Counts_code_points()
    {
        Assert.Equal(3, CursorIndex.CountCodePoints(Utf8("aあb")));
        Assert.Equal(0, CursorIndex.CountCodePoints([]));
    }
}
