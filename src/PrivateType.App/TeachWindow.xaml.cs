using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PrivateType.Core;

namespace PrivateType.App;

public sealed class TeachWord(int index, string text) : INotifyPropertyChanged
{
    private bool isSelected;

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
}

// Shows the one ephemeral sentence as word chips. Only the typed correct spelling is saved;
// the heard words never leave this window.
public partial class TeachWindow : Window
{
    private readonly string sentence;
    private readonly IReadOnlyList<WordSpan> spans;
    private readonly IReadOnlyList<TeachWord> words;
    private readonly WordRangeSelection selection = new();
    private readonly Func<VocabularyEntry, string?> save;
    private string lastPrefill = string.Empty;

    public TeachWindow(FinalizedDictation dictation, string strength, Func<VocabularyEntry, string?> save)
    {
        InitializeComponent();
        sentence = dictation.Text;
        this.save = save;
        spans = WordSpans.Split(sentence);
        words = spans.Select((span, index) => new TeachWord(index, sentence.Substring(span.Start, span.Length))).ToArray();
        WordList.ItemsSource = words;
        ScopeBox.ItemsSource = VocabularyScopes.All;
        ScopeBox.SelectedValue = SuggestedScope(dictation.LocaleCode);
        StrengthText.Text = $"The phrase uses your vocabulary strength ({strength switch
        {
            VocabularyStrengths.Low => "Low",
            VocabularyStrengths.Strong => "Strong",
            _ => "Normal"
        }}) and helps future dictations only.";
        UpdateState();
    }

    // Automatic has no base language, so it suggests Shared.
    internal static string SuggestedScope(string localeCode) =>
        RecognitionLocaleCatalog.Get(localeCode).BaseLanguageCode ?? VocabularyScopes.Shared;

    internal IReadOnlyList<TeachWord> Words => words;

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

        if (selection.Range is not null)
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

    private void SelectionChanged()
    {
        var range = selection.Range;
        foreach (var word in words)
            word.IsSelected = range is { } selected && word.Index >= selected.First && word.Index <= selected.Last;

        // Prefill with the heard words until the user types their own correction.
        var heard = selection.SelectedText(sentence, spans).Trim().TrimEnd('.', ',', '!', '?', ';', ':');
        if (DesiredBox.Text == lastPrefill)
        {
            DesiredBox.Text = heard;
            lastPrefill = heard;
        }
        UpdateState();
    }

    private void DesiredChanged(object sender, RoutedEventArgs e)
    {
        if (SaveButton is not null)
            UpdateState();
    }

    private void UpdateState()
    {
        var desired = VocabularyRules.Normalize(DesiredBox.Text);
        SaveButton.IsEnabled = selection.Range is not null && desired.Length > 0 && VocabularyRules.ValidatePhrase(desired) is null;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled || ScopeBox.SelectedValue is not string scope)
            return;

        if (save(new VocabularyEntry(VocabularyRules.Normalize(DesiredBox.Text), scope)) is { } error)
        {
            // Keep the dialog and the sentence so the user can retry.
            ErrorText.Text = error;
            return;
        }

        DialogResult = true;
    }

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
