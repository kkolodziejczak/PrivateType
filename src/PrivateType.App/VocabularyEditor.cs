using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PrivateType.Core;

namespace PrivateType.App;

public sealed class VocabularyPhraseEditor(string scope, string phrase) : INotifyPropertyChanged
{
    private string phrase = phrase;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Scope { get; } = scope;

    public string Phrase
    {
        get => phrase;
        set
        {
            phrase = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Phrase)));
        }
    }
}

// Edits the whole vocabulary while showing one scope at a time. Changing scope only filters;
// every scope's rows are kept until Save or Cancel.
public sealed class VocabularyEditor : INotifyPropertyChanged
{
    private readonly List<VocabularyPhraseEditor> all;
    private string scope = VocabularyScopes.Shared;
    private string strength;

    public VocabularyEditor(IReadOnlyList<VocabularyEntry> entries, string strength)
    {
        all = entries.Select(entry => Track(new VocabularyPhraseEditor(entry.Scope, entry.Phrase))).ToList();
        this.strength = strength;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<VocabularyScope> Scopes => VocabularyScopes.All;

    public IReadOnlyList<ChoiceOption> Strengths { get; } =
    [
        new(VocabularyStrengths.Low, "Low"),
        new(VocabularyStrengths.Normal, "Normal"),
        new(VocabularyStrengths.Strong, "Strong")
    ];

    public ObservableCollection<VocabularyPhraseEditor> Visible { get; } = [];

    public string Scope
    {
        get => scope;
        set
        {
            scope = value;
            Refresh();
            Notify();
            Notify(nameof(ScopeHint));
        }
    }

    public string Strength
    {
        get => strength;
        set
        {
            strength = value;
            Notify();
        }
    }

    public bool IsEmpty => Visible.Count == 0;

    public string ScopeHint => scope == VocabularyScopes.Shared
        ? "Used with every recognition language, including Automatic."
        : $"Used with every {VocabularyScopes.Get(scope).DisplayName} shortcut, together with shared phrases.";

    public string EmptyText => scope == VocabularyScopes.Shared
        ? "No shared vocabulary yet. Add a word or phrase only when the model repeatedly gets it wrong."
        : $"No {VocabularyScopes.Get(scope).DisplayName} vocabulary yet. Shared phrases are already used with {VocabularyScopes.Get(scope).DisplayName} dictation.";

    public string CountText => $"{Entries.Count} of {VocabularyRules.MaximumEntries} phrases across all languages";

    // Rows left blank are dropped; everything else is normalized and validated as typed.
    public IReadOnlyList<VocabularyEntry> Entries =>
        all.Where(row => !string.IsNullOrWhiteSpace(row.Phrase))
            .Select(row => new VocabularyEntry(VocabularyRules.Normalize(row.Phrase), row.Scope))
            .ToArray();

    public VocabularyPhraseEditor Add()
    {
        var row = Track(new VocabularyPhraseEditor(scope, string.Empty));
        all.Add(row);
        Visible.Add(row);
        NotifyRows();
        return row;
    }

    public void Remove(VocabularyPhraseEditor row)
    {
        all.Remove(row);
        Visible.Remove(row);
        NotifyRows();
    }

    private VocabularyPhraseEditor Track(VocabularyPhraseEditor row)
    {
        row.PropertyChanged += (_, _) => Notify(nameof(CountText));
        return row;
    }

    private void Refresh()
    {
        Visible.Clear();
        foreach (var row in all.Where(row => row.Scope == scope))
            Visible.Add(row);
        NotifyRows();
    }

    private void NotifyRows()
    {
        Notify(nameof(IsEmpty));
        Notify(nameof(EmptyText));
        Notify(nameof(CountText));
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
