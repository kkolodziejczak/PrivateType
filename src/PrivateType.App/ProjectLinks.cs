using System.Diagnostics;

namespace PrivateType.App;

// The project's public pages, written once. Every screen that tells the person to get the complete portable ZIP
// (Licenses without its texts, setup without its engine) names the action in the same words and links here.
internal static class ProjectLinks
{
    public const string GetCompleteZipAction = "Get the complete ZIP";

    public static Uri Releases { get; } = new("https://github.com/kkolodziejczak/privatetype/releases");

    public static ProcessStartInfo CompleteZipBrowserStartInfo() => new(Releases.AbsoluteUri) { UseShellExecute = true };

    public static void GetCompleteZip() => Process.Start(CompleteZipBrowserStartInfo());
}

// Microsoft's pages the app sends the person to, written once beside the project's own.
internal static class SystemLinks
{
    public const string GetVisualCppRedistributableAction = "Get Visual C++ Redistributable";

    public static Uri VisualCppRedistributable { get; } = new("https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist");

    public static ProcessStartInfo VisualCppRedistributableBrowserStartInfo() => new(VisualCppRedistributable.AbsoluteUri) { UseShellExecute = true };

    public static void GetVisualCppRedistributable() => Process.Start(VisualCppRedistributableBrowserStartInfo());

    // Windows' microphone privacy page, and the words of the fix that sends the person there.
    public const string MicrophoneSettingsAction = "Microphone settings";
    public const string MicrophoneFix = "Check that a microphone is connected and that Windows lets apps use it, then try again.";

    public static Uri MicrophonePrivacy { get; } = new("ms-settings:privacy-microphone");

    public static ProcessStartInfo MicrophonePrivacyStartInfo() => new(MicrophonePrivacy.OriginalString) { UseShellExecute = true };

    public static void OpenMicrophoneSettings() => Process.Start(MicrophonePrivacyStartInfo());
}
