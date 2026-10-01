using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

// Synthetic, public phrases only; no dictated or private content.
internal static class SettingsPackProbe
{
    private static readonly VocabularyPack Development = new("Software development", "en", true, ["Kubernetes", "PostgreSQL", "WireGuard", "Proxmox"]);
    private static readonly VocabularyPack Products = new("Product names with a deliberately long local name for layout", "shared", false, ["PrivateType", "Nemotron"]);

    public static void Run(string outputDirectory)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"privatetype-pack-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            VerifyPackSection(outputDirectory, scratch);
            VerifyImportPreview(outputDirectory);
            VerifyPackEditor(outputDirectory);
            VerifyExport(outputDirectory, scratch);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        Console.WriteLine("PASS: Packs section lists, toggles, filters, removes with confirmation, and rejects bad files without quoting them; import preview, pack editor, and export review render and validate; export writes the reviewed canonical array.");
    }

    private static void VerifyPackSection(string outputDirectory, string scratch)
    {
        var settings = PortableSettings.Default with
        {
            Vocabulary = [new("MVVM", "en")],
            VocabularyPacks = [Development, Products]
        };
        var window = new SettingsWindow(settings, [new MicrophoneOption("default", "System default microphone")], ProbeModelStore.Empty, openVocabulary: true) { ShowInTaskbar = false };
        window.Show();
        try
        {
            Flush(window);
            var editor = window.Vocabulary;
            var packList = (ItemsControl)window.FindName("PackList");
            Require(packList.Items.Count == 1 && ((Border)window.FindName("PacksEmptyState")).Visibility != Visibility.Visible,
                "Shared scope must list only the shared pack.");
            editor.ShowAllPacks = true;
            editor.Scope = "en";
            Flush(window);
            Require(packList.Items.Count == 2, "Show all packs must list every pack.");
            Require(((TextBlock)window.FindName("VocabularyBudgetText")).Text.StartsWith("English dictation uses 5 of 200", StringComparison.Ordinal),
                "The budget line must count personal plus enabled applicable pack phrases.");
            var toggles = Descendants(packList).OfType<CheckBox>().ToList();
            Require(toggles.Count == 2 && toggles.All(toggle => AutomationPeer(toggle).GetName().StartsWith("Use pack ", StringComparison.Ordinal)),
                "Each pack needs a named enable toggle.");
            var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
            scroll.ScrollToBottom();
            Flush(window);
            Require(scroll.ScrollableWidth < 1, "Packs must not overflow horizontally.");
            Require(Descendants(packList).OfType<TextBlock>().Where(text => text.IsVisible).All(text => text.ActualWidth <= packList.ActualWidth),
                "Pack text must stay within the card.");
            Capture(window, outputDirectory, "settings-vocabulary-packs.png");

            toggles.Single(toggle => AutomationPeer(toggle).GetName().Contains("Product", StringComparison.Ordinal)).IsChecked = true;
            Flush(window);
            Require(((TextBlock)window.FindName("VocabularyBudgetText")).Text.StartsWith("English dictation uses 7 of 200", StringComparison.Ordinal),
                "Enabling a shared pack must raise the budget count.");

            window.ConfirmPackRemoval = _ => false;
            Click(Descendants(packList).OfType<Button>().First(button => Equals(button.Content, "Remove…")));
            Require(editor.Packs.Count == 2, "Declining the confirmation must keep the pack.");
            window.ConfirmPackRemoval = _ => true;
            Click(Descendants(packList).OfType<Button>().First(button => Equals(button.Content, "Remove…")));
            Flush(window);
            Require(editor.Packs.Count == 1, "Confirming must remove the pack from the local copy.");

            var badFile = Path.Combine(scratch, "bad" + VocabularyPackCodec.Extension);
            File.WriteAllText(badFile, """[{"phrase": "confidential-codename", "weight": "strong"}]""");
            window.ChoosePackFile = () => badFile;
            Click((Button)window.FindName("ImportPackButton"));
            var message = ((TextBlock)window.FindName("ValidationText")).Text;
            Require(message.StartsWith("That pack could not be imported.", StringComparison.Ordinal) && !message.Contains("confidential", StringComparison.Ordinal),
                "A bad pack file must be rejected without quoting its content.");
            Require(editor.Packs.Count == 1, "A rejected file must not change installed packs.");
            Capture(window, outputDirectory, "settings-vocabulary-import-error.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyImportPreview(string outputDirectory)
    {
        var vocabulary = new VocabularyEditor([], "normal", [Development]);
        var window = new ImportPackWindow("software-development" + VocabularyPackCodec.Extension, ["Grafana", "Terraform", "Zigbee"], vocabulary) { ShowInTaskbar = false };
        window.Show();
        try
        {
            Flush(window);
            var name = (TextBox)window.FindName("NameBox");
            var import = (Button)window.FindName("ImportButton");
            Require(name.Text == "Software development (2)", "A clashing file name must propose a unique local name.");
            Require(!import.IsEnabled, "Import must wait for an explicit language.");
            ((ComboBox)window.FindName("ScopeBox")).SelectedValue = "en";
            Flush(window);
            Require(import.IsEnabled, "Import must enable once a language is chosen.");
            Capture(window, outputDirectory, "import-pack-preview.png");

            name.Text = "Software development";
            Flush(window);
            Require(!import.IsEnabled && ((TextBlock)window.FindName("ErrorText")).Text.Contains("already installed", StringComparison.Ordinal),
                "A duplicate name must block import with an inline message.");
            Capture(window, outputDirectory, "import-pack-collision.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyPackEditor(string outputDirectory)
    {
        var vocabulary = new VocabularyEditor([], "normal", [Development]) { ShowAllPacks = true };
        var item = vocabulary.VisiblePacks.Single();
        var window = new PackEditorWindow(item, vocabulary) { ShowInTaskbar = false };
        window.Show();
        try
        {
            Flush(window);
            Require(((ItemsControl)window.FindName("PhraseList")).Items.Count == 4, "The editor must list the pack's phrases.");
            ((TextBox)window.FindName("NameBox")).Text = "Development tools";
            ((ComboBox)window.FindName("ScopeBox")).SelectedValue = "pl";
            var rows = (System.Collections.IList)((ItemsControl)window.FindName("PhraseList")).ItemsSource;
            ((VocabularyPhraseEditor)rows[1]!).Phrase = "Kubernetes";
            Flush(window);
            Click((Button)window.FindName("SaveButton"));
            var error = ((TextBlock)window.FindName("ErrorText")).Text;
            Require(window.IsVisible && error.Contains("twice", StringComparison.Ordinal) && !error.Contains("Kubernetes", StringComparison.Ordinal),
                "A duplicate phrase must keep the editor open with a content-free error.");
            Capture(window, outputDirectory, "pack-editor-error.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyExport(string outputDirectory, string scratch)
    {
        var preview = new ExportPhrasesWindow("Export Software development", Development.Phrases, "Software development") { ShowInTaskbar = false };
        preview.Show();
        try
        {
            Flush(preview);
            Require(((TextBlock)preview.FindName("SummaryText")).Text.StartsWith("4 phrases will be written.", StringComparison.Ordinal), "Export must start with every phrase ticked.");
            Capture(preview, outputDirectory, "export-review.png");
        }
        finally
        {
            preview.Close();
        }

        var destination = Path.Combine(scratch, "export" + VocabularyPackCodec.Extension);
        var window = new ExportPhrasesWindow("Export English phrases", ["MVVM", "private-codename", "Kubernetes"], "My English phrases")
        {
            ShowInTaskbar = false,
            ChooseDestination = _ => destination
        };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var box = Descendants(window).OfType<CheckBox>().Single(check => Equals(check.Content, "private-codename"));
                box.IsChecked = false;
                Flush(window);
                Click((Button)window.FindName("SaveAsButton"));
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
        Require(File.ReadAllText(destination) == VocabularyPackCodec.Serialize(["Kubernetes", "MVVM"]),
            "Export must write exactly the ticked phrases in canonical form.");
    }

    private static AutomationPeer AutomationPeer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Missing automation peer.");

    private static void Click(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
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
