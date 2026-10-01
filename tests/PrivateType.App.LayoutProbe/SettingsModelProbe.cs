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

// A fake store: no network, no files. Downloads finish only when the probe releases them.
internal sealed class ProbeModelStore(params string[] presentIds) : IModelStore
{
    public static ProbeModelStore Empty => new(SpeechModelCatalog.DefaultId);

    private readonly HashSet<string> present = [.. presentIds];
    public TaskCompletionSource? PendingDownload { get; private set; }
    public IProgress<long>? PendingProgress { get; private set; }
    public List<string> Deleted { get; } = [];
    public TaskCompletionSource? DeleteGate { get; set; }

    public ModelStorageMode StorageMode => ModelStorageMode.Shared;
    public bool IsPresent(SpeechModelDefinition model) => present.Contains(model.Id);

    public async Task DownloadAsync(SpeechModelDefinition model, IProgress<long> progress, CancellationToken cancellationToken)
    {
        PendingDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingProgress = progress;
        await PendingDownload.Task.WaitAsync(cancellationToken);
        present.Add(model.Id);
    }

    public async Task DeleteAsync(SpeechModelDefinition model, CancellationToken cancellationToken)
    {
        if (DeleteGate is not null)
            await DeleteGate.Task;
        Deleted.Add(model.Id);
        present.Remove(model.Id);
    }
}

internal static class SettingsModelProbe
{
    public static void Run(string outputDirectory)
    {
        VerifyModelPage(outputDirectory);
        VerifySavedChoice();
        VerifySetupChoice(outputDirectory);
        Console.WriteLine("PASS: Model page lists both models, downloads with progress and cancel, blocks saving mid-download, selects, protects the model in use from deletion, deletes with confirmation, and saves the choice; setup offers both models with per-model terms.");
    }

