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

internal static class ImportProbe
{
    public static void Run(string outputDirectory)
    {
        var root = Path.Combine(Path.GetTempPath(), $"privatetype-import-probe-{Guid.NewGuid():N}");
        try
        {
            var release = Path.Combine(root, "PrivateType 1.3.0");
            new PortableSettingsStore(Path.Combine(release, "app", "data")).Save(PortableSettings.Default with
            {
                Vocabulary = [new VocabularyEntry("WireGuard", VocabularyScopes.Shared)],
                StartWithWindows = true
            });
            var empty = Path.Combine(root, "Photos");
            Directory.CreateDirectory(empty);

            VerifyPrompt(outputDirectory, release);
            VerifySettingsImport(outputDirectory, release, empty);
            VerifyNotice(outputDirectory);
            VerifyActiveModelStaysProtected();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Console.WriteLine("PASS: The import prompt names the earlier copy and its folder, defaults to Import, and treats × as Start fresh; Settings imports a chosen copy for review, refuses a folder without settings, and shows the review notice.");
    }

    private static void VerifyPrompt(string outputDirectory, string release)
    {
        var copy = EarlierCopyFinder.FromChosenFolder(release) ?? throw new InvalidOperationException("The probe copy must be found.");
        var prompt = new ImportSettingsPromptWindow(copy) { ShowInTaskbar = false, ShowActivated = false };
        prompt.Show();
        try
        {
            Flush(prompt);
            var source = (TextBlock)prompt.FindName("SourceText");
            Require(source.Text == $"Found PrivateType (version unknown) in {release}.", $"The prompt must name the copy and its folder; got '{source.Text}'.");
            Require(((Button)prompt.FindName("ImportButton")).IsDefault == false && ((Button)prompt.FindName("StartFreshButton")).IsCancel,
                "Esc and × must mean Start fresh.");
            Require(source.TranslatePoint(new Point(source.ActualWidth, 0), prompt).X <= prompt.ActualWidth, "The source line must fit.");
            Capture(prompt, outputDirectory, "import-prompt.png");
        }
        finally
        {
            prompt.Close();
        }
    }

    private static void VerifySettingsImport(string outputDirectory, string release, string empty)
    {
        var window = Create(notice: null);
        var folders = new Queue<string?>([empty, release]);
        window.ChooseCopyFolder = () => folders.Dequeue();
        EarlierCopy? confirmed = null;
        window.ConfirmImport = copy => { confirmed = copy; return true; };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
                scroll.ScrollToBottom();
                Flush(window);
                var button = (Button)window.FindName("ImportFromCopyButton");
                Require(button.IsVisible, "Other copies must offer the import button at the bottom of General.");
                Capture(window, outputDirectory, "settings-other-copies.png");

                Click(window, button);
                Require(((TextBlock)window.FindName("ValidationText")).Text.StartsWith("No PrivateType settings were found", StringComparison.Ordinal) && window.IsVisible,
                    "A folder without settings must be refused with a reason.");
                Click(window, button);
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
        Require(result == false && confirmed is not null && window.ImportedFrom == confirmed, "Confirming must close Settings with the chosen copy.");
        Require(window.ImportedSettings is { StartWithWindows: false } imported && imported.Vocabulary.Single().Phrase == "WireGuard",
            "The imported settings must carry the copy's vocabulary and keep this copy's Windows startup choice.");
    }

    private static void VerifyNotice(string outputDirectory)
    {
        var window = Create("Showing settings from PrivateType 1.3. Choose Save changes to use them.");
        window.ShowActivated = false;
        window.Show();
        try
        {
            Flush(window);
            Require(((TextBlock)window.FindName("ValidationText")).Text.StartsWith("Showing settings from PrivateType 1.3", StringComparison.Ordinal),
                "Reopened Settings must say the values came from the earlier copy.");
            Capture(window, outputDirectory, "settings-import-review.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyActiveModelStaysProtected()
    {
        var store = new ProbeModelStore(SpeechModelCatalog.NemotronId, SpeechModelCatalog.ParakeetId);
        var window = new SettingsWindow(PortableSettings.Default with { SpeechModel = SpeechModelCatalog.ParakeetId },
            [new MicrophoneOption("default", "System default microphone")], store, activeModelId: SpeechModelCatalog.NemotronId) { ShowInTaskbar = false, ShowActivated = false };
        window.Show();
        try
        {
            Flush(window);
            var active = window.Models.Rows.Single(row => row.Model.Id == SpeechModelCatalog.NemotronId);
            var imported = window.Models.Rows.Single(row => row.Model.Id == SpeechModelCatalog.ParakeetId);
            Require(!active.CanDelete && imported.StatusText == "Selected — used after you save",
                "While reviewing imported settings, the model in use must stay protected and the imported one waits for Save.");
        }
        finally
        {
            window.Close();
        }
    }

    private static SettingsWindow Create(string? notice) =>
        new(PortableSettings.Default, [new MicrophoneOption("default", "System default microphone")], ProbeModelStore.Empty, notice: notice) { ShowInTaskbar = false };

    private static void Click(Window window, Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        Flush(window);
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
