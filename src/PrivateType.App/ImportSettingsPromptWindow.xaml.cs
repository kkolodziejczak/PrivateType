using System.Windows;
using Input = System.Windows.Input;

namespace PrivateType.App;

// Asks once, on the first start of a new copy (or after choosing a folder in Settings), whether to
// copy the settings of an earlier copy. True means import.
public partial class ImportSettingsPromptWindow : Window
{
    internal ImportSettingsPromptWindow(EarlierCopy copy)
    {
        InitializeComponent();
        SourceText.Text = SourceSummary(copy);
    }

    internal static string SourceSummary(EarlierCopy copy) => $"Found {copy.Label} in {copy.Folder}.";

    private void Import(object sender, RoutedEventArgs e) => DialogResult = true;

    private void StartFresh(object sender, RoutedEventArgs e) => DialogResult = false;

    private void FocusImport(object sender, RoutedEventArgs e) => ImportButton.Focus();

    private void DragWindow(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == Input.MouseButtonState.Pressed)
            DragMove();
    }
}
