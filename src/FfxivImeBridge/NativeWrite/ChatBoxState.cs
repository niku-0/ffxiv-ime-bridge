using System.Text;

namespace FfxivImeBridge.NativeWrite;

/// <summary>A node's on-screen box in game-window pixels, as the node itself reports it.</summary>
internal readonly record struct ScreenBox(float X, float Y, float Width, float Height, bool Visible)
{
    public override string ToString() => $"({X:0.#},{Y:0.#}) {Width:0.#}×{Height:0.#}{(Visible ? "" : " hidden")}";
}

/// <summary>The input module's own strings as text (ticket 20): the before/selected/after split its keystrokes edit at, its whole input string and the evaluated one.</summary>
internal sealed record ModuleStrings(string Before, string Selected, string After, string Input, string Evaluated)
{
    public override string ToString() => $"module strings: before=\"{Before}\" sel=\"{Selected}\" after=\"{After}\" input=\"{Input}\" evaluated=\"{Evaluated}\"";
}

/// <summary>What a Native Write reads before it plans: the Chat Box's text, the Cursor and the limits.</summary>
internal sealed record ChatBoxText(byte[] RawText, int CursorIndex, ChatBoxLimits Limits)
{
    /// <summary>The Cursor as a byte offset into <see cref="RawText"/>, or null if the index does not point into it.</summary>
    public int? CursorByteOffset => NativeWrite.CursorIndex.ToByteOffset(RawText, CursorIndex);
}

/// <summary>
/// Everything the Chat Box's text input says about itself at one instant, both
/// the component's own fields and the input module's editing state, so the
/// debug readout can show which of them are live.
/// </summary>
internal sealed record ChatBoxState(
    byte[] RawText,
    string EvaluatedText,
    bool IsActive,
    int ComponentCursor,
    int ComponentSelectionStart,
    int ComponentSelectionEnd,
    bool IsModuleTarget,
    short ModuleCursor,
    short ModuleTextLength,
    short ModuleSelectionStart,
    short ModuleSelectionEnd,
    int ModuleBeforeSelectionBytes,
    int ModuleSelectedBytes,
    int ModuleAfterSelectionBytes,
    int ModuleInputBytes,
    uint InputMaxLength,
    uint MaxChar,
    uint MaxByte,
    uint MaxLine,
    uint MaxWidth,
    int? HandlerMaxChar,
    int? HandlerMaxByte,
    ScreenBox? TextNode,
    ScreenBox? CursorNode,
    ChatBoxStyle Style,
    float? MeasuredCursorX,
    string? MeasureError,
    ModuleStrings? ModuleTexts = null)
{
    public string RawString => Encoding.UTF8.GetString(RawText);
    public int RawCodePoints => NativeWrite.CursorIndex.CountCodePoints(RawText);

    /// <summary>The limits the splice checks: see <see cref="ChatBoxLimits.Of"/>.</summary>
    public ChatBoxLimits Limits => ChatBoxLimits.Of(HandlerMaxChar, HandlerMaxByte, MaxChar, MaxByte, InputMaxLength);

    /// <summary>The Cursor in code points: see <see cref="NativeWrite.CursorIndex.Live"/>.</summary>
    public int CursorIndex => NativeWrite.CursorIndex.Live(IsModuleTarget, ModuleCursor, ComponentCursor);

    /// <summary>The Cursor as a byte offset into <see cref="RawText"/>, or null if the index does not point into it.</summary>
    public int? CursorByteOffset => NativeWrite.CursorIndex.ToByteOffset(RawText, CursorIndex);

    public string CursorSummary =>
        $"component cursor={ComponentCursor} sel={ComponentSelectionStart}..{ComponentSelectionEnd}; " +
        $"module{(IsModuleTarget ? "" : " (not targeting the Chat Box)")} cursor={ModuleCursor} len={ModuleTextLength} sel={ModuleSelectionStart}..{ModuleSelectionEnd} " +
        $"before/sel/after/input bytes={ModuleBeforeSelectionBytes}/{ModuleSelectedBytes}/{ModuleAfterSelectionBytes}/{ModuleInputBytes}";

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"raw: \"{RawString}\" ({RawText.Length} bytes, {RawCodePoints} code points) active={IsActive}");
        if (EvaluatedText != RawString) sb.AppendLine($"evaluated: \"{EvaluatedText}\"");
        sb.AppendLine(CursorSummary);
        if (ModuleTexts != null) sb.AppendLine(ModuleTexts.ToString());
        sb.AppendLine($"limits: GetInputMaxLength={InputMaxLength} uld maxChar={MaxChar} maxByte={MaxByte} maxLine={MaxLine} maxWidth={MaxWidth} handler maxChar={HandlerMaxChar?.ToString() ?? "-"} maxByte={HandlerMaxByte?.ToString() ?? "-"}");
        sb.Append($"text node {TextNode?.ToString() ?? "-"}; cursor node {CursorNode?.ToString() ?? "-"}; measured cursor x={MeasuredCursorX?.ToString("0.#") ?? "-"}");
        if (MeasureError != null) sb.Append($" (measure failed: {MeasureError})");
        sb.AppendLine();
        sb.Append($"style: {Style}; preedit ink={(Style.PreeditInk is null ? "white (game colour unreadable)" : $"game #{Style.ImeColorInUse:X8}")}");
        return sb.ToString();
    }
}
