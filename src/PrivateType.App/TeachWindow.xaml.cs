using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PrivateType.Core;

namespace PrivateType.App;

public sealed class TeachWord(int index, string text) : INotifyPropertyChanged
{
    private bool isSelected;
    private bool isFixed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; } = index;
    public string Text { get; } = text;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
                return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    // A word already covered by an added fix.
    public bool IsFixed
    {
        get => isFixed;
        set
        {
            if (isFixed == value)
                return;
            isFixed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFixed)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
        }
    }

    public bool IsAvailable => !isFixed;
}

public sealed class TeachFixRow(TeachFix fix)
{
    public TeachFix Fix { get; } = fix;
    public string Phrase => Fix.Entry.Phrase;
    public string Detail => $"Replaces “{Fix.Heard}” · {VocabularyScopes.Get(Fix.Entry.Scope).DisplayName}";
    public string RemoveName => $"Remove fix {Phrase}";
}

// Shows the one ephemeral sentence as word chips and collects several fixes for it. Each fix
// saves the typed spelling and the few words it replaces; the rest of the sentence is never saved.
public partial class TeachWindow : Window
{
    private readonly string sentence;
    private readonly IReadOnlyList<WordSpan> spans;
    private readonly IReadOnlyList<TeachWord> words;
    private readonly WordRangeSelection selection = new();
    private readonly TeachFixList fixes = new();
    private readonly ObservableCollection<TeachFixRow> fixRows = [];
    private readonly Func<TaughtTerms, string?> save;
    private string lastPrefill = string.Empty;
    private string? savedScope;

