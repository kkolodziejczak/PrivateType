using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PrivateType.App;

public partial class DiagnosticsWindow : Window
{
    internal const string CopyAction = "Copy report";
    internal const string CopiedWord = "Copied";
    // At rest every row is one line between hairlines, 37 DIP from hairline to hairline.
    internal const double RowPitch = 37;
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2);
    private readonly InMemoryDiagnostics diagnostics;
    private readonly TimeZoneInfo clock;
    private readonly DispatcherTimer copiedTimer;

    // Where the report goes; the capture replaces them, so it never touches the clipboard, opens a dialog or writes a file.
    internal Action<string> SetClipboard { get; init; } = System.Windows.Clipboard.SetText;
    internal Func<Window, string?> PickReportFile { get; init; } = AskWhereToSave;
    internal Action<string, string> WriteReport { get; init; } = File.WriteAllText;

    public DiagnosticsWindow(InMemoryDiagnostics diagnostics) : this(diagnostics, TimeZoneInfo.Local)
    {
    }

    // The list shows each entry's time on the given clock; the report keeps the timestamps as they were recorded.
    internal DiagnosticsWindow(InMemoryDiagnostics diagnostics, TimeZoneInfo clock)
    {
        InitializeComponent();
        this.diagnostics = diagnostics;
        this.clock = clock;
        copiedTimer = new DispatcherTimer { Interval = CopiedFor };
        copiedTimer.Tick += (_, _) => ShowCopied(false);
        Closed += (_, _) => copiedTimer.Stop();
        ShowCopied(false);
        Refresh();
        Loaded += (_, _) => FitListToWholeRows();
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
        Counts.Text = LogCounts(rows);

        var any = rows.Length > 0;
        EntriesRegion.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        RetentionNote.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = any;
        SaveButton.IsEnabled = any;
    }

    // The counts name the whole retained log, not the rows under them: "In the log: 4 events · 1 error · 3 warnings".
    internal static string LogCounts(IReadOnlyCollection<DiagnosticRow> rows) => "In the log: " + string.Join(" · ", new[]
    {
        Counted(rows.Count, "event"),
        Counted(rows.Count(row => row.Severity == DiagnosticSeverity.Error), "error"),
        Counted(rows.Count(row => row.Severity == DiagnosticSeverity.Warning), "warning"),
    }.Where(part => !part.StartsWith("0 ", StringComparison.Ordinal)));

    private static string Counted(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    // A long log shows whole rows only: the list is cut back to the last row that fits at the window's height, and a hairline
    // marks where it ends. A selected row opens taller and may still run under that edge.
    private void FitListToWholeRows()
    {
        Entries.ClearValue(HeightProperty);
        UpdateLayout();
        var scrolls = Descendants(Entries).OfType<ScrollViewer>().FirstOrDefault()?.ComputedVerticalScrollBarVisibility == Visibility.Visible;
        ListEdge.Visibility = scrolls ? Visibility.Visible : Visibility.Collapsed;
        if (scrolls)
            Entries.Height = Math.Max(RowPitch, Math.Floor(Entries.ActualHeight / RowPitch) * RowPitch);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private string Report() => JsonSerializer.Serialize(new { schemaVersion = 1, generatedAt = DateTimeOffset.UtcNow, entries = diagnostics.Snapshot() }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });

    // Copy says "Copied" on the key for two seconds; a clipboard another program holds is told in the footer instead of ending the app.
    private void CopyReport(object sender, RoutedEventArgs e)
    {
        try
        {
            SetClipboard(Report());
            ShowOutcome(null);
            ShowCopied(true);
        }
        catch (ExternalException exception)
        {
            ShowCopied(false);
            ShowOutcome($"Couldn't copy the report: {exception.Message}", failed: true);
        }
    }

    // Save names the file it wrote, or says why it couldn't, in the footer.
    private void SaveReport(object sender, RoutedEventArgs e)
    {
        if (PickReportFile(this) is not { } path)
            return;
        try
        {
            WriteReport(path, Report());
            ShowOutcome($"Saved {Path.GetFileName(path)}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowOutcome($"Couldn't save the report: {exception.Message}", failed: true);
        }
    }

    private static string? AskWhereToSave(Window owner)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"privatetype-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json", Filter = "JSON report|*.json" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private void ShowCopied(bool copied)
    {
        copiedTimer.Stop();
        CopyLabel.Visibility = copied ? Visibility.Hidden : Visibility.Visible;
        CopiedLabel.Visibility = copied ? Visibility.Visible : Visibility.Hidden;
        System.Windows.Automation.AutomationProperties.SetName(CopyButton, copied ? CopiedWord : CopyAction);
        if (!copied)
            return;
        copiedTimer.Start();
        UIElementAutomationPeer.CreatePeerForElement(CopyButton)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // The footer's one line about the last report action; a failure carries the warning mark.
    private void ShowOutcome(string? message, bool failed = false)
    {
        OutcomeText.Text = message ?? string.Empty;
        Outcome.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        OutcomeMark.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null)
            UIElementAutomationPeer.CreatePeerForElement(OutcomeText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void ClearReport(object sender, RoutedEventArgs e)
    {
        diagnostics.Clear();
        ShowCopied(false);
        ShowOutcome(null);
        Refresh();
        FitListToWholeRows();
    }
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
