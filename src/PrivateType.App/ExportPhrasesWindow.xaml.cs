using System.IO;
using System.Windows;
using System.Windows.Input;
using PrivateType.Core;

namespace PrivateType.App;

public sealed class ExportPhraseChoice(string phrase)
{
    public string Phrase { get; } = phrase;
    public bool IsSelected { get; set; } = true;
}

// Reviews exactly which phrases are written. Scope, name, and strength never leave the app.
public partial class ExportPhrasesWindow : Window
{
    private readonly IReadOnlyList<ExportPhraseChoice> choices;
    private readonly string defaultName;

    public ExportPhrasesWindow(string title, IReadOnlyList<string> phrases, string defaultName)
    {
        InitializeComponent();
        TitleText.Text = title;
        Title = title;
        this.defaultName = defaultName;
        choices = phrases.Order(StringComparer.CurrentCultureIgnoreCase).Select(phrase => new ExportPhraseChoice(phrase)).ToArray();
        PhraseList.ItemsSource = choices;
        UpdateSummary();
    }

    // Test seam: lets the layout probe write without a file picker.
    internal Func<string, string?> ChooseDestination { get; set; } = PickDestination;

    public IReadOnlyList<string> SelectedPhrases => choices.Where(choice => choice.IsSelected).Select(choice => choice.Phrase).ToArray();

    private void SelectionChanged(object sender, RoutedEventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        var count = SelectedPhrases.Count;
        SummaryText.Text = $"{count} {(count == 1 ? "phrase" : "phrases")} will be written. Language, name, and strength are not included.";
        SaveAsButton.IsEnabled = count > 0;
    }

    private void SaveAs(object sender, RoutedEventArgs e)
    {
        if (ChooseDestination(defaultName) is not { } path)
            return;

        try
        {
            VocabularyPackCodec.WriteAtomic(path, SelectedPhrases);
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorText.Text = "The file could not be written. Choose another location and try again.";
        }
    }

    private static string? PickDestination(string defaultName)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save vocabulary pack",
            FileName = defaultName + VocabularyPackCodec.Extension,
            Filter = $"PrivateType vocabulary pack (*{VocabularyPackCodec.Extension})|*{VocabularyPackCodec.Extension}",
            AddExtension = false,
            OverwritePrompt = true
        };
        return picker.ShowDialog() == true ? picker.FileName : null;
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
