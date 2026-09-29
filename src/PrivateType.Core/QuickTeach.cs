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

// Clicking words builds one contiguous range: a word outside the range grows it to that word
// (so clicking each word of a phrase, or just its first and last, both work), an end word
// shrinks it, and a word in the middle starts over there. Keyboard extension moves the range
// end while keeping the anchor.
public sealed class WordRangeSelection
{
    private int? anchor;
    private int end;

    public (int First, int Last)? Range => anchor is { } start ? (Math.Min(start, end), Math.Max(start, end)) : null;

    public void Activate(int index)
    {
        if (Range is not { } range)
        {
            Select(index, index);
        }
        else if (index < range.First)
        {
            Select(range.Last, index);
        }
        else if (index > range.Last)
        {
            Select(range.First, index);
        }
        else if (range.First == range.Last)
        {
            Clear();
        }
        else if (index == range.First)
        {
            Select(range.Last, index + 1);
        }
        else if (index == range.Last)
        {
            Select(range.First, index - 1);
        }
        else
        {
            Select(index, index);
        }
    }

    public void ExtendTo(int index)
    {
        anchor ??= index;
        end = index;
    }

    public void Clear() => anchor = null;

    private void Select(int fixedEnd, int movingEnd)
    {
        anchor = fixedEnd;
        end = movingEnd;
    }

    public string SelectedText(string text, IReadOnlyList<WordSpan> spans) =>
        Range is { } range ? text[spans[range.First].Start..spans[range.Last].End] : string.Empty;
}

// One correction in the teach dialog: the word range it replaces and the phrase to save.
public sealed record TeachFix(int First, int Last, VocabularyEntry Entry);

// The corrections for one sentence, in sentence order. Two fixes never share a word.
public sealed class TeachFixList
{
    private readonly List<TeachFix> fixes = [];

    public IReadOnlyList<TeachFix> Fixes => fixes;

    public bool Covers(int index) => fixes.Any(fix => index >= fix.First && index <= fix.Last);

    public bool Overlaps(int first, int last) => fixes.Any(fix => first <= fix.Last && last >= fix.First);

    public bool TryAdd(TeachFix fix)
    {
        if (Overlaps(fix.First, fix.Last))
            return false;

        fixes.Add(fix);
        fixes.Sort((left, right) => left.First.CompareTo(right.First));
        return true;
    }

    public void Remove(TeachFix fix) => fixes.Remove(fix);

    // The same phrase fixed twice in one sentence is saved once.
    public IReadOnlyList<VocabularyEntry> Entries => fixes.Select(fix => fix.Entry).Distinct().ToArray();
}

public static class TaughtVocabulary
{
    // Appends taught phrases that are not already in the vocabulary, keeping the existing order.
    public static IReadOnlyList<VocabularyEntry> Merge(IReadOnlyList<VocabularyEntry> existing, IEnumerable<VocabularyEntry> taught) =>
        [.. existing, .. taught.Distinct().Where(entry => !existing.Contains(entry))];
}
