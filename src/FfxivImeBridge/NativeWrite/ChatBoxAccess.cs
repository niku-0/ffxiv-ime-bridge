using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FfxivImeBridge.Rendering;

namespace FfxivImeBridge.NativeWrite;

/// <summary>
/// Reads and writes the ChatLog addon's text input through FFXIVClientStructs.
/// Main thread only. Every field touched is listed in ticket 04's answer, which
/// is also where what each means was settled: the cursor is in code points and
/// live on both the component and the input module.
/// </summary>
internal static unsafe class ChatBoxAccess
{
    private const string ChatLogAddon = "ChatLog";

    /// <summary><c>AddonChatLog.TextInput</c>: the Chat Box, focused or not. Null while the addon is not loaded.</summary>
    public static AtkComponentTextInput* Find(IGameGui gui)
    {
        var addon = gui.GetAddonByName<AddonChatLog>(ChatLogAddon);
        return addon == null ? null : addon->TextInput;
    }

    /// <summary>The Chat Box holds no text (Slash Bypass starts on a <c>/</c> here). False while the addon is not loaded.</summary>
    public static bool IsEmpty(IGameGui gui)
    {
        var input = Find(gui);
        return input != null && input->RawString.AsSpan().IsEmpty;
    }

    /// <summary>The Chat Box's text node on screen (the Indicator's fallback anchor), or null while the addon is not loaded.</summary>
    public static ScreenBox? TextNodeBox(IGameGui gui)
    {
        var input = Find(gui);
        return input == null || input->AtkTextNode == null ? null : Box(&input->AtkTextNode->AtkResNode);
    }

    /// <summary>
    /// The channel label (<c>Say</c>, <c>Party</c>, …) at the top-left of the
    /// input frame, whose leading gap the game's own input-mode badge occupies:
    /// the Indicator takes its vertical place, size and colours (ticket 14). Null
    /// while the addon is not loaded or the node is missing.
    /// </summary>
    public static ChannelLabel? ChannelLabel(IGameGui gui)
    {
        var addon = gui.GetAddonByName<AddonChatLog>(ChatLogAddon);
        if (addon == null || addon->CurrentChannelTextNode == null) return null;
        var text = addon->CurrentChannelTextNode;
        return new ChannelLabel(Box(&text->AtkResNode)!.Value, text->FontSize, AccumulatedScaleY(&text->AtkResNode), text->TextColor.RGBA, text->EdgeColor.RGBA);
    }

    /// <summary>The text input's font and IME colours (ticket 14), or null while the addon is not loaded.</summary>
    public static ChatBoxStyle? Style(IGameGui gui)
    {
        var input = Find(gui);
        return input == null ? null : Style(input);
    }

    private static ChatBoxStyle Style(AtkComponentTextInput* input)
    {
        var text = input->AtkTextNode;
        var handlerColour = input->HandlerValues.IMEColor;
        return new ChatBoxStyle(
            FontSizePt: text == null ? (byte)0 : text->FontSize,
            FontType: text == null ? (byte)0 : text->AlignmentFontType,
            Scale: text == null ? 1f : AccumulatedScaleY(&text->AtkResNode),
            ImeColor: input->ComponentTextData.IMEColor.RGBA,
            HandlerImeColor: IntValue(handlerColour) is { } v ? unchecked((uint)v) : null,
            CandidateColor: input->ComponentTextData.CandidateColor.RGBA);
    }

    /// <summary>
    /// The Cursor on screen, for the Preedit: the input's own cursor node (exact,
    /// follows horizontal scrolling — ticket 04), else the text node's left edge plus
    /// the drawn width of the text before the cursor (drifts once the text
    /// scrolls). The text line's top is the text node's, which the Preedit's
    /// baseline follows (ticket 15); the cursor node's when there is no text
    /// node. Null while the addon is not loaded or neither node exists.
    /// </summary>
    public static CursorAnchor? CursorAnchor(IGameGui gui)
    {
        var input = Find(gui);
        if (input == null) return null;

        var text = input->AtkTextNode;
        var cursorNode = input->CursorContainer;
        if (cursorNode != null)
        {
            var scale = AccumulatedScaleY(cursorNode);
            var textTop = text == null ? cursorNode->ScreenY : text->AtkResNode.ScreenY;
            return new CursorAnchor(cursorNode->ScreenX, cursorNode->ScreenY, cursorNode->Height * scale, scale, textTop);
        }

        if (text == null) return null;
        var node = &text->AtkResNode;
        var raw = input->RawString.AsSpan();
        var offset = CursorIndex.ToByteOffset(raw, CursorIndexOf(input)) ?? raw.Length;
        var textScale = AccumulatedScaleY(node);
        return new CursorAnchor(node->ScreenX + MeasureWidth(text, raw[..offset]), node->ScreenY, node->Height * textScale, textScale, node->ScreenY);
    }

