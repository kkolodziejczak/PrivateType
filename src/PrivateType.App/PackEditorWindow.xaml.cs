using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PrivateType.Core;

namespace PrivateType.App;

// Edits the local copy of one installed pack. The original file is never touched.
public partial class PackEditorWindow : Window
{
    private readonly VocabularyPackItem item;
    private readonly VocabularyEditor vocabulary;
    private readonly ObservableCollection<VocabularyPhraseEditor> rows;

    public PackEditorWindow(VocabularyPackItem item, VocabularyEditor vocabulary)
    {
        InitializeComponent();
        this.item = item;
        this.vocabulary = vocabulary;
        NameBox.Text = item.Name;
        ScopeBox.ItemsSource = VocabularyScopes.All;
        ScopeBox.SelectedValue = item.Scope;
        rows = new(item.Phrases.Select(phrase => new VocabularyPhraseEditor(item.Scope, phrase)));
        PhraseList.ItemsSource = rows;
    }

    public VocabularyPack? Result { get; private set; }

    private void AddPhrase(object sender, RoutedEventArgs e)
    {
        var row = new VocabularyPhraseEditor(item.Scope, string.Empty);
        rows.Add(row);
        Dispatcher.BeginInvoke(() =>
        {
            if (PhraseList.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement container)
            {
                container.BringIntoView();
                FindTextBox(container)?.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RemovePhrase(object sender, RoutedEventArgs e) => rows.Remove((VocabularyPhraseEditor)((FrameworkElement)sender).Tag);

    private void Save(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Give the pack a name.";
            return;
        }
        if (vocabulary.IsNameTaken(name, except: item))
        {
            ErrorText.Text = "A pack with this name is already installed. Choose another name.";
            return;
        }

        var phrases = rows.Where(row => !string.IsNullOrWhiteSpace(row.Phrase)).Select(row => VocabularyRules.Normalize(row.Phrase)).ToArray();
        if (phrases.Length == 0)
        {
            ErrorText.Text = "A pack needs at least one phrase. Remove the pack instead if it is no longer needed.";
            return;
        }

        var candidate = new VocabularyPack(name, (string)ScopeBox.SelectedValue, item.IsEnabled, phrases);
        if (vocabulary.ValidateReplacing(item, candidate) is { } error)
        {
            ErrorText.Text = error;
            return;
        }

        Result = candidate;
        DialogResult = true;
    }

    private static System.Windows.Controls.TextBox? FindTextBox(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is System.Windows.Controls.TextBox box)
                return box;
            if (FindTextBox(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
