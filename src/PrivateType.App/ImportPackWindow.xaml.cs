using System.Windows;
using System.Windows.Input;
using PrivateType.Core;

namespace PrivateType.App;

// Previews a parsed pack file. Nothing is installed until Import succeeds validation.
public partial class ImportPackWindow : Window
{
    private readonly IReadOnlyList<string> phrases;
    private readonly VocabularyEditor vocabulary;

    public ImportPackWindow(string fileName, IReadOnlyList<string> phrases, VocabularyEditor vocabulary)
    {
        InitializeComponent();
        this.phrases = phrases;
        this.vocabulary = vocabulary;
        FileText.Text = $"File: {fileName}";
        NameBox.Text = vocabulary.SuggestName(VocabularyEditor.NameFromFile(fileName));
        ScopeBox.ItemsSource = VocabularyScopes.All;
        PhraseList.ItemsSource = phrases;
        CountText.Text = $"{phrases.Count} {(phrases.Count == 1 ? "phrase" : "phrases")}";
        UpdateState();
    }

    public VocabularyPack? Result { get; private set; }

    private void InputChanged(object sender, RoutedEventArgs e)
    {
        if (ImportButton is not null)
            UpdateState();
    }

    // The language has no default: a file carries no scope, so the user must choose one.
    private void UpdateState()
    {
        var name = NameBox.Text.Trim();
        ErrorText.Text = name.Length > 0 && vocabulary.IsNameTaken(name) ? "A pack with this name is already installed. Choose another name." : string.Empty;
        ImportButton.IsEnabled = name.Length > 0 && ScopeBox.SelectedValue is string && ErrorText.Text.Length == 0;
    }

    private void Import(object sender, RoutedEventArgs e)
    {
        if (ScopeBox.SelectedValue is not string scope)
            return;

        var candidate = new VocabularyPack(NameBox.Text.Trim(), scope, EnabledBox.IsChecked == true, phrases);
        if (vocabulary.ValidateAdding(candidate) is { } error)
        {
            ErrorText.Text = error;
            return;
        }

        Result = candidate;
        DialogResult = true;
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
