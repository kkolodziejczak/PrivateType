using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.IO;

namespace PrivateType.App;

public partial class DiagnosticsWindow : Window
{
    private readonly InMemoryDiagnostics diagnostics;
    public DiagnosticsWindow(InMemoryDiagnostics diagnostics) { InitializeComponent(); this.diagnostics = diagnostics; Refresh(); }
    private void Refresh() => Entries.ItemsSource = diagnostics.Snapshot().Select(entry => new { Display = $"{entry.Timestamp:HH:mm:ss}  {entry.Severity,-11} {entry.EventName} {string.Join(" ", entry.Details.Select(pair => $"{pair.Key}={pair.Value}"))}" }).DefaultIfEmpty(new { Display = "No diagnostics this session." }).ToArray();
    private string Report() => JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = DateTimeOffset.UtcNow, entries = diagnostics.Snapshot() }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
    private void CopyReport(object sender, RoutedEventArgs e) => System.Windows.Clipboard.SetText(Report());
    private void SaveReport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"live-dictation-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json", Filter = "JSON report|*.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Report());
    }
    private void ClearReport(object sender, RoutedEventArgs e) { diagnostics.Clear(); Refresh(); }
    private void Close(object sender, RoutedEventArgs e) => Close();
}
