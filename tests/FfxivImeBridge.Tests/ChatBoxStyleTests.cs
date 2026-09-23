using System.Numerics;
using FfxivImeBridge.NativeWrite;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// The text input's own styling as the overlay uses it (ticket 14): the IME
/// colour is the handler's value when the addon set one, else the ULD data's,
/// and it only colours the Preedit when it would read on our dark ground.
/// Colours are the game's <c>ByteColor</c>: R in the low byte, A in the high one.
/// </summary>
public sealed class ChatBoxStyleTests
{
    private const uint OpaqueWhite = 0xFFFFFFFF;
    private const uint OpaqueGold = 0xFF40C0FF; // R=FF G=C0 B=40 A=FF

    [Fact]
    public void The_handler_ime_colour_wins_over_the_uld_one_when_set()
    {
        var style = new ChatBoxStyle(FontSizePt: 12, FontType: 0, Scale: 1f, ImeColor: OpaqueWhite, HandlerImeColor: OpaqueGold, CandidateColor: 0);
        Assert.Equal(OpaqueGold, style.ImeColorInUse);

        var unset = style with { HandlerImeColor = null };
        Assert.Equal(OpaqueWhite, unset.ImeColorInUse);

        var zero = style with { HandlerImeColor = 0 };
        Assert.Equal(OpaqueWhite, zero.ImeColorInUse);
    }

    [Fact]
    public void A_readable_ime_colour_becomes_the_preedit_ink()
    {
        var style = new ChatBoxStyle(FontSizePt: 12, FontType: 0, Scale: 1f, ImeColor: OpaqueGold, HandlerImeColor: null, CandidateColor: 0);

        var ink = style.PreeditInk;

        Assert.NotNull(ink);
        Assert.Equal(new Vector4(1f, 0.75294f, 0.25098f, 1f), ink.Value, new Vector4Comparer(0.001f));
    }

    [Theory]
    [InlineData(0x00000000u)] // nothing set
    [InlineData(0x40FFFFFFu)] // faint: alpha under half
    [InlineData(0xFF000000u)] // black on our dark ground
    [InlineData(0xFF202020u)] // near-black
    public void An_unreadable_ime_colour_leaves_the_preedit_ink_alone(uint rgba)
    {
        var style = new ChatBoxStyle(FontSizePt: 12, FontType: 0, Scale: 1f, ImeColor: rgba, HandlerImeColor: null, CandidateColor: 0);

        Assert.Null(style.PreeditInk);
    }

    private sealed class Vector4Comparer(float tolerance) : IEqualityComparer<Vector4>
    {
        public bool Equals(Vector4 a, Vector4 b) => Vector4.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector4 v) => 0;
    }
}
