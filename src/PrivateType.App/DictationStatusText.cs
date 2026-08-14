using PrivateType.Core;

namespace PrivateType.App;

internal static class DictationStatusText
{
    public static string ForRecording(RecognitionLanguage language)
    {
        return language switch
        {
            RecognitionLanguage.English => "● Listening",
            RecognitionLanguage.Auto => "● Listening",
            _ => "● Słucham"
        };
    }
}