    /// <summary>The live cursor index in code points (ticket 04): the module's while the Chat Box is its target, else the component's.</summary>
    private static int CursorIndexOf(AtkComponentTextInput* input)
    {
        var module = Module();
        return IsModuleTarget(module, input) ? module->CursorPos : input->CursorPos;
    }

    /// <summary>The node's own scale times every ancestor's: what its drawn height is in screen pixels per unit of <c>Height</c>.</summary>
    private static float AccumulatedScaleY(AtkResNode* node)
    {
        var scale = 1f;
        for (var n = node; n != null; n = n->ParentNode) scale *= n->ScaleY;
        return scale;
    }

    public static ChatBoxState? Read(IGameGui gui)
    {
        var input = Find(gui);
        return input == null ? null : Read(input);
    }

    public static ChatBoxState Read(AtkComponentTextInput* input)
    {
        var raw = input->RawString.AsSpan().ToArray();
        var module = Module();
        var isTarget = IsModuleTarget(module, input);
        var data = input->ComponentTextData;

        var textNode = input->AtkTextNode == null ? null : Box(&input->AtkTextNode->AtkResNode);
        var cursorNode = Box(input->CursorContainer);

        var state = new ChatBoxState(
            RawText: raw,
            EvaluatedText: input->EvaluatedString.ToString(),
            IsActive: input->IsActive,
            ComponentCursor: input->CursorPos,
            ComponentSelectionStart: input->SelectionStart,
            ComponentSelectionEnd: input->SelectionEnd,
            IsModuleTarget: isTarget,
            ModuleCursor: module->CursorPos,
            ModuleTextLength: module->TextLength,
            ModuleSelectionStart: module->SelectionStart,
            ModuleSelectionEnd: module->SelectionEnd,
            ModuleBeforeSelectionBytes: module->RawTextBeforeSelection.Length,
            ModuleSelectedBytes: module->RawSelectedText.Length,
            ModuleAfterSelectionBytes: module->RawTextAfterSelection.Length,
            ModuleInputBytes: module->RawInputString.Length,
            InputMaxLength: input->GetInputMaxLength(),
            MaxChar: data.MaxChar,
            MaxByte: data.MaxByte,
            MaxLine: data.MaxLine,
            MaxWidth: data.MaxWidth,
            HandlerMaxChar: IntValue(input->HandlerValues.MaxChar),
            HandlerMaxByte: IntValue(input->HandlerValues.MaxByte),
            TextNode: textNode,
            CursorNode: cursorNode,
            Style: Style(input),
            MeasuredCursorX: null,
            MeasureError: null,
            ModuleTexts: new ModuleStrings(
                module->RawTextBeforeSelection.ToString(), module->RawSelectedText.ToString(), module->RawTextAfterSelection.ToString(),
                module->RawInputString.ToString(), module->EvaluatedInputString.ToString()));

        // Cursor x = text node's left edge + the drawn width of the text before the cursor:
        // the Preedit's fallback anchor when the cursor node is missing.
        if (textNode is { } box && state.CursorByteOffset is { } offset)
        {
            try
            {
                state = state with { MeasuredCursorX = box.X + MeasureWidth(input->AtkTextNode, raw.AsSpan(0, offset)) };
            }
            catch (Exception ex)
            {
                state = state with { MeasureError = Describe(ex) };
            }
        }

        return state;
    }

    /// <summary>
    /// The game's own splice at its cursor, as the auto-translate menu uses it.
    /// Unlike <see cref="SetText"/> it refreshes the input module's copy of the
    /// text (<c>RawInputString</c> and the split around the selection), which is
    /// what the game hands back to the component when the Chat Box loses focus;
    /// it leaves the cursor where it was (ticket 04).
    /// </summary>
    public static void InsertText(AtkComponentTextInput* input, string text) => input->InsertText(text, unique: false);

