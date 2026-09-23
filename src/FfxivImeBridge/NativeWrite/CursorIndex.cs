namespace FfxivImeBridge.NativeWrite;

/// <summary>
/// Converts between the game's cursor index and UTF-8 byte offsets. The index
/// is in code points: with <c>aあ</c> and the cursor at the end it reads 2, not 4
/// (ticket 04, in-game).
/// </summary>
internal static class CursorIndex
{
    /// <summary>The byte offset the game's index denotes, or null if it does not point into the text.</summary>
    public static int? ToByteOffset(ReadOnlySpan<byte> text, int index)
    {
        if (index < 0) return null;
        var seen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (IsContinuation(text[i])) continue;
            if (seen == index) return i;
            seen++;
        }
        return seen == index ? text.Length : null;
    }

    /// <summary>The game's index for a byte offset that is on a code-point boundary.</summary>
    public static int FromByteOffset(ReadOnlySpan<byte> text, int byteOffset) => CountCodePoints(text[..byteOffset]);

    public static int CountCodePoints(ReadOnlySpan<byte> text)
    {
        var count = 0;
        foreach (var b in text)
            if (!IsContinuation(b)) count++;
        return count;
    }

    /// <summary>True if <paramref name="offset"/> is between code points (or at either end), so a splice there keeps the text valid UTF-8.</summary>
    public static bool IsBoundary(ReadOnlySpan<byte> text, int offset) =>
        offset >= 0 && offset <= text.Length && (offset == text.Length || !IsContinuation(text[offset]));

    private static bool IsContinuation(byte b) => (b & 0xC0) == 0x80;
}
