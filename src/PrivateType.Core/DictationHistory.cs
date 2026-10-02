namespace PrivateType.Core;

public static class DictationHistoryRetentions
{
    public const string UntilExit = "until-exit";
    public const string OneHour = "1h";
    public const string FifteenMinutes = "15m";
    public const string Off = "off";

    public static bool IsSupported(string? value) => value is UntilExit or OneHour or FifteenMinutes or Off;

    public static TimeSpan? MaximumAge(string value) => value switch
    {
        OneHour => TimeSpan.FromHours(1),
        FifteenMinutes => TimeSpan.FromMinutes(15),
        _ => null
    };
}

public sealed record DictationHistoryEntry(long Id, string Text, string LocaleCode, DateTimeOffset FinishedAt, bool Inserted);

// Recent dictations for pasting again with the history shortcut, newest first. Entries live only
// in memory: nothing is written to files, settings, or diagnostics, and the owner clears them on exit.
public sealed class DictationHistory(TimeProvider clock)
{
    public const int Capacity = 50;
    private readonly List<DictationHistoryEntry> entries = [];
    private string retention = DictationHistoryRetentions.UntilExit;
    private long nextId;

    public event Action? Changed;

    public bool IsEnabled => retention != DictationHistoryRetentions.Off;

    public string Retention
    {
        get => retention;
        set
        {
            if (!DictationHistoryRetentions.IsSupported(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported dictation history retention.");

            retention = value;
            if (!IsEnabled)
                Clear();
            else
                Prune();
        }
    }

    public void Add(FinalizedDictation dictation)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(dictation.Text))
            return;

        entries.Insert(0, new DictationHistoryEntry(++nextId, dictation.Text, dictation.LocaleCode, clock.GetUtcNow(), dictation.Inserted));
        if (entries.Count > Capacity)
            entries.RemoveRange(Capacity, entries.Count - Capacity);
        Prune();
        Changed?.Invoke();
    }

    // Expired entries are dropped before every read.
    public IReadOnlyList<DictationHistoryEntry> Snapshot()
    {
        Prune();
        return entries.ToArray();
    }

    public bool Remove(long id)
    {
        if (entries.RemoveAll(entry => entry.Id == id) == 0)
            return false;

        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (entries.Count == 0)
            return;

        entries.Clear();
        Changed?.Invoke();
    }

    // Also called on a timer by the owner, so expired text does not linger in memory unread.
    public void Prune()
    {
        if (DictationHistoryRetentions.MaximumAge(retention) is not { } maximumAge)
            return;

        var oldest = clock.GetUtcNow() - maximumAge;
        if (entries.RemoveAll(entry => entry.FinishedAt < oldest) > 0)
            Changed?.Invoke();
    }
}
