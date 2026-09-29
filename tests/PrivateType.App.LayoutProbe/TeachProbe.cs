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
        Console.WriteLine("PASS: Teach is disabled until a result exists; the dialog selects words by mouse and keyboard, collects several non-overlapping fixes with their own languages, keeps the fixes on save failure, saves only the typed phrases, and offers to open the saved language.");
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
            Require((string)save.Content == "Save term", "One pending fix must offer to save one term.");
            Capture(window, outputDirectory, "teach-range-edited.png");

            Click(chips[1]);
            Require(window.Words.Count(word => word.IsSelected) == 1 && desired.Text == "MVVM",
                "A new click must restart the selection without overwriting a typed correction.");

            window.ExtendSelection(2);
            window.ExtendSelection(3);
            Require(window.Words.Where(word => word.IsSelected).Select(word => word.Index).SequenceEqual([1, 2, 3]), "Keyboard extension must grow the range.");

            PressEscape(window);
            Require(window.IsVisible && window.Words.All(word => !word.IsSelected) && !save.IsEnabled, "Escape must clear the selection before closing.");

            // Several fixes for one sentence.
            var add = (Button)window.FindName("AddFixButton");
            Click(chips[3]);
            Click(chips[5]);
            desired.Text = "MVVM";
            Flush(window);
            Click(add);
            Require(window.Fixes.Count == 1 && window.Words.Where(word => word.IsFixed).Select(word => word.Index).SequenceEqual([3, 4, 5])
                && !chips[4].IsEnabled && desired.Text.Length == 0 && window.Words.All(word => !word.IsSelected) && save.IsEnabled && (string)save.Content == "Save term",
                "Adding a fix must mark its words, clear the selection and spelling, and keep Save available.");
            Capture(window, outputDirectory, "teach-one-fix.png");

            Click(chips[1]);
            Require(desired.Text == "app", "The next selection must prefill again after a fix was added.");
            desired.Text = "App";
            ((ComboBox)window.FindName("ScopeBox")).SelectedValue = VocabularyScopes.Shared;
            Flush(window);
            Require((string)save.Content == "Save 2 terms", "A pending fix must count toward the terms to save.");
            Click(add);
            Require(window.Fixes.Select(fix => fix.Entry).SequenceEqual([new VocabularyEntry("App", VocabularyScopes.Shared), new VocabularyEntry("MVVM", "en")]),
                "Fixes must keep their own language and stay in sentence order.");
            var fixList = (ItemsControl)window.FindName("FixList");
            Require(fixList.Items.Count == 2 && ((FrameworkElement)window.FindName("FixesPanel")).IsVisible, "Added fixes must be listed.");
            Capture(window, outputDirectory, "teach-two-fixes.png");

            window.ActivateWord(0);
            window.ActivateWord(4);
            Flush(window);
            Require(!add.IsEnabled && ((TextBlock)window.FindName("SelectionHint")).Text.Length > 0, "A selection over fixed words must not be addable and must say why.");
            Capture(window, outputDirectory, "teach-overlap.png");
            PressEscape(window);
            Require(((TextBlock)window.FindName("SelectionHint")).Text.Length == 0, "Clearing the selection must clear the hint.");

            var remove = Descendants(fixList).OfType<Button>().ToList();
            Require(remove.Count == 2 && remove.All(button => AutomationPeer(button).GetName().StartsWith("Remove fix", StringComparison.Ordinal)), "Every fix needs a named remove button.");
            Click(remove[0]);
            Require(window.Fixes.Count == 1 && chips[1].IsEnabled && !window.Words[1].IsFixed && (string)save.Content == "Save term", "Removing a fix must free its words.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyRetryAndSave()
    {
        var attempts = new List<IReadOnlyList<VocabularyEntry>>();
        var window = new TeachWindow(new FinalizedDictation(Sentence, "pl-PL"), "strong", entries =>
        {
            attempts.Add(entries);
            return attempts.Count == 1 ? "The phrases could not be saved. Check that the PrivateType folder is writable, then try again." : null;
        }) { ShowInTaskbar = false };
        Exception? failure = null;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "live-dictation-layout-probe");
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var desired = (TextBox)window.FindName("DesiredBox");
                window.ActivateWord(3);
                desired.Text = "MVVM";
                Flush(window);
                Click((Button)window.FindName("AddFixButton"));

                // The second fix is still pending when Save is pressed, so Save adds it first.
                window.ActivateWord(1);
                desired.Text = "App";
                ((ComboBox)window.FindName("ScopeBox")).SelectedValue = VocabularyScopes.Shared;
                Flush(window);
                Click((Button)window.FindName("SaveButton"));
                var error = ((TextBlock)window.FindName("ErrorText")).Text;
                Require(window.IsVisible && error.Length > 0 && !error.Contains("model", StringComparison.Ordinal) && window.Fixes.Count == 2
                    && ((FrameworkElement)window.FindName("EditPanel")).IsVisible,
                    "A failed save must keep the dialog, fixes, and sentence, with a content-free error.");
                Capture(window, outputDirectory, "teach-save-error.png");
                Click((Button)window.FindName("SaveButton"));

                Require(((FrameworkElement)window.FindName("SavedPanel")).IsVisible && !((FrameworkElement)window.FindName("EditPanel")).IsVisible
                    && ((TextBlock)window.FindName("SavedText")).Text == "Saved 2 terms to Shared across languages and Polish.",
                    "A successful save must confirm how many terms went to which languages.");
                Capture(window, outputDirectory, "teach-saved.png");
                Click((Button)window.FindName("OpenVocabularyButton"));
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
        Require(result == true && attempts.Count == 2
            && attempts.All(entries => entries.SequenceEqual([new VocabularyEntry("App", VocabularyScopes.Shared), new VocabularyEntry("MVVM", "pl")])),
            "Retry must save only the typed phrases, each for its own language.");
        Require(window.VocabularyScopeToOpen == VocabularyScopes.Shared, "Open vocabulary must ask for the first saved term's language.");
    }

    private static void PressEscape(Window window)
    {
        window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.Escape)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
        });
        Flush(window);
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
