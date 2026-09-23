using FfxivImeBridge.Capture;
using FfxivImeBridge.Fcitx;
using Xunit;
using static FfxivImeBridge.Tests.Messages;

namespace FfxivImeBridge.Tests;

public sealed class KeyTranslationTests
{
    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkCapital = 0x14;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkPrior = 0x21;
    private const int VkEnd = 0x23;
    private const int VkHome = 0x24;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkDelete = 0x2E;
    private const int Vk2 = 0x32;
    private const int VkA = 0x41;
    private const int VkK = 0x4B;
    private const int VkV = 0x56;
    private const int VkLWin = 0x5B;
    private const int VkNumpad7 = 0x67;
    private const int VkF1 = 0x70;
    private const int VkF12 = 0x7B;
    private const int VkF24 = 0x87;
    private const int VkNumLock = 0x90;
    private const int VkScroll = 0x91;
    private const int VkOem7 = 0xDE; // what Wine reports for § on a Nordic layout (M0.3 trace)
    private const int VkOemComma = 0xBC;

    private const int ScK = 0x25;
    private const int ScA = 0x1E;
    private const int Sc2 = 0x03;
    private const int ScSection = 0x29;

    private static readonly Modifiers None = default;
    private static readonly Modifiers Ctrl = new() { Ctrl = true };
    private static readonly Modifiers Alt = new() { Alt = true };
    private static readonly Modifiers AltGr = new() { Ctrl = true, Alt = true };

    private static KeyMessage ExtDown(int vk, int sc) => new(WindowMessage.KeyDown, (nuint)vk, LParam(sc, extended: true));

    // --- Classification ---

    [Theory]
    [InlineData(VkK, ScK)]
    [InlineData(VkA, ScA)]
    [InlineData(Vk2, Sc2)]
    [InlineData(VkSpace, 0x39)]
    [InlineData(VkOem7, ScSection)] // § on a Nordic layout
    [InlineData(VkOemComma, 0x33)]
    [InlineData(VkNumpad7, 0x47)]
    public void A_text_key_alone_is_Printing(int vk, int sc)
    {
        Assert.Equal(KeyClass.Printing, KeyTranslation.Classify(Down(vk, sc), None));
    }

    [Fact]
    public void Shift_does_not_change_a_printing_key()
    {
        Assert.Equal(KeyClass.Printing, KeyTranslation.Classify(Down(VkA, ScA), new Modifiers { Shift = true }));
    }