    /// <summary>
    /// Replaces the whole text on the component only. The module's copy is not
    /// refreshed (ticket 04), so this is for a Chat Box whose cursor cannot be
    /// trusted, where the game's own splice would land who knows where.
    /// </summary>
    public static void SetText(AtkComponentTextInput* input, ReadOnlySpan<byte> utf8)
    {
        var buffer = new byte[utf8.Length + 1]; // NUL-terminated copy for the CStringPointer
        utf8.CopyTo(buffer);
        fixed (byte* p = buffer) input->SetText(p);
    }

    /// <summary>
    /// The input module's own notice to the component that the selection
    /// changed — the one virtual on <c>AtkTextInputEventInterface</c> besides
    /// <c>GetOwnerNode</c> — with the selection collapsed at
    /// <paramref name="index"/>. The only call that draws the cursor at the
    /// index: a bare cursor write never moves the cursor node, and
    /// <see cref="SetText"/> puts it at the end of the text (ticket 20). The
    /// component copies the struct's two strings into its raw and its evaluated
    /// string, and the evaluated one is what the game rebuilds the box from on
    /// the next focus-in — so both are the module's raw input string, which the
    /// game's splice has just refreshed. Not its evaluated string or
    /// <c>TextLength</c>: the splice leaves those as the old text plus the new
    /// (ticket 20, round 4), and the box came back as that, cut to the raw
    /// length. Raw and evaluated are the same for the text the splice accepts
    /// (no payloads). Only while the module targets the Chat Box.
    /// </summary>
    public static void NotifySelection(AtkComponentTextInput* input, int index)
    {
        var module = Module();
        if (!IsModuleTarget(module, input)) return;
        var raw = &module->RawInputString;
        var info = new AtkTextInput.TextSelectionInfo
        {
            CharactersAdded = 0,
            SelectionStart = checked((ushort)index),
            SelectionEnd = checked((ushort)index),
            StringLength = checked((ushort)CursorIndex.CountCodePoints(raw->AsSpan())),
            String1 = raw,
            String2 = raw,
        };
        input->UpdateTextSelection(&info);
    }

    /// <summary>
    /// Sets the cursor index (collapsing any selection) on the component and, if
    /// the Chat Box is the module's target, on the module's editing state too.
    /// Both hold past the next frame (ticket 04).
    /// </summary>
    public static void SetCursor(AtkComponentTextInput* input, int index)
    {
        input->CursorPos = index;
        input->SelectionStart = index;
        input->SelectionEnd = index;

        var module = Module();
        if (!IsModuleTarget(module, input)) return;
        var s = checked((short)index);
        module->CursorPos = s;
        module->SelectionStart = s;
        module->SelectionEnd = s;
    }

    public static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    /// <summary>The module's editing state (cursor, selection) is live for the input it targets (ticket 04).</summary>
    private static bool IsModuleTarget(AtkTextInput* module, AtkComponentTextInput* input) =>
        module->TargetTextInputEventInterface == &input->AtkTextInputEventInterface;

    /// <summary>The input module's editing state: cursor, selection and the text split around them for whichever input it targets.</summary>
    private static AtkTextInput* Module()
    {
        var atk = RaptureAtkModule.Instance();
        if (atk == null) throw new InvalidOperationException("RaptureAtkModule is not available");
        return &atk->AtkModule.TextInput;
    }

    private static float MeasureWidth(AtkTextNode* node, ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty) return 0f;
        var buffer = new byte[prefix.Length + 1];
        prefix.CopyTo(buffer);
        ushort width, height;
        fixed (byte* p = buffer) node->GetTextDrawSize(&width, &height, p, 0, prefix.Length, considerScale: true);
        return width;
    }

    private static ScreenBox? Box(AtkResNode* node)
    {
        if (node == null) return null;
        return new ScreenBox(node->ScreenX, node->ScreenY, node->Width * node->GetScaleX(), node->Height * node->GetScaleY(), node->IsVisible());
    }

    private static int? IntValue(in AtkValue value) => value.Type switch
    {
        AtkValueType.Int => value.Int,
        AtkValueType.UInt => (int)value.UInt,
        _ => null,
    };
}
