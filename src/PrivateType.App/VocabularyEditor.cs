using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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

public sealed class VocabularyCorrectionEditor(string scope, string heard, string phrase) : INotifyPropertyChanged
{
    private string heard = heard;
    private string phrase = phrase;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Scope { get; } = scope;

    public string Heard
    {
        get => heard;
        set
        {
            heard = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heard)));
        }
    }

    public string Phrase
    {
        get => phrase;
        set
        {
            phrase = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Phrase)));
        }
    }

    public bool IsBlank => string.IsNullOrWhiteSpace(heard) && string.IsNullOrWhiteSpace(phrase);
}

public sealed class VocabularyPackItem : INotifyPropertyChanged
{
    private VocabularyPack pack;

    public VocabularyPackItem(VocabularyPack pack) => this.pack = pack;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => pack.Name;

    public string Scope => pack.Scope;

    public IReadOnlyList<string> Phrases => pack.Phrases;

    public bool IsEnabled
    {
        get => pack.IsEnabled;
        set
        {
            pack = pack with { IsEnabled = value };
            Notify();
        }
    }

    public string Summary => $"{VocabularyScopes.Get(pack.Scope).DisplayName} · {pack.Phrases.Count} {(pack.Phrases.Count == 1 ? "phrase" : "phrases")}";

    public string ToggleName => $"Use pack {pack.Name}";

    public VocabularyPack ToPack() => pack;

