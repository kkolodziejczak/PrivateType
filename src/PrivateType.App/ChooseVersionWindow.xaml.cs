using System.Windows;
using System.Windows.Automation;
using Input = System.Windows.Input;

namespace PrivateType.App;

// Asks which copy to use when another copy is running, or when Windows starts a different copy. True means use this one.
public partial class ChooseVersionWindow : Window
{
    private const string Shared = "Settings and speech models are shared, so nothing needs moving.";

    // running: the other copy that runs now, if any. registeredVersion: the copy Windows starts, shown when using this
    // copy would take startup over. quitsCleanly: whether the running copy can be asked to quit rather than ended.
    internal ChooseVersionWindow(RunningCopy? running, Version? registeredVersion, bool claimsStartup, Version? currentVersion, bool quitsCleanly)
    {
        InitializeComponent();
        var other = running is null ? registeredVersion : running.Version;
        TitleBlock.Text = TitleText(currentVersion);
        RunningRow.Visibility = running is null ? Visibility.Collapsed : Visibility.Visible;
        RunningVersionText.Text = VersionLabel(running?.Version);
        StartupRow.Visibility = claimsStartup ? Visibility.Visible : Visibility.Collapsed;
        RegisteredVersionText.Text = VersionLabel(registeredVersion);
        CurrentVersionText.Text = VersionLabel(currentVersion);
        ExplanationBlock.Text = ExplanationText(other, running is not null, claimsStartup, currentVersion);
        ForcedCloseNotice.Visibility = running is not null && !quitsCleanly ? Visibility.Visible : Visibility.Collapsed;
        ForcedCloseBlock.Text = ForcedCloseText(running?.Version);
        KeepButton.Content = KeepText(other);
        UseButton.Content = UseText(currentVersion);
        AutomationProperties.SetName(CloseButton, KeepText(other));
    }

    internal static string VersionLabel(Version? version) => ApplicationVersion.Label(version);

    internal static string TitleText(Version? current) => current is null ? "Use this copy?" : $"Use {VersionLabel(current)}?";

    internal static string KeepText(Version? other) => other is null ? "Keep other copy" : $"Keep {ApplicationVersion.Number(other)}";

    internal static string UseText(Version? current) => current is null ? "Use this copy" : $"Use {ApplicationVersion.Number(current)}";

    internal static string ExplanationText(Version? other, bool runs, bool claimsStartup, Version? current)
    {
        if (!runs)
            return $"{Shared} You can run either version; this chooses the one that starts with Windows.";

        var self = current is null ? "this copy" : ApplicationVersion.Number(current);
        var closes = $"{Shared} Using {self} closes {CopyName(other, "the other copy")}";
        return claimsStartup ? $"{closes} and starts {self} with Windows." : $"{closes}.";
    }

    internal static string ForcedCloseText(Version? running) =>
        $"{CopyName(running, "The other copy")} is closed at once. A dictation in progress is lost.";

    private static string CopyName(Version? version, string unknown) => version is null ? unknown : ApplicationVersion.Number(version);

    private void UseThisCopy(object sender, RoutedEventArgs e) => DialogResult = true;

    private void KeepOtherCopy(object sender, RoutedEventArgs e) => DialogResult = false;

    private void FocusSafeChoice(object sender, RoutedEventArgs e) => KeepButton.Focus();

    private void DragWindow(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == Input.MouseButtonState.Pressed)
            DragMove();
    }
}