    public TeachWindow(FinalizedDictation dictation, string strength, Func<TaughtTerms, string?> save)
    {
        InitializeComponent();
        sentence = dictation.Text;
        this.save = save;
        spans = WordSpans.Split(sentence);
        words = spans.Select((span, index) => new TeachWord(index, sentence.Substring(span.Start, span.Length))).ToArray();
        WordList.ItemsSource = words;
        FixList.ItemsSource = fixRows;
        ScopeBox.ItemsSource = VocabularyScopes.All;
        ScopeBox.SelectedValue = SuggestedScope(dictation.LocaleCode);
        StrengthText.Text = $"The phrases use your vocabulary strength ({strength switch
        {
            VocabularyStrengths.Low => "Low",
            VocabularyStrengths.Strong => "Strong",
            _ => "Normal"
        }}) and help future dictations only.";
        UpdateState();
    }

    // Automatic has no base language, so it suggests Shared.
    internal static string SuggestedScope(string localeCode) =>
        RecognitionLocaleCatalog.Get(localeCode).BaseLanguageCode ?? VocabularyScopes.Shared;

    // "Saved 2 terms to Polish and Shared across languages."
    internal static string SavedSummary(IReadOnlyList<VocabularyEntry> entries)
    {
        var scopes = entries.Select(entry => entry.Scope).Distinct().Select(scope => VocabularyScopes.Get(scope).DisplayName).ToArray();
        var names = scopes.Length == 1 ? scopes[0] : $"{string.Join(", ", scopes[..^1])} and {scopes[^1]}";
        return $"Saved {entries.Count} {(entries.Count == 1 ? "term" : "terms")} to {names}.";
    }

    internal IReadOnlyList<TeachWord> Words => words;

    internal IReadOnlyList<TeachFix> Fixes => fixes.Fixes;

    // Set when the user asks to see the saved terms; the owner opens that vocabulary language.
    public string? VocabularyScopeToOpen { get; private set; }

    internal void ActivateWord(int index)
    {
        selection.Activate(index);
        SelectionChanged();
    }

    internal void ExtendSelection(int index)
    {
        selection.ExtendTo(index);
        SelectionChanged();
    }

    private void ActivateWord(object sender, RoutedEventArgs e) => ActivateWord(((TeachWord)((FrameworkElement)sender).Tag).Index);

    // Space activates a chip (the toggle's own click); Shift+Left/Right extends the range.
    private void WordKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 || e.Key is not (Key.Left or Key.Right))
            return;

        var word = (TeachWord)((FrameworkElement)sender).Tag;
        var target = Math.Clamp(word.Index + (e.Key == Key.Right ? 1 : -1), 0, words.Count - 1);
        if (selection.Range is null)
            selection.Activate(word.Index);
        ExtendSelection(target);
        FocusWord(target);
        e.Handled = true;
    }

    // Escape clears a selection first; with nothing selected it closes the window.
    private void HandleKeys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (selection.Range is not null && EditPanel.Visibility == Visibility.Visible)
        {
            selection.Clear();
            SelectionChanged();
        }
        else
        {
            Close();
        }
        e.Handled = true;
    }

    // Enter in the spelling box adds the fix, so several can be entered without the mouse.
    private void DesiredKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CanAddCurrent)
        {
            AddCurrentFix();
            e.Handled = true;
        }
    }

    private void SelectionChanged()
    {
        var range = selection.Range;
        foreach (var word in words)
            word.IsSelected = !word.IsFixed && range is { } selected && word.Index >= selected.First && word.Index <= selected.Last;

        // Prefill with the heard words until the user types their own correction.
        var heard = HeardText();
        if (DesiredBox.Text == lastPrefill)
        {
            DesiredBox.Text = heard;
            lastPrefill = heard;
        }
        UpdateState();
    }

    private string HeardText() => selection.SelectedText(sentence, spans).Trim().TrimEnd('.', ',', '!', '?', ';', ':');

    private void DesiredChanged(object sender, RoutedEventArgs e)
    {
        if (SaveButton is not null)
            UpdateState();
    }

    private void ScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SaveButton is not null)
            UpdateState();
    }

    private bool SelectionOverlapsFix => selection.Range is { } range && fixes.Overlaps(range.First, range.Last);

    private bool CanAddCurrent
    {
        get
        {
            var desired = VocabularyRules.Normalize(DesiredBox.Text);
            return selection.Range is not null && !SelectionOverlapsFix && ScopeBox.SelectedValue is string
                && desired.Length > 0 && VocabularyRules.ValidatePhrase(desired) is null;
        }
    }

    private void UpdateState()
    {
        var canAdd = CanAddCurrent;
        AddFixButton.IsEnabled = canAdd;
        var count = fixes.Entries.Count + (canAdd ? 1 : 0);
        SaveButton.IsEnabled = count > 0;
        SaveButton.Content = count > 1 ? $"Save {count} terms" : "Save term";
        SelectionHint.Text = SelectionOverlapsFix
            ? "The selection includes words you already fixed. Remove that fix or select other words."
            : string.Empty;
        FixesPanel.Visibility = fixRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddCurrentFix()
    {
        if (!CanAddCurrent || selection.Range is not { } range || ScopeBox.SelectedValue is not string scope)
            return;

        var fix = new TeachFix(range.First, range.Last, new VocabularyEntry(VocabularyRules.Normalize(DesiredBox.Text), scope), HeardText());
        if (!fixes.TryAdd(fix))
            return;

        fixRows.Insert(fixes.Fixes.ToList().IndexOf(fix), new TeachFixRow(fix));
        MarkFixedWords();
        selection.Clear();
        lastPrefill = string.Empty;
        DesiredBox.Text = string.Empty;
        ErrorText.Text = string.Empty;
        SelectionChanged();
    }

    private void AddFix(object sender, RoutedEventArgs e) => AddCurrentFix();

    private void RemoveFix(object sender, RoutedEventArgs e)
    {
        var row = (TeachFixRow)((FrameworkElement)sender).Tag;
        fixes.Remove(row.Fix);
        fixRows.Remove(row);
        MarkFixedWords();
        SelectionChanged();
    }

    private void MarkFixedWords()
    {
        foreach (var word in words)
            word.IsFixed = fixes.Covers(word.Index);
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled)
            return;

        // A typed correction that was not added yet is saved too.
        AddCurrentFix();
        var taught = fixes.ToTaughtTerms();
        if (taught.Entries.Count == 0)
            return;

        if (save(taught) is { } error)
        {
            // Keep the dialog, the sentence, and the fixes so the user can retry.
            ErrorText.Text = error;
            return;
        }

        ShowSaved(taught.Entries);
    }

    private void ShowSaved(IReadOnlyList<VocabularyEntry> entries)
    {
        savedScope = entries[0].Scope;
        SavedText.Text = SavedSummary(entries);
        EditPanel.Visibility = Visibility.Collapsed;
        SavedPanel.Visibility = Visibility.Visible;
        SaveButton.IsDefault = false;
        DoneButton.IsDefault = true;
        DoneButton.Focus();
    }

    private void OpenVocabulary(object sender, RoutedEventArgs e)
    {
        VocabularyScopeToOpen = savedScope;
        DialogResult = true;
    }

    private void Done(object sender, RoutedEventArgs e) => DialogResult = true;

    private void FocusWord(int index)
    {
        if (WordList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container
            && FindToggle(container) is { } toggle)
            toggle.Focus();
    }

    private static ToggleButton? FindToggle(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is ToggleButton toggle)
                return toggle;
            if (FindToggle(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
