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
}
