using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

internal static class SettingsVocabularyProbe
{
    public static void Run(string outputDirectory)
    {
        VerifyEmptyAndTabs(outputDirectory);
        VerifyPopulated(outputDirectory);
        VerifyValidationAndSave();
        Console.WriteLine("PASS: Settings tabs switch pages; Vocabulary opens directly, shows empty and populated scopes, scrolls with fixed actions, and saves validated phrases and strength.");
    }

    private static void VerifyEmptyAndTabs(string outputDirectory)
    {
        var window = Create(PortableSettings.Default, openVocabulary: false);
        window.Show();
        try
        {
            Flush(window);
            var general = (FrameworkElement)window.FindName("GeneralPage");
            var vocabulary = (FrameworkElement)window.FindName("VocabularyPage");
            var tab = (RadioButton)window.FindName("VocabularyTab");
            Require(general.IsVisible && !vocabulary.IsVisible, "Settings must open on General by default.");
            Require(new RadioButtonAutomationPeer(tab).GetName() == "Vocabulary settings", "Tabs need accessible names.");

            tab.IsChecked = true;
            Flush(window);
            Require(!general.IsVisible && vocabulary.IsVisible, "The Vocabulary tab must show the vocabulary page.");
            Require(((Border)window.FindName("VocabularyEmptyState")).IsVisible, "An empty scope must show the empty state.");
            Require(((RadioButton)window.FindName("VocabularyTab")).IsChecked == true, "The active tab must be exposed as selected.");
            VerifyContained(window, (FrameworkElement)window.FindName("SaveSettingsButton"));
            Capture(window, outputDirectory, "settings-vocabulary-empty.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyPopulated(string outputDirectory)
    {
        var entries = new List<VocabularyEntry>
        {
            new("PrivateType", "shared"),
            new("Kubernetes", "shared"),
            new("Żółć i źdźbło w długiej frazie z polskimi znakami diakrytycznymi oraz nazwą produktu PostgreSQL", "shared"),
            new("Wiesław", "pl")
        };
        entries.AddRange(Enumerable.Range(1, 14).Select(index => new VocabularyEntry($"Term {index:D2}", "shared")));
        var window = Create(PortableSettings.Default with { Vocabulary = entries, VocabularyStrength = "strong" }, openVocabulary: true);
        window.Show();
        try
        {
            Flush(window);
            Require(((FrameworkElement)window.FindName("VocabularyPage")).IsVisible, "Opening from the Vocabulary action must select the vocabulary page.");
            var list = (ItemsControl)window.FindName("VocabularyList");
            Require(list.Items.Count == 17, "The shared scope must list only shared phrases.");
            var scopes = window.Vocabulary.Scopes;
            Require(scopes.Single(option => option.Code == "shared").Label == "Shared across languages (17)"
                && scopes.Single(option => option.Code == "pl").Label == "Polish (1)"
                && scopes.Single(option => option.Code == "en").Label == "English",
                "The language picker must show how many phrases each language holds.");
            window.Vocabulary.Add().Phrase = "Nowa fraza";
            Require(scopes.Single(option => option.Code == "shared").Count == 18, "Counts must follow edits.");
            window.Vocabulary.Remove(window.Vocabulary.Visible.Last());
            var boxes = Descendants(list).OfType<TextBox>().ToList();
            Require(boxes.All(box => box.ActualWidth > 200 && AutomationPeer(box).GetName() == "Vocabulary phrase"), "Phrase boxes need width and accessible names.");
            var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
            Require(scroll.ScrollableWidth < 1, "Vocabulary must not overflow horizontally.");
            Require(scroll.ScrollableHeight > 0, "A long vocabulary must scroll.");
            Capture(window, outputDirectory, "settings-vocabulary-populated.png");

            scroll.ScrollToBottom();
            Flush(window);
            VerifyContained(window, (FrameworkElement)window.FindName("SaveSettingsButton"));
            VerifyContained(window, (FrameworkElement)window.FindName("AddPhraseButton"));
            Capture(window, outputDirectory, "settings-vocabulary-bottom.png");

            ((ComboBox)window.FindName("VocabularyScopeBox")).SelectedValue = "pl";
            Flush(window);
            Require(list.Items.Count == 1, "Changing scope must filter to that language.");
            ((ComboBox)window.FindName("VocabularyScopeBox")).IsDropDownOpen = true;
            Flush(window);
            var box = (ComboBox)window.FindName("VocabularyScopeBox");
            var popup = box.Template.FindName("Popup", box) as Popup ?? box.Template.FindName("PART_Popup", box) as Popup;
            Require(popup?.IsOpen == true && popup.Child is FrameworkElement, "The language scope list must open.");
            Capture((FrameworkElement)popup!.Child, outputDirectory, "settings-vocabulary-scopes.png");
            box.IsDropDownOpen = false;
        }
        finally
        {
            window.Close();
        }

        // Teach opens Settings on the language it just saved to.
        var opened = Create(PortableSettings.Default with
        {
            Vocabulary = entries,
            VocabularyCorrections = [new("free the printing", "3D printing", "pl"), new("fusion tree sixty", "Fusion 360", "pl"), new("sea sharp", "C#", "shared")]
        }, openVocabulary: true, vocabularyScope: "pl");
        opened.Show();
        try
        {
            Flush(opened);
            Require(Equals(((ComboBox)opened.FindName("VocabularyScopeBox")).SelectedValue, "pl") && ((ItemsControl)opened.FindName("VocabularyList")).Items.Count == 1,
                "Opening on a language must select it and list its phrases.");
            var corrections = (ItemsControl)opened.FindName("CorrectionList");
            Require(corrections.Items.Count == 2 && ((CheckBox)opened.FindName("CorrectAfterDictationBox")).IsChecked == true
                && !((FrameworkElement)opened.FindName("CorrectionsEmptyState")).IsVisible,
                "The language's taught corrections must be listed with correction on by default.");
            var correctionBoxes = Descendants(corrections).OfType<TextBox>().ToList();
            Require(correctionBoxes.Count == 4 && correctionBoxes.All(box => box.ActualWidth > 120 && AutomationPeer(box).GetName() is "Heard as" or "Write as"),
                "Correction rows need two named, usefully wide boxes.");
            ((FrameworkElement)opened.FindName("CorrectionList")).BringIntoView();
            Flush(opened);
            Capture(opened, outputDirectory, "settings-vocabulary-corrections.png");
            ((ComboBox)opened.FindName("VocabularyScopeBox")).SelectedValue = "en";
            Flush(opened);
            Require(corrections.Items.Count == 0 && ((FrameworkElement)opened.FindName("CorrectionsEmptyState")).IsVisible, "A language without corrections must show the empty state.");
            ((ComboBox)opened.FindName("VocabularyScopeBox")).SelectedValue = "pl";
            Flush(opened);
            Capture(opened, outputDirectory, "settings-vocabulary-opened-on-language.png");
        }
        finally
        {
            opened.Close();
        }
    }

    private static void VerifyValidationAndSave()
    {
        var window = Create(PortableSettings.Default, openVocabulary: false);
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var editor = window.Vocabulary;
                editor.Add().Phrase = "Duplicate";
                editor.Add().Phrase = " Duplicate ";
                Click(window, "SaveSettingsButton");
                Require(window.IsVisible && ((RadioButton)window.FindName("VocabularyTab")).IsChecked == true,
                    "A vocabulary error must keep Settings open on the Vocabulary page.");
                Require(((TextBlock)window.FindName("ValidationText")).Text.Contains("twice", StringComparison.Ordinal)
                    && !((TextBlock)window.FindName("ValidationText")).Text.Contains("Duplicate", StringComparison.Ordinal),
                    "The error must explain the duplicate without quoting the phrase.");

                editor.Remove(editor.Visible.Last());
                editor.Scope = "en";
                editor.Add().Phrase = "WireGuard";
                editor.Add();
                editor.Strength = "low";

                var half = editor.AddCorrection();
                half.Heard = "wire guard";
                Click(window, "SaveSettingsButton");
                Require(window.IsVisible && ((TextBlock)window.FindName("ValidationText")).Text.StartsWith("Correction:", StringComparison.Ordinal)
                    && !((TextBlock)window.FindName("ValidationText")).Text.Contains("wire", StringComparison.Ordinal),
                    "A half-filled correction must block saving with a content-free error.");
                half.Phrase = "WireGuard";
                editor.AddCorrection();
                editor.CorrectAfterDictation = false;
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
        Require(window.SavedSettings is { } saved
            && saved.Vocabulary.SequenceEqual([new VocabularyEntry("Duplicate", "shared"), new VocabularyEntry("WireGuard", "en")])
            && saved.VocabularyStrength == "low"
            && saved.VocabularyCorrections.SequenceEqual([new VocabularyCorrection("wire guard", "WireGuard", "en")])
            && !saved.CorrectAfterDictation,
            "Saved settings must carry normalized phrases, their scopes, the strength, corrections, and the correction switch.");
    }

    private static SettingsWindow Create(PortableSettings settings, bool openVocabulary, string? vocabularyScope = null) =>
        new(settings, [new MicrophoneOption("default", "System default microphone")], openVocabulary, vocabularyScope) { ShowInTaskbar = false };

    private static AutomationPeer AutomationPeer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Missing automation peer.");

    private static void Click(Window window, string name)
    {
        var button = (Button)window.FindName(name);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        Flush(window);
    }

    private static void VerifyContained(Window window, FrameworkElement control)
    {
        var origin = control.TranslatePoint(new Point(), window);
        Require(control.IsVisible && origin.X >= 0 && origin.Y >= 0
            && origin.X + control.ActualWidth <= window.ActualWidth + 1
            && origin.Y + control.ActualHeight <= window.ActualHeight + 1,
            $"{control.Name} must remain visible inside the window.");
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
