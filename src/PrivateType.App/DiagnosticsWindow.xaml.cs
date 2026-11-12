using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.IO;

namespace PrivateType.App;

public partial class DiagnosticsWindow : Window
{
    private readonly InMemoryDiagnostics diagnostics;
    private readonly TimeZoneInfo clock;

    public DiagnosticsWindow(InMemoryDiagnostics diagnostics) : this(diagnostics, TimeZoneInfo.Local)
    {
    }

    // The list shows each entry's time on the given clock; the report keeps the timestamps as they were recorded.
    internal DiagnosticsWindow(InMemoryDiagnostics diagnostics, TimeZoneInfo clock)
    {
        InitializeComponent();
        this.diagnostics = diagnostics;
        this.clock = clock;
        Refresh();
    }

    private void Refresh()
    {
        var entries = diagnostics.Snapshot();
        Entries.ItemsSource = entries.Select(entry => DiagnosticRow.From(entry, clock)).ToArray();
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

    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
}

// One line of the list: the time on the person's clock, the level, and the event with its details.
internal sealed record DiagnosticRow(string Time, string Level, string Message, string SpokenName)
{
    internal static DiagnosticRow From(DiagnosticEntry entry, TimeZoneInfo clock)
    {
        var time = TimeZoneInfo.ConvertTime(entry.Timestamp, clock);
        var message = $"{entry.EventName} {string.Join(" ", entry.Details.Select(pair => $"{pair.Key}={pair.Value}"))}";
        // What a screen reader says for the row: "Warning, 11:58, cue skipped, source ready, reason muted".
        string[] spoken = [entry.Severity.ToString(), $"{time:HH:mm}", entry.EventName.Replace('.', ' '), .. entry.Details.Select(pair => $"{pair.Key} {pair.Value}")];
        return new($"{time:HH:mm:ss}", entry.Severity.ToString(), message, string.Join(", ", spoken));
    }
}
