using System.Collections.Immutable;

namespace FfxivImeBridge.Fcitx;

public readonly record struct TextSegment(string Text, TextFormat Format);

/// <summary>The not-yet-committed text, with the cursor as a UTF-8 byte offset (fcitx5's convention) and as a char index.</summary>
public sealed record Preedit(ImmutableArray<TextSegment> Segments, int CursorByteOffset)
{
    public static readonly Preedit Empty = new(ImmutableArray<TextSegment>.Empty, 0);

    public string Text { get; } = string.Concat(Segments.Select(s => s.Text));

    public bool IsEmpty => Text.Length == 0;

    /// <summary>Cursor as an index into <see cref="Text"/> (UTF-16 chars). -1 when fcitx5 reports no cursor.</summary>
    public int CursorIndex
    {
        get
        {
            if (CursorByteOffset < 0) return -1;
            var bytes = System.Text.Encoding.UTF8.GetBytes(Text);
            var offset = Math.Min(CursorByteOffset, bytes.Length);
            return System.Text.Encoding.UTF8.GetCharCount(bytes, 0, offset);
        }
    }
}

public readonly record struct Candidate(string Label, string Text);

/// <summary>fcitx5's <c>CandidateLayoutHint</c>.</summary>
public enum CandidateLayout
{
    NotSet = 0,
    Vertical = 1,
    Horizontal = 2,
}

/// <summary>
/// Everything fcitx5 wants drawn right now. Immutable; a new instance replaces
/// the old. <see cref="AuxUp"/> is Mozc's mode/hint line ("あ (Hiragana)",
/// "[Tabキーで選択]"); prediction candidates come with empty labels and
/// <see cref="SelectedCandidate"/> = -1 until the user starts cycling.
/// </summary>
public sealed record CompositionState(
    Preedit Preedit,
    ImmutableArray<TextSegment> AuxUp,
    ImmutableArray<TextSegment> AuxDown,
    ImmutableArray<Candidate> Candidates,
    int SelectedCandidate,
    CandidateLayout Layout,
    bool HasPreviousPage,
    bool HasNextPage)
{
    public static readonly CompositionState Idle = new(
        Preedit.Empty,
        ImmutableArray<TextSegment>.Empty,
        ImmutableArray<TextSegment>.Empty,
        ImmutableArray<Candidate>.Empty,
        -1,
        CandidateLayout.NotSet,
        false,
        false);

    /// <summary>True while fcitx5 owns typed keys: there is a preedit or a candidate list.</summary>
    public bool IsComposing => !Preedit.IsEmpty || Candidates.Length > 0;
}

public sealed record InputMethodInfo(string Name, string UniqueName, string LanguageCode);
