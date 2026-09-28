using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

internal static class SettingsLanguageProbe
{
    public static void Run(string outputDirectory)
    {
        var settings = PortableSettings.Default with
        {
            Shortcuts =
            [
                new ShortcutBinding("zh-CN", 0x52),
                new ShortcutBinding("nb-NO", 0x45),
                new ShortcutBinding("auto", 0x41)
            ]
        };
        var window = new SettingsWindow(settings, [new MicrophoneOption("default", "System default microphone")]) { ShowInTaskbar = false };
        window.Show();
        try
        {
            Flush(window);
            var selectors = Descendants(window).OfType<ComboBox>().Where(box => box.Name.Length == 0 && box.ItemsSource is IEnumerable<LanguageOption>).ToList();
            Require(selectors.Count == 3, "Each shortcut row must have a language selector.");
            foreach (var selector in selectors)
            {
                Require(selector.Items.Count == 33, "The selector must offer Automatic plus 32 explicit locales.");
                Require(((LanguageOption)selector.Items[0]).LocaleCode == "auto", "Automatic must be listed first.");
                var label = Descendants(selector).OfType<TextBlock>().FirstOrDefault(text => text.IsVisible && text.Text == ((LanguageOption)selector.SelectedItem).Label)
                    ?? throw new InvalidOperationException("The selected language must be visible.");
                var rendered = label.ActualWidth;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Require(rendered > 0 && rendered + 0.5 >= label.DesiredSize.Width, $"{label.Text} must not be clipped.");
                label.InvalidateMeasure();
                Flush(window);
                Require(AutomationPeer(selector).GetName() == "Recognition language", "Language selectors need an accessible name.");
            }
            Require(Descendants(window).OfType<TextBox>().Where(box => box.IsReadOnly && box.Text.StartsWith("Ctrl+Shift", StringComparison.Ordinal)).All(box => box.ExtentWidth <= box.ViewportWidth + 0.5),
                "Shortcut labels must fit their boxes.");
            Capture(window, outputDirectory, "settings-languages.png");

            foreach (var name in new[] { "ShortcutModeBox", "InsertionModeBox" })
            {
                var box = (ComboBox)window.FindName(name);
                Require(box.Items.Count == 2 && box.SelectedIndex == 0, $"{name} must offer two choices with the default selected.");
                Require(AutomationPeer(box).GetName().Length > 0 && box.Focus(), $"{name} needs an accessible name and keyboard focus.");
            }
            var hint = (TextBlock)window.FindName("InsertionModeHint");
            var pastingHint = hint.Text;
            Require(pastingHint.Contains("clipboard is restored", StringComparison.Ordinal), "The default paste mode must explain clipboard handling.");
            ((ComboBox)window.FindName("InsertionModeBox")).SelectedValue = "type";
            Flush(window);
            Require(hint.Text.Length > 0 && hint.Text != pastingHint && hint.Text.Contains("Ctrl+V", StringComparison.Ordinal),
                "Choosing typing must explain when to use it.");
            hint.BringIntoView();
            Flush(window);
            Capture(window, outputDirectory, "settings-dictation-modes.png");

            var first = selectors[0];
            first.Focus();
            first.IsDropDownOpen = true;
            Flush(window);
            var popup = first.Template.FindName("Popup", first) as Popup ?? first.Template.FindName("PART_Popup", first) as Popup;
            Require(popup?.IsOpen == true && popup.Child is FrameworkElement, "The language selector must open a rendered list.");
            Capture((FrameworkElement)popup!.Child, outputDirectory, "settings-languages-open.png");
            first.IsDropDownOpen = false;
            Flush(window);

            // Type-ahead by display name; the stored value stays the locale code.
            var item = TextSearch(first, "Spanish (Spain)");
            first.SelectedItem = item;
            Flush(window);
            Require(Equals(first.SelectedValue, "es-ES"), "Selecting Spanish (Spain) must store es-ES.");
            Require(first.Focus() && first.IsKeyboardFocusWithin, "Language selectors must accept keyboard focus.");
        }
        finally
        {
            window.Close();
        }

        VerifySavedLocaleCodes(settings);
        Console.WriteLine("PASS: Settings offers 33 language choices without clipping, opens the list, supports type-ahead and focus, and saves locale codes, shortcut behavior, and insertion mode.");
    }

    private static void VerifySavedLocaleCodes(PortableSettings settings)
    {
        var window = new SettingsWindow(settings, [new MicrophoneOption("default", "System default microphone")]) { ShowInTaskbar = false };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var first = Descendants(window).OfType<ComboBox>().First(box => box.ItemsSource is IEnumerable<LanguageOption>);
                first.SelectedItem = TextSearch(first, "Spanish (Spain)");
                ((ComboBox)window.FindName("ShortcutModeBox")).SelectedValue = "toggle";
                ((ComboBox)window.FindName("InsertionModeBox")).SelectedValue = "type";
                Click(window, "SaveSettingsButton");
            }
            catch (Exception exception)
            {
                failure = exception;
                window.Close();
            }
        }, DispatcherPriority.ApplicationIdle);
        window.ShowDialog();
        if (failure is not null)
            throw failure;
        Require(window.SavedSettings is { } saved && saved.Shortcuts[0].LocaleCode == "es-ES" && saved.Shortcuts[2].LocaleCode == "auto",
            "Saved shortcuts must carry locale codes.");
        Require(window.SavedSettings!.ShortcutMode == "toggle" && window.SavedSettings.InsertionMode == "type",
            "Saved settings must carry the chosen shortcut behavior and insertion mode.");
    }

    private static object TextSearch(ComboBox box, string prefix) =>
        box.Items.Cast<LanguageOption>().First(option => option.Label.StartsWith(prefix, StringComparison.Ordinal));

    private static AutomationPeer AutomationPeer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Missing automation peer.");

    private static void Click(Window window, string name)
    {
        var button = (Button)window.FindName(name);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        Flush(window);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void Capture(FrameworkElement element, string directory, string name)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
    }

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
