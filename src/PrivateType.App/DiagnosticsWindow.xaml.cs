using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.IO;

namespace PrivateType.App;

public partial class DiagnosticsWindow : Window
{
    private readonly InMemoryDiagnostics diagnostics;
    public DiagnosticsWindow(InMemoryDiagnostics diagnostics) { InitializeComponent(); this.diagnostics = diagnostics; Refresh(); }
    private void Refresh()
    {
        var entries = diagnostics.Snapshot();
        Entries.ItemsSource = entries.Select(entry => new { Display = $"{entry.Timestamp:HH:mm:ss}  {entry.Severity,-11} {entry.EventName} {string.Join(" ", entry.Details.Select(pair => $"{pair.Key}={pair.Value}"))}" }).ToArray();
        Entries.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private string Report() => JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = DateTimeOffset.UtcNow, entries = diagnostics.Snapshot() }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
    private void CopyReport(object sender, RoutedEventArgs e) => System.Windows.Clipboard.SetText(Report());
    private void SaveReport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"privatetype-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json", Filter = "JSON report|*.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Report());
    }
    private void ClearReport(object sender, RoutedEventArgs e) { diagnostics.Clear(); Refresh(); }
    private void Close(object sender, RoutedEventArgs e) => Close();
}
