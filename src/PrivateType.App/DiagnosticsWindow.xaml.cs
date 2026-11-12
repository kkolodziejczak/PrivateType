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

    // The panel holds the newest warning or error, whichever it is; the list holds every other entry, newest first.
    private void Refresh()
    {
        var rows = diagnostics.Snapshot().Select(entry => DiagnosticRow.From(entry, clock)).ToArray();
        var latest = rows.FirstOrDefault(row => row.IsProblem);

        Latest.DataContext = latest ?? (rows.Length == 0
            ? DiagnosticRow.Neutral("Nothing recorded yet", "If dictation goes wrong, come back here before you close PrivateType and save the report.")
            : DiagnosticRow.Neutral("No warnings or errors in the retained log",
                $"{Counted(rows.Length, "event")} recorded. The log keeps the latest {InMemoryDiagnostics.Capacity} and is cleared when PrivateType closes."));

        Entries.ItemsSource = rows.Where(row => row != latest).ToArray();
        EntriesLabel.Text = latest is null ? "RECORDED EVENTS" : "OTHER RECORDED EVENTS";
        Counts.Text = string.Join(" · ", new[]
        {
            $"{rows.Length} recorded",
            Counted(rows.Count(row => row.Severity == DiagnosticSeverity.Error), "error"),
            Counted(rows.Count(row => row.Severity == DiagnosticSeverity.Warning), "warning"),
        }.Where(part => !part.StartsWith("0 ", StringComparison.Ordinal)));

        var any = rows.Length > 0;
        EntriesRegion.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        RetentionNote.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = any;
        SaveButton.IsEnabled = any;
    }

    private static string Counted(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

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

// One recorded entry as the window shows it: the severity, the time on the person's clock, a plain title and maybe a
// next step, and the raw event with its details (and, for an error, its type and code) as secondary detail.
internal sealed record DiagnosticRow(DiagnosticSeverity Severity, string Time, string Title, string? NextStep, string Raw, string SpokenName)
{
    public bool IsProblem => Severity != DiagnosticSeverity.Information;
    // The panel's word and time ("Warning · 13:58"), and the word an error row carries before its title.
    public string Word => IsProblem ? Severity.ToString() : "";
    public string ShortTime => Time.Length == 0 ? "" : $"· {Time[..5]}";
    public string ErrorWord => Severity == DiagnosticSeverity.Error ? "Error  " : "";

    internal static DiagnosticRow From(DiagnosticEntry entry, TimeZoneInfo clock)
    {
        var time = TimeZoneInfo.ConvertTime(entry.Timestamp, clock);
        var (title, nextStep) = DiagnosticTitles.Describe(entry);
        string[] error = [.. new[] { entry.ErrorType, entry.ErrorCode }.OfType<string>()];
        string[] raw = [entry.EventName, .. entry.Details.Select(pair => $"{pair.Key}={pair.Value}"), .. error];
        // What a screen reader says for the row: "Warning, 13:58, No words were recognized, so nothing was typed, injection.skipped reason=empty-transcript".
        string[] spoken = [entry.Severity.ToString(), $"{time:HH:mm}", title, string.Join(" ", raw)];
        return new(entry.Severity, $"{time:HH:mm:ss}", title, nextStep, string.Join(" ", raw), string.Join(", ", spoken));
    }

    // The panel when there is no problem to show: no time and no raw line.
    internal static DiagnosticRow Neutral(string title, string line) => new(DiagnosticSeverity.Information, "", title, line, "", title);
}
