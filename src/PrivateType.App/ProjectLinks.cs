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
