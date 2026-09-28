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

// Uses a synthetic sentence only; never real dictated content.
internal static class TeachProbe
{
    private const string Sentence = "The app uses model view model for the settings screen.";

    public static void Run(string outputDirectory)
    {
        VerifyBubbleMenu(outputDirectory);
        VerifyDialogStates(outputDirectory);
        VerifyRetryAndSave();
        Require(TeachWindow.SuggestedScope("pl-PL") == "pl" && TeachWindow.SuggestedScope("auto") == VocabularyScopes.Shared,
            "Save-for must default to the dictation's base language, or Shared after Automatic.");
        Console.WriteLine("PASS: Teach is disabled until a result exists; the dialog selects one word or a contiguous range by mouse and keyboard, prefills and edits the correction, keeps the sentence on save failure, and saves only the typed phrase.");
    }

    private static void VerifyBubbleMenu(string outputDirectory)
    {
        var bubble = new DictationBubble();
        bubble.Show();
        try
        {
            bubble.ShowReady(PortableSettings.Default, modelLoaded: true);
            Flush(bubble);
            var menu = (ContextMenu)((FrameworkElement)bubble.FindName("BubbleShell")).ContextMenu;
            var teach = (MenuItem)bubble.FindName("TeachMenuItem");
            Require(!teach.IsEnabled && !bubble.IsTeachAvailable, "Teach must start disabled.");
            Require(menu.Items.OfType<MenuItem>().All(item => item.Header is string header && !header.Contains(Sentence, StringComparison.Ordinal)),
                "The menu must never show transcript text.");
            // Open once so the popup is realized before capturing.
            menu.IsOpen = true;
            Flush(bubble);
            menu.UpdateLayout();
            Capture(menu, outputDirectory, "bubble-menu-teach-disabled.png");
            menu.IsOpen = false;

            bubble.SetTeachAvailable(true);
            menu.IsOpen = true;
            Flush(bubble);
            Require(teach.IsEnabled, "Teach must enable once a result exists.");
            menu.UpdateLayout();
            Capture(menu, outputDirectory, "bubble-menu-teach-enabled.png");
            menu.IsOpen = false;
        }
        finally
        {
            bubble.Close();
        }
    }

    private static void VerifyDialogStates(string outputDirectory)
    {
        var window = new TeachWindow(new FinalizedDictation(Sentence, "en-US"), "normal", _ => null) { ShowInTaskbar = false };
        window.Show();
        try
        {
            Flush(window);
            var save = (Button)window.FindName("SaveButton");
            var desired = (TextBox)window.FindName("DesiredBox");
            Require(window.Words.Count == 10 && !save.IsEnabled, "The dialog must show every word and wait for a selection.");
            Require(Equals(((ComboBox)window.FindName("ScopeBox")).SelectedValue, "en"), "English dictation must suggest English.");
            var chips = Descendants((ItemsControl)window.FindName("WordList")).OfType<ToggleButton>().ToList();
            Require(chips.Count == 10 && chips.All(chip => chip.Focusable && AutomationPeer(chip).GetName().Length > 0), "Every word must be a focusable, named chip.");
            Capture(window, outputDirectory, "teach-no-selection.png");

            Click(chips[3]);
            Require(window.Words.Count(word => word.IsSelected) == 1 && desired.Text == "model" && save.IsEnabled,
                "One click must select one word and prefill it.");
            Capture(window, outputDirectory, "teach-one-word.png");

            Click(chips[5]);
            Require(window.Words.Where(word => word.IsSelected).Select(word => word.Index).SequenceEqual([3, 4, 5]) && desired.Text == "model view model",
                "A second click must select the contiguous range.");
            Require(chips.Select(chip => chip.IsChecked == true).SequenceEqual(window.Words.Select(word => word.IsSelected)), "Chips must mirror the selection.");
            desired.Text = "MVVM";
            Flush(window);
            Capture(window, outputDirectory, "teach-range-edited.png");

            Click(chips[1]);
            Require(window.Words.Count(word => word.IsSelected) == 1 && desired.Text == "MVVM",
                "A new click must restart the selection without overwriting a typed correction.");

            window.ExtendSelection(2);
            window.ExtendSelection(3);
            Require(window.Words.Where(word => word.IsSelected).Select(word => word.Index).SequenceEqual([1, 2, 3]), "Keyboard extension must grow the range.");

            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
            });
            Flush(window);
            Require(window.IsVisible && window.Words.All(word => !word.IsSelected) && !save.IsEnabled, "Escape must clear the selection before closing.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyRetryAndSave()
    {
        var attempts = new List<VocabularyEntry>();
        var window = new TeachWindow(new FinalizedDictation(Sentence, "pl-PL"), "strong", entry =>
        {
            attempts.Add(entry);
            return attempts.Count == 1 ? "The phrase could not be saved. Check that the PrivateType folder is writable, then try again." : null;
        }) { ShowInTaskbar = false };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                window.ActivateWord(3);
                ((TextBox)window.FindName("DesiredBox")).Text = "MVVM";
                Flush(window);
                Click((Button)window.FindName("SaveButton"));
                var error = ((TextBlock)window.FindName("ErrorText")).Text;
                Require(window.IsVisible && error.Length > 0 && !error.Contains("model", StringComparison.Ordinal) && window.Words.Any(word => word.IsSelected),
                    "A failed save must keep the dialog, selection, and sentence, with a content-free error.");
                Capture(window, Path.Combine(Path.GetTempPath(), "live-dictation-layout-probe"), "teach-save-error.png");
                Click((Button)window.FindName("SaveButton"));
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
        Require(result == true && attempts.Count == 2 && attempts.All(entry => entry == new VocabularyEntry("MVVM", "pl")),
            "Retry must save only the typed phrase for the suggested language.");
    }

    private static FrameworkElement Root(ContextMenu menu)
    {
        DependencyObject node = menu;
        while (VisualTreeHelper.GetParent(node) is { } parent)
            node = parent;
        return (FrameworkElement)node;
    }

    private static AutomationPeer AutomationPeer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Missing automation peer.");

    private static void Click(ButtonBase button)
    {
        // A real click flips the toggle and then raises Click, which is where selection happens.
        if (button is ToggleButton toggle)
        {
            toggle.IsChecked = toggle.IsChecked != true;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        else
            ((IInvokeProvider)new ButtonAutomationPeer((Button)button).GetPattern(PatternInterface.Invoke)).Invoke();
        if (Window.GetWindow(button) is { } window)
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
