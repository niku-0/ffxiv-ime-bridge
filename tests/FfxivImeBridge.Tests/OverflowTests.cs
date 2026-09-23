using System.Text;
using FfxivImeBridge.NativeWrite;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>The refusal line for a Commit that would push the Chat Box past its limit: bytes over, and about how many characters that is.</summary>
public sealed class OverflowTests
{
    private static readonly ChatBoxLimits ChatBox = new(MaxChars: 0, MaxBytes: 500); // what the Chat Box reads in-game (ticket 04)

    [Fact]
    public void States_bytes_over_and_the_approximate_character_count()
    {
        // 498 bytes in the box + こんにちは (15 bytes) = 13 over; 13 bytes is about 5 kana (3 bytes each, rounded up).
        var plan = ChatBoxSplice.Plan(Encoding.UTF8.GetBytes(new string('a', 498)), 498, "こんにちは", ChatBox);

        Assert.Equal("13 bytes (about 5 characters) over the limit of 500 bytes", Overflow.Describe(plan, ChatBox));
    }

    [Fact]
    public void A_single_byte_over_is_still_one_character()
    {
        var plan = ChatBoxSplice.Plan(Encoding.UTF8.GetBytes(new string('a', 500)), 500, "a", ChatBox);

        Assert.Equal("1 byte (about 1 character) over the limit of 500 bytes", Overflow.Describe(plan, ChatBox));
    }

    [Fact]
    public void A_character_limit_is_stated_in_characters()
    {
        // Not the Chat Box (MaxChar reads 0 there), but a limit the input could state.
        var limits = new ChatBoxLimits(MaxChars: 10, MaxBytes: 0);
        var plan = ChatBoxSplice.Plan(Encoding.UTF8.GetBytes("abcdefgh"), 8, "こんにちは", limits);

        Assert.Equal("3 characters over the limit of 10 characters", Overflow.Describe(plan, limits));
    }
}
