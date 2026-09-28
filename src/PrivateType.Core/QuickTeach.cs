namespace PrivateType.Core;

public sealed record FinalizedDictation(string Text, string LocaleCode);

// Holds only the most recent dictation, in memory, for "Teach from last dictation".
// It has no history, timestamps, files, or diagnostics; the owner clears it before the next
// dictation starts and whenever the teach dialog closes.
public sealed class EphemeralTranscriptBuffer
{
    public event Action? Changed;

    public FinalizedDictation? Current { get; private set; }

    public bool HasValue => Current is not null;

    public void Replace(FinalizedDictation value)
    {
        if (string.IsNullOrWhiteSpace(value.Text))
            return;

        Current = value;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Current is null)
            return;

        Current = null;
        Changed?.Invoke();
    }
}

public sealed record WordSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public static class WordSpans
{
    // Words are runs of non-whitespace, so punctuation stays attached to its word.
    public static IReadOnlyList<WordSpan> Split(string text)
    {
        var spans = new List<WordSpan>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
                index++;
            if (index > start)
                spans.Add(new WordSpan(start, index - start));
        }

        return spans;
    }
}

// First activation anchors one word; the next selects the contiguous range to it; a further
// activation starts over. Keyboard extension moves the range end while keeping the anchor.
public sealed class WordRangeSelection
{
    private int? anchor;
    private int end;
    private bool completed;

    public (int First, int Last)? Range => anchor is { } start ? (Math.Min(start, end), Math.Max(start, end)) : null;

    public void Activate(int index)
    {
        if (anchor is null || completed)
        {
            anchor = index;
            end = index;
            completed = false;
            return;
        }

        end = index;
        completed = true;
    }

    public void ExtendTo(int index)
    {
        anchor ??= index;
        end = index;
        completed = true;
    }

    public void Clear()
    {
        anchor = null;
        completed = false;
    }

    public string SelectedText(string text, IReadOnlyList<WordSpan> spans) =>
        Range is { } range ? text[spans[range.First].Start..spans[range.Last].End] : string.Empty;
}
