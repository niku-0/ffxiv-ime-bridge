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

    /// <summary>
    /// The limits the splice checks: a non-zero handler value wins over the ULD
    /// data, and <c>GetInputMaxLength()</c> stands in for the character limit when
    /// both read 0, so that the check can fire before <c>SetText</c> gets to
    /// truncate. In-game the Chat Box reads MaxByte=500 everywhere and MaxChar=0
    /// (ticket 04): its limit is 500 bytes.
    /// </summary>
    public ChatBoxLimits Limits => new(
        HandlerMaxChar is > 0 and var chars ? chars : MaxChar > 0 ? (int)MaxChar : (int)InputMaxLength,
        HandlerMaxByte is > 0 and var bytes ? bytes : (int)MaxByte);

    /// <summary>
    /// The Cursor as the game counts it, in code points: the input module's
    /// while the Chat Box is its target (it is focused), else the component's
    /// own field. Both are live and agree when targeted (ticket 04).
    /// </summary>
    public int CursorIndex => IsModuleTarget ? ModuleCursor : ComponentCursor;

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
