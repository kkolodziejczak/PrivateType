using PrivateType.Core;

namespace PrivateType.App;

internal enum DiagnosticSeverity { Information, Warning, Error }

internal sealed record DiagnosticEntry(DateTimeOffset Timestamp, DiagnosticSeverity Severity, string EventName, string SessionId, IReadOnlyDictionary<string, string> Details, string? ErrorType, string? ErrorCode);

public sealed class InMemoryDiagnostics : IDictationDiagnostics
{
    internal const int Capacity = 200;
    private readonly object gate = new();
    private readonly Queue<DiagnosticEntry> entries = new();

    public void Record(DictationDiagnostic diagnostic)
    {
        lock (gate)
        {
            if (entries.Count == Capacity)
                entries.Dequeue();
            entries.Enqueue(CreateEntry(diagnostic));
        }
    }

    internal IReadOnlyList<DiagnosticEntry> Snapshot()
    {
        lock (gate)
            return entries.Reverse().ToArray();
    }

    internal void Clear()
    {
        lock (gate)
            entries.Clear();
    }

    private static DiagnosticEntry CreateEntry(DictationDiagnostic diagnostic)
    {
        var details = diagnostic.Details
            .Where(pair => pair.Key is "language" or "phase" or "state" or "targetEligibility" or "characters" or "timeoutMilliseconds" or "minutes")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var severity = diagnostic.Error is not null ? DiagnosticSeverity.Error
            : diagnostic.EventName.Contains("skipped", StringComparison.Ordinal) || diagnostic.EventName.Contains("failed", StringComparison.Ordinal) ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Information;
        return new(diagnostic.Timestamp, severity, diagnostic.EventName, diagnostic.SessionId, details,
            diagnostic.Error?.GetType().Name, diagnostic.Error is null ? null : $"0x{diagnostic.Error.HResult:X8}");
    }
}