    private static void VerifyModelPage(string outputDirectory)
    {
        var store = new ProbeModelStore(SpeechModelCatalog.NemotronId);
        var window = new SettingsWindow(PortableSettings.Default, [new MicrophoneOption("default", "System default microphone")], store) { ShowInTaskbar = false };
        window.ShowModelPage();
        window.Show();
        try
        {
            Flush(window);
            var models = window.Models;
            var nemotron = models.Rows.Single(row => row.Model.Id == SpeechModelCatalog.NemotronId);
            var parakeet = models.Rows.Single(row => row.Model.Id == SpeechModelCatalog.ParakeetId);
            Require(((FrameworkElement)window.FindName("ModelPage")).Visibility == Visibility.Visible, "The Model tab must show the Model page.");
            Require(nemotron.StatusText == "In use" && !nemotron.CanDelete && !nemotron.CanUse, "The model in use must not offer Use or Delete.");
            Require(parakeet.StatusText == "Not downloaded" && parakeet.CanDownload && !parakeet.CanUse, "A missing model must offer only Download.");
            Require(VisibleButtons(window).Contains("Download Parakeet TDT 0.6B v3"), "The Download button must be visible and named for the model.");
            RequireNoClipping(window);
            Capture(window, outputDirectory, "settings-model.png");

            Click(window, VisibleButton(window, "Download Parakeet TDT 0.6B v3"));
            store.PendingProgress!.Report(300L * 1024 * 1024);
            Flush(window);
            Require(parakeet.IsDownloading && parakeet.StatusText.StartsWith("Downloading… 300 of 681 MiB", StringComparison.Ordinal), $"Progress must be shown; got '{parakeet.StatusText}'.");
            Require(VisibleButtons(window).Contains("Cancel Parakeet TDT 0.6B v3 download"), "A running download must offer Cancel.");
            Capture(window, outputDirectory, "settings-model-downloading.png");
            Click(window, (Button)window.FindName("SaveSettingsButton"));
            Require(((TextBlock)window.FindName("ValidationText")).Text.Contains("download", StringComparison.OrdinalIgnoreCase), "Saving mid-download must explain why it waits.");

            Click(window, VisibleButton(window, "Cancel Parakeet TDT 0.6B v3 download"));
            WaitFor(window, () => !parakeet.IsDownloading);
            Require(!parakeet.IsDownloading && parakeet.CanDownload, "Cancel must return the model to Not downloaded.");

            Click(window, VisibleButton(window, "Download Parakeet TDT 0.6B v3"));
            store.PendingDownload!.SetResult();
            WaitFor(window, () => !parakeet.IsDownloading);
            Require(parakeet.IsPresent && parakeet.CanUse && parakeet.CanDelete, "A downloaded model must offer Use and Delete.");

            Click(window, VisibleButton(window, "Use Parakeet TDT 0.6B v3"));
            Require(models.SelectedId == SpeechModelCatalog.ParakeetId && parakeet.StatusText.StartsWith("Selected", StringComparison.Ordinal), "Use must select the model until save.");
            Require(nemotron.StatusText == "In use until you save" && !nemotron.CanDelete && nemotron.CanUse, "The model still in use must stay undeletable but selectable.");
            Require(!parakeet.CanDelete, "The selected model must not be deletable.");
            Capture(window, outputDirectory, "settings-model-selected.png");

            Click(window, VisibleButton(window, "Use Nemotron 3.5 ASR Streaming 0.6B"));
            window.ConfirmModelDeletion = _ => false;
            Click(window, VisibleButton(window, "Delete Parakeet TDT 0.6B v3"));
            Require(store.Deleted.Count == 0 && parakeet.IsPresent, "Declining the confirmation must keep the model.");
            window.ConfirmModelDeletion = model => model.Id == SpeechModelCatalog.ParakeetId;
            store.DeleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(window, VisibleButton(window, "Delete Parakeet TDT 0.6B v3"));
            Require(parakeet.IsDeleting && parakeet.StatusText == "Deleting…" && !parakeet.CanUse && !parakeet.CanDelete, "A deletion in progress must not offer Use or Delete.");
            Click(window, (Button)window.FindName("SaveSettingsButton"));
            Require(((TextBlock)window.FindName("ValidationText")).Text.Contains("deletion", StringComparison.OrdinalIgnoreCase), "Saving mid-deletion must explain why it waits.");
            store.DeleteGate.SetResult();
            WaitFor(window, () => !parakeet.IsDeleting);
            Require(store.Deleted.SequenceEqual([SpeechModelCatalog.ParakeetId]) && !parakeet.IsPresent && parakeet.CanDownload, "Confirmed deletion must remove the model.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifySavedChoice()
    {
        var store = new ProbeModelStore(SpeechModelCatalog.NemotronId, SpeechModelCatalog.ParakeetId);
        var window = new SettingsWindow(PortableSettings.Default, [new MicrophoneOption("default", "System default microphone")], store) { ShowInTaskbar = false };
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                window.ShowModelPage();
                Flush(window);
                Click(window, VisibleButton(window, "Use Parakeet TDT 0.6B v3"));
                var preview = (CheckBox)window.FindName("OfflinePreviewCheckBox");
                Require(preview.IsChecked == true && preview.IsVisible, "The live preview option must be visible and on by default.");
                preview.IsChecked = false;
                Click(window, (Button)window.FindName("SaveSettingsButton"));
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
        Require(window.SavedSettings?.SpeechModel == SpeechModelCatalog.ParakeetId, "Saved settings must carry the chosen speech model.");
        Require(window.SavedSettings?.OfflinePreview == false, "Saved settings must carry the live preview choice.");
    }

    private static void VerifySetupChoice(string outputDirectory)
    {
        var setup = new ModelSetupWindow(SpeechModelCatalog.NemotronId) { ShowInTaskbar = false };
        setup.ShowDownloadConsent();
        setup.Show();
        try
        {
            Flush(setup);
            var terms = (CheckBox)setup.FindName("TermsCheckBox");
            var link = (System.Windows.Documents.Hyperlink)setup.FindName("ModelTermsLink");
            Require(link.NavigateUri == SpeechModelCatalog.Nemotron.LicenseUri, "Setup must start with the default model's terms.");
            terms.IsChecked = true;
            var cards = Descendants(setup).OfType<RadioButton>().ToList();
            Require(cards.Count == 2, "Setup must offer both models.");
            cards[1].IsChecked = true;
            Flush(setup);
            Require(setup.ChosenModel.Id == SpeechModelCatalog.ParakeetId, "Choosing a card must choose its model.");
            Require(link.NavigateUri == SpeechModelCatalog.Parakeet.LicenseUri, "Terms must follow the chosen model.");
            Require(terms.IsChecked == false && !((Button)setup.FindName("DownloadButton")).IsEnabled, "Changing the model must ask for consent again.");
            Require(((TextBlock)setup.FindName("TermsText")).Text.Contains("CC-BY-4.0", StringComparison.Ordinal), "Consent must name the chosen model's terms.");
            Capture(setup, outputDirectory, "model-consent-parakeet.png");
        }
        finally
        {
            setup.Close();
        }
    }

    private static List<string> VisibleButtons(Window window) =>
        Descendants(window).OfType<Button>().Where(button => button.IsVisible).Select(button => AutomationPeer(button).GetName()).ToList();

    private static Button VisibleButton(Window window, string name) =>
        Descendants(window).OfType<Button>().FirstOrDefault(button => button.IsVisible && AutomationPeer(button).GetName() == name)
        ?? throw new InvalidOperationException($"No visible button named '{name}'. Visible: {string.Join(", ", VisibleButtons(window))}");

    // Every visible model-card text and button must fit inside the page width.
    private static void RequireNoClipping(Window window)
    {
        var page = (FrameworkElement)window.FindName("ModelPage");
        foreach (var element in Descendants(page).OfType<FrameworkElement>().Where(element => element.IsVisible && element is Button or TextBlock))
        {
            var bounds = element.TransformToAncestor(page).TransformBounds(new Rect(element.RenderSize));
            Require(bounds.Left >= -0.5 && bounds.Right <= page.ActualWidth + 0.5, $"A model page element is clipped horizontally ({bounds.Left:F1}–{bounds.Right:F1} of {page.ActualWidth:F1}).");
        }
    }

    private static AutomationPeer AutomationPeer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException("Missing automation peer.");

    private static void Click(Window window, Button button)
    {
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

    // Async continuations hop through the thread pool, so pump until the state settles.
    private static void WaitFor(Window window, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            Flush(window);
        }
        Flush(window);
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
