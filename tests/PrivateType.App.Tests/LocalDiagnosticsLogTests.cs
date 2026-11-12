using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class LocalDiagnosticsLogTests
{
    [Fact]
    public void Keeps_only_sanitized_recent_diagnostics_in_memory()
    {
        var log = new InMemoryDiagnostics();
        log.Record(new DictationDiagnostic("session-1", DateTimeOffset.UtcNow, "session.failed", new Dictionary<string, string> { ["phase"] = "text.inject", ["secret"] = "dictated secret" }, new InvalidOperationException("C:\\Users\\secret")));
        for (var index = 0; index < 200; index++)
            log.Record(new DictationDiagnostic("session-1", DateTimeOffset.UtcNow, $"session.state.{index}", new Dictionary<string, string>()));

        var entries = log.Snapshot();
        Assert.Equal(InMemoryDiagnostics.Capacity, entries.Count);
        Assert.DoesNotContain(entries.SelectMany(entry => entry.Details.Values), value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.ErrorType?.Contains("C:\\Users", StringComparison.OrdinalIgnoreCase) == true);
        log.Clear();
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Keeps_the_timings_behind_the_transcribing_cue()
    {
        var log = new InMemoryDiagnostics();
        log.Record(new DictationDiagnostic("application", DateTimeOffset.UtcNow, "cue.transcribing", new Dictionary<string, string>
        {
            ["recordedSeconds"] = "21.4",
            ["predictedSeconds"] = "1.7",
            ["announced"] = "True"
        }));
        log.Record(new DictationDiagnostic("application", DateTimeOffset.UtcNow, "transcription.timed", new Dictionary<string, string>
        {
            ["recordedSeconds"] = "21.4",
            ["tookSeconds"] = "1.78"
        }));

        var entries = log.Snapshot();
        Assert.Equal(["recordedSeconds", "tookSeconds"], entries[0].Details.Keys.Order());
        Assert.Equal(["announced", "predictedSeconds", "recordedSeconds"], entries[1].Details.Keys.Order());
    }

    [Fact]
    public void Shows_an_entry_on_the_persons_clock_and_names_it_for_a_screen_reader()
    {
        var log = new InMemoryDiagnostics();
        log.Record(new DictationDiagnostic("session-1", new DateTimeOffset(2026, 10, 2, 11, 58, 7, TimeSpan.Zero), "injection.skipped", new Dictionary<string, string>
        {
            ["reason"] = "empty-transcript"
        }));
        var twoHoursAhead = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test");

        var row = DiagnosticRow.From(log.Snapshot().Single(), twoHoursAhead);

        Assert.Equal("13:58:07", row.Time);
        Assert.Equal(DiagnosticSeverity.Warning, row.Severity);
        Assert.Equal("No words were recognized, so nothing was typed", row.Title);
        Assert.Equal("injection.skipped reason=empty-transcript", row.Raw);
        Assert.Equal("Warning, 13:58, No words were recognized, so nothing was typed, injection.skipped reason=empty-transcript", row.SpokenName);
    }

    [Fact]
    public void Titles_an_unknown_event_by_its_name_and_keeps_an_errors_type_and_code_in_the_raw_line()
    {
        var log = new InMemoryDiagnostics();
        log.Record(new DictationDiagnostic("application", DateTimeOffset.UtcNow, "cue.skipped", new Dictionary<string, string> { ["source"] = "ready" }));
        log.Record(new DictationDiagnostic("e8802c", DateTimeOffset.UtcNow, "session.failed", new Dictionary<string, string> { ["phase"] = "capture.start" }, new InvalidOperationException("synthetic")));

        var rows = log.Snapshot().Select(entry => DiagnosticRow.From(entry, TimeZoneInfo.Utc)).ToArray();

        Assert.Equal("The microphone couldn't start recording", rows[0].Title);
        Assert.Equal(DiagnosticSeverity.Error, rows[0].Severity);
        Assert.Equal("session.failed phase=capture.start InvalidOperationException 0x80131509", rows[0].Raw);
        Assert.Equal("cue skipped", rows[1].Title);
        Assert.Null(rows[1].NextStep);
    }
}
