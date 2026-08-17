using System.Windows;
using Input = System.Windows.Input;

namespace PrivateType.App;

public partial class StartupVersionPromptWindow : Window
{
    public StartupVersionPromptWindow(Version? registeredVersion, Version? currentVersion)
    {
        InitializeComponent();
        VersionSummary.Text = $"{VersionLabel(registeredVersion)} is currently registered. You opened {VersionLabel(currentVersion)}.";
    }

    internal static string VersionLabel(Version? version)
    {
        if (version is null)
            return "PrivateType (version unknown)";

        var fieldCount = version.Revision > 0 ? 4 : version.Build > 0 ? 3 : 2;
        return $"PrivateType {version.ToString(fieldCount)}";
    }

    private void UseCurrentVersion(object sender, RoutedEventArgs e) => DialogResult = true;

    private void KeepRegisteredVersion(object sender, RoutedEventArgs e) => DialogResult = false;

    private void FocusSafeChoice(object sender, RoutedEventArgs e) => KeepButton.Focus();

    private void DragWindow(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == Input.MouseButtonState.Pressed)
            DragMove();
    }
}
