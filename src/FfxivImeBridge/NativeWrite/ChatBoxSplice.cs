using System.Text;

namespace FfxivImeBridge.NativeWrite;

/// <summary>The game's length limits for a text input, as its ULD data states them. 0 = no limit on that axis.</summary>
internal readonly record struct ChatBoxLimits(int MaxChars, int MaxBytes);

/// <summary>
/// The outcome of planning a Native Write: either the Chat Box's new text and
/// where the Cursor lands (a byte offset into <see cref="Text"/>), or an Overflow, in which
/// case <see cref="Text"/> and <see cref="CursorByteOffset"/> are the unchanged
/// input and the counts say by how much the limit would have been exceeded.
/// <see cref="Committed"/> is the committed text as it goes in — what the
/// caller must write, since the plan may have dropped control characters from
/// what fcitx5 sent.
/// </summary>
internal sealed record SplicePlan(byte[] Text, int CursorByteOffset, string Committed, int ControlsDropped, int CharsOver, int BytesOver, bool AppendedAtEnd)
{
    public bool IsOverflow => CharsOver > 0 || BytesOver > 0;
}

/// <summary>
/// Pure splice of committed text into the Chat Box's UTF-8 text. Overflow is
/// decided here, before anything is written, and is never truncated (spec). An
/// unusable cursor (unknown, out of range, or inside a multi-byte sequence)
/// degrades to append-at-end, flagged so the caller can report it. So does a
/// Chat Box holding SeString payloads (auto-translate entries: <c>0x02 … 0x03</c>),
/// because the cursor index and the byte offsets no longer line up and a splice
/// could land inside one; M1 decides how to count them.
/// </summary>
internal static class ChatBoxSplice
{
    private const byte PayloadStart = 0x02;

    public static SplicePlan Plan(ReadOnlySpan<byte> current, int? cursorByteOffset, string committed, ChatBoxLimits limits)
    {
        var (text, controlsDropped) = WithoutControls(committed);
        var committedBytes = Encoding.UTF8.GetBytes(text);

        var charsOver = limits.MaxChars > 0
            ? Math.Max(0, CursorIndex.CountCodePoints(current) + CursorIndex.CountCodePoints(committedBytes) - limits.MaxChars)
            : 0;
        var bytesOver = limits.MaxBytes > 0
            ? Math.Max(0, current.Length + committedBytes.Length - limits.MaxBytes)
            : 0;

        var appendAtEnd = cursorByteOffset is not { } at || !CursorIndex.IsBoundary(current, at) || current.Contains(PayloadStart);
        var offset = appendAtEnd ? current.Length : cursorByteOffset!.Value;

        if (charsOver > 0 || bytesOver > 0)
            return new SplicePlan(current.ToArray(), offset, text, controlsDropped, charsOver, bytesOver, appendAtEnd);

        var spliced = new byte[current.Length + committedBytes.Length];
        current[..offset].CopyTo(spliced);
        committedBytes.CopyTo(spliced, offset);
        current[offset..].CopyTo(spliced.AsSpan(offset + committedBytes.Length));

        return new SplicePlan(spliced, offset + committedBytes.Length, text, controlsDropped, 0, 0, appendAtEnd);
    }

    /// <summary>
    /// fcitx5 can commit any string: a newline, any other C0 control, or
    /// <c>U+0002</c>, the byte a SeString payload starts with. The game's own
    /// <c>InsertText</c> sanitises as well, but the splice already refuses a
    /// buffer holding <c>0x02</c> and must not introduce one either (ticket 19).
    /// Controls are single UTF-16 units, so surrogate pairs pass through whole.
    /// </summary>
    private static (string Text, int Dropped) WithoutControls(string committed)
    {
        var dropped = committed.Count(IsControl);
        return dropped == 0 ? (committed, 0) : (string.Concat(committed.Where(c => !IsControl(c))), dropped);
    }

    private static bool IsControl(char c) => c < ' ' || c == '\u007F';
}
