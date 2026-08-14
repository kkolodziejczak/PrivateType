using System.Collections.ObjectModel;

namespace PrivateType.Core;

public sealed record DictationDiagnostic(
    string SessionId,
    DateTimeOffset Timestamp,
    string EventName,
    IReadOnlyDictionary<string, string> Details,
    Exception? Error = null);

public interface IDictationDiagnostics
{
    void Record(DictationDiagnostic diagnostic);
}

internal sealed class NullDictationDiagnostics : IDictationDiagnostics
{
    public static NullDictationDiagnostics Instance { get; } = new();

    public void Record(DictationDiagnostic diagnostic)
    {
    }
}
