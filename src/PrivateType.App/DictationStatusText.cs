using PrivateType.Core;

namespace PrivateType.App;

internal static class DictationStatusText
{
    // The UI stays in English for every recognition locale.
    public static string ForRecording(string localeCode) => "● Listening";
}
