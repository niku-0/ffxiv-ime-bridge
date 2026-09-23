namespace FfxivImeBridge.NativeWrite;

/// <summary>
/// Words the refusal of a Commit that would push the Chat Box past the game's
/// limit. The Chat Box's limit is 500 bytes (ticket 04), which means nothing to
/// the user, so the byte count comes with an approximate character count at
/// 3 bytes per kana or kanji, rounded up: the least they would have to remove.
/// </summary>
internal static class Overflow
{
    private const int BytesPerKana = 3;

    public static string Describe(SplicePlan plan, ChatBoxLimits limits)
    {
        var parts = new List<string>(2);
        if (plan.CharsOver > 0) parts.Add($"{Count(plan.CharsOver, "character")} over the limit of {limits.MaxChars} characters");
        if (plan.BytesOver > 0)
        {
            var about = (plan.BytesOver + BytesPerKana - 1) / BytesPerKana;
            parts.Add($"{Count(plan.BytesOver, "byte")} (about {Count(about, "character")}) over the limit of {limits.MaxBytes} bytes");
        }
        return string.Join(", ", parts);
    }

    /// <summary>"1 byte" / "2 bytes"; the write report's count of dropped control characters shares it.</summary>
    internal static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
