using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class DictationHistoryTests
{
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Keeps_dictations_newest_first_with_their_insertion_outcome()
    {
        var history = new DictationHistory(clock);

        history.Add(new FinalizedDictation("first", "pl-PL"));
        clock.Advance(TimeSpan.FromMinutes(1));
        history.Add(new FinalizedDictation("second", "en-US") { Inserted = false });

        var entries = history.Snapshot();
        Assert.Equal(["second", "first"], entries.Select(entry => entry.Text));
        Assert.False(entries[0].Inserted);
        Assert.True(entries[1].Inserted);
        Assert.Equal("en-US", entries[0].LocaleCode);
        Assert.Equal(clock.GetUtcNow(), entries[0].FinishedAt);
    }

    [Fact]
    public void Ignores_blank_text()
    {
        var history = new DictationHistory(clock);

        history.Add(new FinalizedDictation("   ", "pl-PL"));

        Assert.Empty(history.Snapshot());
    }

    [Fact]
    public void Keeps_entries_until_exit_regardless_of_age()
    {
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("old", "pl-PL"));

        clock.Advance(TimeSpan.FromDays(3));

        Assert.Single(history.Snapshot());
    }

    [Theory]
    [InlineData(DictationHistoryRetentions.OneHour, 60)]
    [InlineData(DictationHistoryRetentions.FifteenMinutes, 15)]
    public void Drops_entries_older_than_the_retention(string retention, int minutes)
    {
        var history = new DictationHistory(clock) { Retention = retention };
        history.Add(new FinalizedDictation("expires", "pl-PL"));
        clock.Advance(TimeSpan.FromMinutes(minutes - 1));
        history.Add(new FinalizedDictation("stays", "pl-PL"));

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(["stays"], history.Snapshot().Select(entry => entry.Text));
    }

    [Fact]
    public void Shortening_the_retention_drops_expired_entries_immediately()
    {
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("two hours old", "pl-PL"));
        clock.Advance(TimeSpan.FromHours(2));

        history.Retention = DictationHistoryRetentions.OneHour;

        Assert.Empty(history.Snapshot());
    }

    [Fact]
    public void Turning_history_off_clears_it_and_stops_recording()
    {
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("kept", "pl-PL"));

        history.Retention = DictationHistoryRetentions.Off;
        history.Add(new FinalizedDictation("ignored", "pl-PL"));

        Assert.False(history.IsEnabled);
        Assert.Empty(history.Snapshot());
    }

    [Fact]
    public void Keeps_at_most_the_newest_entries_up_to_capacity()
    {
        var history = new DictationHistory(clock);

        for (var index = 0; index < DictationHistory.Capacity + 5; index++)
            history.Add(new FinalizedDictation($"entry {index}", "pl-PL"));

        var entries = history.Snapshot();
        Assert.Equal(DictationHistory.Capacity, entries.Count);
        Assert.Equal($"entry {DictationHistory.Capacity + 4}", entries[0].Text);
    }

    [Fact]
    public void Removes_one_entry_or_clears_all_and_reports_changes()
    {
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("first", "pl-PL"));
        history.Add(new FinalizedDictation("second", "pl-PL"));
        var changes = 0;
        history.Changed += () => changes++;

        Assert.True(history.Remove(history.Snapshot()[1].Id));
        Assert.False(history.Remove(12345));
        Assert.Equal(["second"], history.Snapshot().Select(entry => entry.Text));

        history.Clear();
        history.Clear();

        Assert.Empty(history.Snapshot());
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Rejects_an_unknown_retention()
    {
        var history = new DictationHistory(clock);

        Assert.Throws<ArgumentOutOfRangeException>(() => history.Retention = "forever");
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