    [Theory]
    [InlineData(VkReturn, 0x1C)]
    [InlineData(VkEscape, 0x01)]
    [InlineData(VkTab, 0x0F)]
    [InlineData(VkBack, 0x0E)]
    [InlineData(VkF1, 0x3B)]
    [InlineData(VkF12, 0x58)]
    [InlineData(VkF24, 0x76)]
    public void A_non_printing_key_is_Fixed(int vk, int sc)
    {
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(Down(vk, sc), None));
    }

    [Theory]
    [InlineData(VkDelete, 0x53)]
    [InlineData(VkLeft, 0x4B)]
    [InlineData(VkUp, 0x48)]
    [InlineData(VkHome, 0x47)]
    [InlineData(VkEnd, 0x4F)]
    [InlineData(VkPrior, 0x49)]
    public void An_extended_navigation_key_is_Fixed(int vk, int sc)
    {
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(ExtDown(vk, sc), None));
    }

    [Fact]
    public void A_letter_with_Ctrl_is_Fixed()
    {
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(Down(VkV, 0x2F), Ctrl));
    }

    [Fact]
    public void A_letter_with_Alt_is_Fixed_whether_read_from_the_message_or_the_state()
    {
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(SysDown(VkA, ScA), None));
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(Down(VkA, ScA), Alt));
    }

    [Fact]
    public void Space_with_Ctrl_is_Fixed()
    {
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(Down(VkSpace, 0x39), Ctrl));
    }

    [Fact]
    public void AltGr_with_a_text_key_is_Printing()
    {
        // Nordic layout: AltGr+2 types @. Windows reports AltGr as Ctrl+Alt; Wine may report right Alt alone.
        Assert.Equal(KeyClass.Printing, KeyTranslation.Classify(Down(Vk2, Sc2), AltGr));
        Assert.Equal(KeyClass.Printing, KeyTranslation.Classify(Down(Vk2, Sc2), new Modifiers { RightAlt = true }));
        Assert.Equal(KeyClass.Printing, KeyTranslation.Classify(SysDown(Vk2, Sc2), new Modifiers { Ctrl = true }));
    }

    [Fact]
    public void AltGr_with_a_non_printing_key_is_still_Fixed()
    {
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(Down(VkReturn, 0x1C), AltGr));
    }

    [Theory]
    [InlineData(VkShift, 0x2A, false)]
    [InlineData(VkControl, 0x1D, false)]
    [InlineData(VkControl, 0x1D, true)]
    [InlineData(VkMenu, 0x38, false)]
    [InlineData(VkMenu, 0x38, true)]
    [InlineData(VkLWin, 0x5B, true)]
    [InlineData(VkCapital, 0x3A, false)]
    [InlineData(VkNumLock, 0x45, false)]
    [InlineData(VkScroll, 0x46, false)]
    public void A_modifier_key_is_Modifier_on_press_and_release(int vk, int sc, bool extended)
    {
        var down = new KeyMessage(WindowMessage.KeyDown, (nuint)vk, LParam(sc, extended: extended));
        var up = new KeyMessage(WindowMessage.KeyUp, (nuint)vk, LParam(sc, extended: extended, wasDown: true));

        Assert.Equal(KeyClass.Modifier, KeyTranslation.Classify(down, None));
        Assert.Equal(KeyClass.Modifier, KeyTranslation.Classify(up, None));
        Assert.Equal(KeyClass.Modifier, KeyTranslation.Classify(down, AltGr));
    }

    [Fact]
    public void AltGr_as_Wine_posts_it_is_a_Modifier_press_of_right_Alt()
    {
        // Ticket 07 trace, Nordic layout: KEYDOWN vk=0xE4 sc=0x38 ext — not VK_RMENU, and no Alt context bit.
        var down = new KeyMessage(WindowMessage.KeyDown, 0xE4, LParam(0x38, extended: true));

        Assert.Equal(KeyClass.Modifier, KeyTranslation.Classify(down, None));
        var e = KeyTranslation.FromKey(down, None, time: 1);
        Assert.Equal(KeySym.AltR, e.KeySym);
        Assert.Equal(KeyCode.RightAlt, e.KeyCode);
    }

    [Fact]
    public void Alt_pressed_alone_is_Modifier_even_as_a_system_key()
    {
        // M0.3 trace: SYSKEYDOWN vk=0x12 sc=0x38 alt
        Assert.Equal(KeyClass.Modifier, KeyTranslation.Classify(SysDown(VkMenu, 0x38), None));
    }

    [Fact]
    public void A_dead_char_message_is_DeadChar()
    {
        var dead = new KeyMessage(WindowMessage.DeadChar, 0x00A8 /* ¨ */, LParam(0x1B));

        Assert.Equal(KeyClass.DeadChar, KeyTranslation.Classify(dead, None));
    }

    [Fact]
    public void A_release_classifies_like_its_press()
    {
        Assert.Equal(KeyClass.Printing, KeyTranslation.Classify(Up(VkK, ScK), None));
        Assert.Equal(KeyClass.Fixed, KeyTranslation.Classify(Up(VkReturn, 0x1C), None));
    }

    // --- KeyEvent from a WM_CHAR ---

    [Fact]
    public void A_char_becomes_a_press_with_its_code_point_and_its_own_scancode()
    {
        var e = KeyTranslation.FromChar(Char('k', ScK), None, time: 42);

        Assert.Equal(new KeyEvent(0x6B, KeyCode.FromAsciiChar('k'), KeyState.None, IsRelease: false, Time: 42), e);
    }

    [Fact]
    public void Section_on_a_Nordic_layout_is_Latin1_on_its_own_key()
    {
        // M0.3 trace: SYSCHAR U+00A7 '§' sc=0x29 (vk=0xDE). Latin-1 maps to the keysym directly.
        var e = KeyTranslation.FromChar(Char(0x00A7, ScSection), None, time: 1);

        Assert.Equal(0x00A7u, e!.Value.KeySym);
        Assert.Equal(KeyCode.FromWindowsScanCode(ScSection, extended: false), e.Value.KeyCode);
    }

    [Fact]
    public void A_char_beyond_Latin1_uses_the_Unicode_keysym_range()
    {
        var e = KeyTranslation.FromChar(Char(0x0101 /* ā */, ScA), None, time: 1);

        Assert.Equal(0x01000101u, e!.Value.KeySym);
    }

    [Fact]
    public void A_char_carries_the_modifier_state_as_read()
    {
        var mods = new Modifiers { Shift = true, CapsLock = true, NumLock = true };

        var e = KeyTranslation.FromChar(Char('A', ScA), mods, time: 1);

        Assert.Equal(KeyState.Shift | KeyState.CapsLock | KeyState.NumLock, e!.Value.State);
        Assert.False(e.Value.IsRelease);
    }

    [Fact]
    public void AltGr_2_is_the_at_sign_with_Ctrl_and_Alt_state()
    {
        var e = KeyTranslation.FromChar(Char('@', Sc2), AltGr, time: 1);

        Assert.Equal((uint)'@', e!.Value.KeySym);
        Assert.Equal(KeyCode.FromWindowsScanCode(Sc2, extended: false), e.Value.KeyCode);
        Assert.Equal(KeyState.Ctrl | KeyState.Alt, e.Value.State);
    }

    [Fact]
    public void A_sys_char_translates_like_a_char()
    {
        var e = KeyTranslation.FromChar(SysChar(0x00A7, ScSection), Alt, time: 1);

        Assert.Equal(0x00A7u, e!.Value.KeySym);
        Assert.Equal(KeyState.Alt, e.Value.State);
    }

    [Fact]
    public void A_surrogate_half_yields_no_event()
    {
        var high = Char(0xD83D, ScA);
        var low = Char(0xDE00, ScA);

        Assert.True(high.IsSurrogateHalf);
        Assert.True(low.IsSurrogateHalf);
        Assert.False(Char('k', ScK).IsSurrogateHalf);
        Assert.Null(KeyTranslation.FromChar(high, None, time: 1));
        Assert.Null(KeyTranslation.FromChar(low, None, time: 1));
    }

    [Fact]
    public void A_unichar_carries_a_full_code_point()
    {
        var uni = new KeyMessage(WindowMessage.UniChar, 0x1F600, LParam(ScA));

        Assert.False(uni.IsSurrogateHalf);
        Assert.Equal(0x0101F600u, KeyTranslation.FromChar(uni, None, time: 1)!.Value.KeySym);
    }

    [Fact]
    public void The_time_defaults_to_the_tick_count()
    {
        var before = unchecked((uint)Environment.TickCount);
        var e = KeyTranslation.FromChar(Char('k', ScK), None);
        var after = unchecked((uint)Environment.TickCount);

        Assert.InRange(e!.Value.Time, before, after);
    }

    // --- KeyEvent from a Fixed or Modifier key message ---

    [Theory]
    [InlineData(VkReturn, 0x1C, false, KeySym.Return, KeyCode.Enter)]
    [InlineData(VkEscape, 0x01, false, KeySym.Escape, KeyCode.Esc)]
    [InlineData(VkBack, 0x0E, false, KeySym.BackSpace, KeyCode.Backspace)]
    [InlineData(VkTab, 0x0F, false, KeySym.Tab, KeyCode.Tab)]
    [InlineData(VkDelete, 0x53, true, KeySym.Delete, KeyCode.Delete)]
    [InlineData(VkLeft, 0x4B, true, KeySym.Left, KeyCode.Left)]
    [InlineData(VkUp, 0x48, true, KeySym.Up, KeyCode.Up)]
    [InlineData(VkHome, 0x47, true, KeySym.Home, KeyCode.Home)]
    [InlineData(VkEnd, 0x4F, true, KeySym.End, KeyCode.End)]
    [InlineData(VkPrior, 0x49, true, KeySym.PageUp, KeyCode.PageUp)]
    [InlineData(VkF1, 0x3B, false, KeySym.F1, 0x3B + 8u)]
    [InlineData(VkF12, 0x58, false, KeySym.F12, 0x58 + 8u)]
    [InlineData(VkF24, 0x76, false, 0xffd5u, 0x76 + 8u)]
    public void A_non_printing_key_has_its_X_keysym_and_the_keycode_of_its_scancode(int vk, int sc, bool extended, uint keySym, uint keyCode)
    {
        var down = new KeyMessage(WindowMessage.KeyDown, (nuint)vk, LParam(sc, extended: extended));

        var e = KeyTranslation.FromKey(down, None, time: 7);

        Assert.Equal(new KeyEvent(keySym, keyCode, KeyState.None, IsRelease: false, Time: 7), e);
    }

    [Fact]
    public void Enter_on_the_numpad_is_KP_Enter()
    {
        var e = KeyTranslation.FromKey(ExtDown(VkReturn, 0x1C), None, time: 1);

        Assert.Equal(0xff8du, e.KeySym);
        Assert.Equal(96 + 8u, e.KeyCode);
    }

    [Fact]
    public void Ctrl_V_is_the_letter_with_Ctrl_state()
    {
        // M0.3 trace: KEYDOWN vk=0x56 sc=0x2F while Ctrl is held; the WM_CHAR that follows is U+0016, not the keysym.
        var e = KeyTranslation.FromKey(Down(VkV, 0x2F), Ctrl, time: 1);

        Assert.Equal((uint)'v', e.KeySym);
        Assert.Equal(KeyCode.FromAsciiChar('v'), e.KeyCode);
        Assert.Equal(KeyState.Ctrl, e.State);
    }

    [Fact]
    public void A_shifted_letter_chord_is_the_uppercase_keysym()
    {
        Assert.Equal((uint)'A', KeyTranslation.FromKey(Down(VkA, ScA), new Modifiers { Ctrl = true, Shift = true }, time: 1).KeySym);
        Assert.Equal((uint)'A', KeyTranslation.FromKey(Down(VkA, ScA), new Modifiers { Ctrl = true, CapsLock = true }, time: 1).KeySym);
        Assert.Equal((uint)'a', KeyTranslation.FromKey(Down(VkA, ScA), new Modifiers { Ctrl = true, Shift = true, CapsLock = true }, time: 1).KeySym);
    }

    [Fact]
    public void Alt_A_from_a_system_key_down_is_the_letter_with_Alt_state()
    {
        var e = KeyTranslation.FromKey(SysDown(VkA, ScA), None, time: 1);

        Assert.Equal((uint)'a', e.KeySym);
        Assert.Equal(KeyState.Alt, e.State);
    }

    [Fact]
    public void Ctrl_Space_and_Ctrl_digit_derive_from_the_virtual_key()
    {
        Assert.Equal(KeySym.Space, KeyTranslation.FromKey(Down(VkSpace, 0x39), Ctrl, time: 1).KeySym);
        Assert.Equal((uint)'2', KeyTranslation.FromKey(Down(Vk2, Sc2), Ctrl, time: 1).KeySym);
        Assert.Equal(0xffb7u /* KP_7 */, KeyTranslation.FromKey(Down(VkNumpad7, 0x47), Ctrl, time: 1).KeySym);
    }

    [Fact]
    public void A_chord_on_a_layout_specific_key_has_no_keysym_but_keeps_its_keycode()
    {
        var e = KeyTranslation.FromKey(Down(VkOem7, ScSection), Ctrl, time: 1);

        Assert.Equal(KeySym.VoidSymbol, e.KeySym);
        Assert.Equal(ScSection + 8u, e.KeyCode);
    }

    [Theory]
    [InlineData(VkShift, 0x2A, false, KeySym.ShiftL, KeyCode.LeftShift)]
    [InlineData(VkShift, 0x36, false, KeySym.ShiftR, KeyCode.RightShift)]
    [InlineData(VkControl, 0x1D, false, KeySym.ControlL, KeyCode.LeftCtrl)]
    [InlineData(VkControl, 0x1D, true, KeySym.ControlR, KeyCode.RightCtrl)]
    [InlineData(VkMenu, 0x38, false, KeySym.AltL, KeyCode.LeftAlt)]
    [InlineData(VkMenu, 0x38, true, KeySym.AltR, KeyCode.RightAlt)]
    [InlineData(VkLWin, 0x5B, true, KeySym.SuperL, KeyCode.LeftMeta)]
    [InlineData(VkCapital, 0x3A, false, 0xffe5u, 0x3A + 8u)]
    [InlineData(VkNumLock, 0x45, false, 0xff7fu, 0x45 + 8u)]
    [InlineData(VkScroll, 0x46, false, 0xff14u, 0x46 + 8u)]
    public void A_modifier_key_is_told_apart_by_side_from_its_scancode(int vk, int sc, bool extended, uint keySym, uint keyCode)
    {
        var down = new KeyMessage(WindowMessage.KeyDown, (nuint)vk, LParam(sc, extended: extended));

        var e = KeyTranslation.FromKey(down, None, time: 1);

        Assert.Equal(keySym, e.KeySym);
        Assert.Equal(keyCode, e.KeyCode);
    }

    [Fact]
    public void A_release_is_its_press_with_the_release_flag()
    {
        var enter = KeyTranslation.FromKey(Down(VkReturn, 0x1C), None, time: 1);
        Assert.Equal(new KeyEvent(KeySym.Return, KeyCode.Enter, KeyState.None, IsRelease: true, Time: 1), enter.AsRelease());

        var at = KeyTranslation.FromChar(Char('@', Sc2), AltGr, time: 1)!.Value;
        Assert.Equal(new KeyEvent((uint)'@', at.KeyCode, KeyState.Ctrl | KeyState.Alt, IsRelease: true, Time: 1), at.AsRelease());
    }

    [Fact]
    public void A_modifier_key_up_translates_as_a_release()
    {
        var release = KeyTranslation.FromKey(Up(VkShift, 0x2A), None, time: 2);

        Assert.Equal(new KeyEvent(KeySym.ShiftL, KeyCode.LeftShift, KeyState.None, IsRelease: true, Time: 2), release);
    }

    [Fact]
    public void Only_a_modifier_release_is_translated_from_its_key_up()
    {
        // Ctrl+A pressed, Ctrl released first: the key-up of A no longer looks like a chord.
        // Its release is the stored press's AsRelease, never a fresh translation.
        Assert.Throws<InvalidOperationException>(() => KeyTranslation.FromKey(Up(VkA, ScA), None, time: 1));
        Assert.Throws<InvalidOperationException>(() => KeyTranslation.FromKey(Up(VkReturn, 0x1C), None, time: 1));
    }

    [Fact]
    public void A_printing_key_message_is_not_translated_at_the_keydown()
    {
        Assert.Throws<InvalidOperationException>(() => KeyTranslation.FromKey(Down(VkA, ScA), None, time: 1));
    }

    [Theory]
    [InlineData(0x0D, 0x1C)] // Enter
    [InlineData(0x1B, 0x01)] // Escape
    [InlineData(0x08, 0x0E)] // Backspace
    [InlineData(0x16, 0x2F)] // Ctrl+V (M0.3 trace: CHAR U+0016 sc=0x2F)
    public void A_control_char_translates_to_its_raw_code_point_and_is_the_Gates_to_pair_with_its_press(uint cp, int sc)
    {
        // M0.3 trace: KEYDOWN vk=0x0D then CHAR U+000D — the keydown is Fixed and asked; this char goes where it went.
        var e = KeyTranslation.FromChar(Char(cp, sc), None, time: 1);

        Assert.Equal(cp, e!.Value.KeySym);
        Assert.Equal(sc + 8u, e.Value.KeyCode);
    }

    // --- SkipsOutsideComposition ---

    [Theory]
    [InlineData(VkReturn, 0x1C, false)]
    [InlineData(VkEscape, 0x01, false)]
    [InlineData(VkTab, 0x0F, false)]
    [InlineData(VkBack, 0x0E, false)]
    [InlineData(VkDelete, 0x53, true)]
    [InlineData(VkLeft, 0x4B, true)]
    [InlineData(VkUp, 0x48, true)]
    [InlineData(VkHome, 0x47, true)]
    [InlineData(VkEnd, 0x4F, true)]
    [InlineData(VkPrior, 0x49, true)]
    [InlineData(VkF1, 0x3B, false)]
    [InlineData(VkF24, 0x76, false)]
    public void The_spec_list_passes_without_asking_outside_a_Composition(int vk, int sc, bool extended)
    {
        var down = new KeyMessage(WindowMessage.KeyDown, (nuint)vk, LParam(sc, extended: extended));

        Assert.True(KeyTranslation.SkipsOutsideComposition(down, None));
        Assert.True(KeyTranslation.SkipsOutsideComposition(down, new Modifiers { Shift = true }));
    }

    [Fact]
    public void A_listed_key_is_asked_when_Ctrl_or_Alt_is_held()
    {
        Assert.False(KeyTranslation.SkipsOutsideComposition(Down(VkReturn, 0x1C), Ctrl));
        Assert.False(KeyTranslation.SkipsOutsideComposition(Down(VkReturn, 0x1C), Alt));
        Assert.False(KeyTranslation.SkipsOutsideComposition(SysDown(VkReturn, 0x1C), None));
        Assert.False(KeyTranslation.SkipsOutsideComposition(Down(VkBack, 0x0E), AltGr));
    }

    [Fact]
    public void Text_keys_and_chords_are_always_asked()
    {
        Assert.False(KeyTranslation.SkipsOutsideComposition(Down(VkA, ScA), None));
        Assert.False(KeyTranslation.SkipsOutsideComposition(Down(VkSpace, 0x39), Ctrl));
        Assert.False(KeyTranslation.SkipsOutsideComposition(Down(0x2D /* VK_INSERT */, 0x52), None));
    }
}
