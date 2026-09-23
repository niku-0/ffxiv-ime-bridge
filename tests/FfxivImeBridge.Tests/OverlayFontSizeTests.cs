using FfxivImeBridge.Rendering;
using Xunit;

namespace FfxivImeBridge.Tests;

/// <summary>
/// Which pixel size the AXIS handle is built at (ticket 14): the Chat Box text
/// node's own size at its HUD scale by default, the config override otherwise.
/// The node's <c>FontSize</c> is the game's pt unit (AXIS_12 draws 16 px lines
/// at 100 %), so px = pt × 4/3.
/// </summary>
public sealed class OverlayFontSizeTests
{
    [Fact]
    public void The_chat_box_text_nodes_size_at_100_percent_is_four_thirds_of_its_pt_value()
    {
        Assert.Equal(16f, OverlayFontSize.Choose(nodeFontSizePt: 12, scale: 1f, overridePx: 0f));
        Assert.Equal(24f, OverlayFontSize.Choose(nodeFontSizePt: 18, scale: 1f, overridePx: 0f));
    }

    [Fact]
    public void The_hud_scale_multiplies_the_node_size()
    {
        Assert.Equal(24f, OverlayFontSize.Choose(nodeFontSizePt: 12, scale: 1.5f, overridePx: 0f));
        Assert.Equal(13f, OverlayFontSize.Choose(nodeFontSizePt: 12, scale: 0.8f, overridePx: 0f)); // 12.8 rounded: one handle per whole px
    }

    [Fact]
    public void An_override_is_taken_as_screen_pixels_regardless_of_node_and_scale()
    {
        Assert.Equal(24f, OverlayFontSize.Choose(nodeFontSizePt: 12, scale: 1.5f, overridePx: 24f));
        Assert.Equal(24f, OverlayFontSize.Choose(nodeFontSizePt: null, scale: 1f, overridePx: 24f));
    }

    [Fact]
    public void A_missing_or_zero_node_size_falls_back_to_the_chat_default_of_12_pt()
    {
        Assert.Equal(16f, OverlayFontSize.Choose(nodeFontSizePt: null, scale: 1f, overridePx: 0f));
        Assert.Equal(32f, OverlayFontSize.Choose(nodeFontSizePt: 0, scale: 2f, overridePx: 0f));
    }

    [Fact]
    public void The_chat_texts_own_size_is_unrounded_so_its_baseline_is_where_the_game_draws_it()
    {
        Assert.Equal(12.8f, OverlayFontSize.ChatTextPx(nodeFontSizePt: 12, scale: 0.8f), precision: 4);
        Assert.Equal(16f, OverlayFontSize.ChatTextPx(nodeFontSizePt: null, scale: 1f));
    }

    [Fact]
    public void Nothing_smaller_than_the_minimum_is_ever_built()
    {
        Assert.Equal(OverlayFontSize.MinimumPx, OverlayFontSize.Choose(nodeFontSizePt: 12, scale: 0.1f, overridePx: 0f));
        Assert.Equal(OverlayFontSize.MinimumPx, OverlayFontSize.Choose(nodeFontSizePt: null, scale: 1f, overridePx: 1f));
    }
}
