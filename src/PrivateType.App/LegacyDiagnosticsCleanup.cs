using PrivateType.Core;
using System.IO;

namespace PrivateType.App;

internal static class LegacyDiagnosticsCleanup
{
    internal static void DeleteKnownLogs(string dataDirectory, IDictationDiagnostics diagnostics)
    {
        var directory = Path.Combine(dataDirectory, "logs");
        foreach (var suffix in new[] { string.Empty, ".1", ".2", ".3" })
        {
            try { var path = Path.Combine(directory, $"live-dictation.log{suffix}"); if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { diagnostics.Record(new DictationDiagnostic("application", DateTimeOffset.UtcNow, "diagnostics.legacy-cleanup.failed", new Dictionary<string, string>(), exception)); }
        }
        try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
        catch (Exception exception) { diagnostics.Record(new DictationDiagnostic("application", DateTimeOffset.UtcNow, "diagnostics.legacy-cleanup.failed", new Dictionary<string, string>(), exception)); }
    }
}
