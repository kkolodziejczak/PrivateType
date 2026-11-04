using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

internal static class SettingsStartupProbe
{
    private const string SourceBuildHint = "Only a copy from the portable ZIP can change this. This one was built from source.";

    public static void Run(string outputDirectory)
    {
        VerifyPortableCopy(outputDirectory);
        VerifySourceBuild(startWithWindows: false, outputDirectory, "settings-startup-source-build.png");
        VerifySourceBuild(startWithWindows: true, outputDirectory, "settings-startup-source-build-registered.png");
        VerifySourceBuildSaveKeepsChoice();
    }

    private static void VerifyPortableCopy(string outputDirectory)
    {
        var window = Create(PortableSettings.Default, canChangeStartup: true);
        window.Show();
        try
        {
            Flush(window);
            var checkBox = Find<CheckBox>(window, "StartWithWindowsCheckBox");
            Require(checkBox.IsEnabled, "A portable copy can change the startup choice.");
            Require(!Find<TextBlock>(window, "StartWithWindowsHint").IsVisible, "A portable copy shows no startup hint.");
            Capture(window, checkBox, outputDirectory, "settings-startup-portable.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifySourceBuild(bool startWithWindows, string outputDirectory, string name)
    {
        var window = Create(PortableSettings.Default with { StartWithWindows = startWithWindows }, canChangeStartup: false);
        window.Show();
        try
        {
            Flush(window);
            var checkBox = Find<CheckBox>(window, "StartWithWindowsCheckBox");
            var hint = Find<TextBlock>(window, "StartWithWindowsHint");
            Require(!checkBox.IsEnabled, "A build from source cannot change the startup choice.");
            RequireAssistiveToggleRefused(checkBox);
            Require(checkBox.IsChecked == startWithWindows, "The checkbox still shows whether a copy starts with Windows.");
            Require(checkBox.Opacity < 1, "The unavailable checkbox looks unavailable.");
            Require(hint.IsVisible && hint.Text == SourceBuildHint, "The hint says why the checkbox is unavailable.");
            Require(Find<ComboBox>(window, "IdleTimeoutBox").IsEnabled, "Unload model after still works.");
            Capture(window, hint, outputDirectory, name);
            var scroll = Find<ScrollViewer>(window, "SettingsScrollViewer");
            var origin = hint.TranslatePoint(new Point(), scroll);
            Require(origin.X >= 0 && origin.X + hint.ActualWidth <= scroll.ViewportWidth + 1
                && origin.Y >= 0 && origin.Y + hint.ActualHeight <= scroll.ViewportHeight + 1,
                "The hint fits the visible part of Settings.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void RequireAssistiveToggleRefused(CheckBox checkBox)
    {
        var before = checkBox.IsChecked;
        var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkBox).GetPattern(PatternInterface.Toggle);
        try
        {
            toggle.Toggle();
        }
        catch (ElementNotEnabledException)
        {
        }

        Require(checkBox.IsChecked == before, "Assistive technology cannot change the unavailable checkbox.");
    }

    private static void VerifySourceBuildSaveKeepsChoice()
    {
        var window = Create(PortableSettings.Default with { StartWithWindows = true }, canChangeStartup: false);
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var save = Find<Button>(window, "SaveSettingsButton");
                ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)).Invoke();
            }
            catch (Exception exception)
            {
                failure = exception;
                window.Close();
            }
        }, DispatcherPriority.ApplicationIdle);
        var result = window.ShowDialog();
        if (failure is not null)
            throw failure;
        Require(result == true && window.SavedSettings is { StartWithWindows: true }, "Saving from a build from source keeps the startup choice.");
    }

    private static SettingsWindow Create(PortableSettings settings, bool canChangeStartup) =>
        new(settings, [new MicrophoneOption("default", "System default microphone")], ProbeModelStore.Empty, canChangeStartup: canChangeStartup) { ShowInTaskbar = false };

    private static void Capture(Window window, FrameworkElement control, string directory, string name)
    {
        control.BringIntoView();
        Flush(window);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(window.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"Missing control {name}.");

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