    internal void Replace(VocabularyPack replacement)
    {
        pack = replacement;
        Notify(nameof(Name));
        Notify(nameof(Scope));
        Notify(nameof(Phrases));
        Notify(nameof(IsEnabled));
        Notify(nameof(Summary));
        Notify(nameof(ToggleName));
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

// A language in the Vocabulary picker, labelled with how many personal phrases it holds, so
// phrases saved under another language are easy to find.
public sealed class VocabularyScopeOption(VocabularyScope scope) : INotifyPropertyChanged
{
    private int count;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code => scope.Code;

    public string DisplayName => scope.DisplayName;

    public string Label => count > 0 ? $"{scope.DisplayName} ({count})" : scope.DisplayName;

    public int Count
    {
        get => count;
        set
        {
            if (count == value)
                return;
            count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }
}

// Edits the whole vocabulary while showing one scope at a time. Changing scope only filters;
// every scope's rows and every pack are kept until Save or Cancel.
public sealed class VocabularyEditor : INotifyPropertyChanged
{
    private readonly List<VocabularyPhraseEditor> all;
    private readonly List<VocabularyPackItem> packs;
    private readonly List<VocabularyCorrectionEditor> corrections;
    private string scope = VocabularyScopes.Shared;
    private string strength;
    private bool showAllPacks;

    public VocabularyEditor(
        IReadOnlyList<VocabularyEntry> entries,
        string strength,
        IReadOnlyList<VocabularyPack>? packs = null,
        IReadOnlyList<VocabularyCorrection>? corrections = null,
        bool correctAfterDictation = true)
    {
        all = entries.Select(entry => Track(new VocabularyPhraseEditor(entry.Scope, entry.Phrase))).ToList();
        this.packs = (packs ?? []).Select(pack => Track(new VocabularyPackItem(pack))).ToList();
        this.corrections = (corrections ?? []).Select(correction => new VocabularyCorrectionEditor(correction.Scope, correction.Heard, correction.Phrase)).ToList();
        this.strength = strength;
        CorrectAfterDictation = correctAfterDictation;
        Refresh();
    }

    public bool CorrectAfterDictation { get; set; }

    public ObservableCollection<VocabularyCorrectionEditor> VisibleCorrections { get; } = [];

    public bool HasNoVisibleCorrections => VisibleCorrections.Count == 0;

    public bool HasVisibleCorrections => VisibleCorrections.Count > 0;

    public string CorrectionsEmptyText => scope == VocabularyScopes.Shared
        ? "No shared corrections yet. Teach from last dictation adds them, or add one here."
        : $"No {VocabularyScopes.Get(scope).DisplayName} corrections yet. Teach from last dictation adds them, or add one here.";

    // Rows left completely blank are dropped; half-filled rows are kept so validation can explain them.
    public IReadOnlyList<VocabularyCorrection> Corrections =>
        corrections.Where(row => !row.IsBlank)
            .Select(row => new VocabularyCorrection(VocabularyRules.Normalize(row.Heard ?? string.Empty), VocabularyRules.Normalize(row.Phrase ?? string.Empty), row.Scope))
            .ToArray();

    public VocabularyCorrectionEditor AddCorrection()
    {
        var row = new VocabularyCorrectionEditor(scope, string.Empty, string.Empty);
        corrections.Add(row);
        VisibleCorrections.Add(row);
        NotifyCorrections();
        return row;
    }

    public void RemoveCorrection(VocabularyCorrectionEditor row)
    {
        corrections.Remove(row);
        VisibleCorrections.Remove(row);
        NotifyCorrections();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<VocabularyScopeOption> Scopes { get; } = VocabularyScopes.All.Select(scope => new VocabularyScopeOption(scope)).ToArray();

    public IReadOnlyList<ChoiceOption> Strengths { get; } =
    [
        new(VocabularyStrengths.Low, "Low"),
        new(VocabularyStrengths.Normal, "Normal"),
        new(VocabularyStrengths.Strong, "Strong")
    ];

    public ObservableCollection<VocabularyPhraseEditor> Visible { get; } = [];

    public ObservableCollection<VocabularyPackItem> VisiblePacks { get; } = [];

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

    public bool ShowAllPacks
    {
        get => showAllPacks;
        set
        {
            showAllPacks = value;
            Refresh();
            Notify();
        }
    }

    public bool IsEmpty => Visible.Count == 0;

    public bool HasVisiblePhrases => VisiblePhrases.Count > 0;

    public bool HasNoVisiblePacks => VisiblePacks.Count == 0;

    public string ScopeHint => scope == VocabularyScopes.Shared
        ? "Used with every recognition language, including Automatic."
        : $"Used with every {VocabularyScopes.Get(scope).DisplayName} shortcut, together with shared phrases.";

    public string EmptyText => scope == VocabularyScopes.Shared
        ? "No shared vocabulary yet. Add a word or phrase only when the model repeatedly gets it wrong."
        : $"No {VocabularyScopes.Get(scope).DisplayName} vocabulary yet. Shared phrases are already used with {VocabularyScopes.Get(scope).DisplayName} dictation.";

    public string PacksEmptyText => packs.Count == 0
        ? "No packs yet. Packs are files you download or receive, then import here. PrivateType never downloads them."
        : showAllPacks ? "No packs installed." : "No packs for this language. Turn on \"Show all packs\" to see the others.";

    // Usage of the selected language's per-dictation budget, plus the stored total.
    public string BudgetText
    {
        get
        {
            var baseLanguage = scope == VocabularyScopes.Shared ? null : scope;
            var used = VocabularyComposer.ComposeScope(Entries, Packs, baseLanguage).Count;
            var label = baseLanguage is null ? "Automatic" : VocabularyScopes.Get(baseLanguage).DisplayName;
            var stored = Entries.Count + packs.Sum(pack => pack.Phrases.Count);
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{label} dictation uses {used} of {VocabularyRules.MaximumEntries} phrases. {stored:N0} of {VocabularyRules.MaximumStoredPhrases:N0} stored.");
        }
    }

    // Rows left blank are dropped; everything else is normalized and validated as typed.
    public IReadOnlyList<VocabularyEntry> Entries =>
        all.Where(row => !string.IsNullOrWhiteSpace(row.Phrase))
            .Select(row => new VocabularyEntry(VocabularyRules.Normalize(row.Phrase), row.Scope))
            .ToArray();

    public IReadOnlyList<VocabularyPack> Packs => packs.Select(pack => pack.ToPack()).ToArray();

    public IReadOnlyList<string> VisiblePhrases =>
        Visible.Where(row => !string.IsNullOrWhiteSpace(row.Phrase))
            .Select(row => VocabularyRules.Normalize(row.Phrase))
            .Distinct(StringComparer.Ordinal)
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

    public string? ValidateAdding(VocabularyPack candidate) => VocabularyRules.Validate(Entries, [.. Packs, candidate]);

    public string? ValidateReplacing(VocabularyPackItem item, VocabularyPack candidate) =>
        VocabularyRules.Validate(Entries, packs.Select(pack => ReferenceEquals(pack, item) ? candidate : pack.ToPack()).ToArray());

    public bool IsNameTaken(string name, VocabularyPackItem? except = null) =>
        packs.Any(pack => !ReferenceEquals(pack, except) && string.Equals(pack.Name, name.Trim(), StringComparison.Ordinal));

    // "Software development", then "Software development (2)", and so on.
    public string SuggestName(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "Vocabulary pack" : baseName.Trim();
        if (!IsNameTaken(name))
            return name;
        for (var index = 2; ; index++)
        {
            var candidate = $"{name} ({index})";
            if (!IsNameTaken(candidate))
                return candidate;
        }
    }

    // "software-development.privatetype-vocabulary.json" -> "Software development".
    public static string NameFromFile(string path)
    {
        var name = Path.GetFileName(path);
        name = name.EndsWith(VocabularyPackCodec.Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^VocabularyPackCodec.Extension.Length]
            : Path.GetFileNameWithoutExtension(name);
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public void AddPack(VocabularyPack pack)
    {
        packs.Add(Track(new VocabularyPackItem(pack)));
        Refresh();
    }

    public void ReplacePack(VocabularyPackItem item, VocabularyPack replacement)
    {
        item.Replace(replacement);
        Refresh();
    }

    public void RemovePack(VocabularyPackItem item)
    {
        packs.Remove(item);
        Refresh();
    }

    private VocabularyPhraseEditor Track(VocabularyPhraseEditor row)
    {
        row.PropertyChanged += (_, _) => NotifyPhrases();
        return row;
    }

    private VocabularyPackItem Track(VocabularyPackItem item)
    {
        item.PropertyChanged += (_, _) => Notify(nameof(BudgetText));
        return item;
    }

    private void Refresh()
    {
        Visible.Clear();
        foreach (var row in all.Where(row => row.Scope == scope))
            Visible.Add(row);

        VisiblePacks.Clear();
        foreach (var pack in packs.Where(pack => showAllPacks || pack.Scope == scope).OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase))
            VisiblePacks.Add(pack);

        VisibleCorrections.Clear();
        foreach (var row in corrections.Where(row => row.Scope == scope))
            VisibleCorrections.Add(row);

        NotifyRows();
        Notify(nameof(HasNoVisiblePacks));
        Notify(nameof(PacksEmptyText));
        NotifyCorrections();
        Notify(nameof(CorrectionsEmptyText));
    }

    private void NotifyCorrections()
    {
        Notify(nameof(HasNoVisibleCorrections));
        Notify(nameof(HasVisibleCorrections));
    }

    private void NotifyRows()
    {
        Notify(nameof(IsEmpty));
        Notify(nameof(EmptyText));
        NotifyPhrases();
    }

    private void NotifyPhrases()
    {
        foreach (var option in Scopes)
            option.Count = all.Count(row => row.Scope == option.Code && !string.IsNullOrWhiteSpace(row.Phrase));
        Notify(nameof(BudgetText));
        Notify(nameof(HasVisiblePhrases));
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
