using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrivateType.App;
using PrivateType.Core;
using Forms = System.Windows.Forms;

if (args.Length >= 3 && string.Equals(args[0], "--cache-worker", StringComparison.OrdinalIgnoreCase))
{
    RunCacheWorker(args[1], args[2], args.Length >= 4 && string.Equals(args[3], "--offline", StringComparison.OrdinalIgnoreCase));
    return;
}

var thread = new Thread(RenderWindows) { IsBackground = false };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void RenderWindows()
{
    var inputSize = Marshal.SizeOf<NativeMethods.Input>();
    if (inputSize != 40)
        throw new InvalidOperationException($"Win32 x64 INPUT must be 40 bytes; actual size was {inputSize}.");

    var application = new PrivateType.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    Application.LoadComponent(application, new Uri("/PrivateType;component/App.xaml", UriKind.Relative));
    var outputDirectory = Path.Combine(Path.GetTempPath(), "live-dictation-layout-probe");
    Directory.CreateDirectory(outputDirectory);

    Render(
        new SettingsWindow(
            new PortableSettings("default", ShortcutBinding.Defaults),
            [new MicrophoneOption("default", "System default microphone")]),
        Path.Combine(outputDirectory, "settings.png"));
    Render(new DiagnosticsWindow(new InMemoryDiagnostics()), Path.Combine(outputDirectory, "diagnostics-empty.png"));
    Render(new OpenSourceLicensesWindow(), Path.Combine(outputDirectory, "open-source-licenses.png"));
    var sharedModelSetup = new ModelSetupWindow();
    sharedModelSetup.ShowDownloadConsent();
    Render(sharedModelSetup, Path.Combine(outputDirectory, "model-consent-shared.png"), VerifyModelTermsLinkAndSharedCopy);
    var portableModelSetup = new ModelSetupWindow();
    portableModelSetup.ShowDownloadConsent(ModelStorageMode.Portable);
    Render(portableModelSetup, Path.Combine(outputDirectory, "model-consent-portable.png"), VerifyPortableCopy);
    var modelSetup = new ModelSetupWindow();
    modelSetup.ShowProgress(148L * 1024 * 1024, 240L * 1024 * 1024);
    Render(modelSetup, Path.Combine(outputDirectory, "model-setup.png"), VerifyModelProgressGeometry);
    var engineMissing = new ModelSetupWindow();
    engineMissing.ShowMissingEnginePrerequisite();
    Render(engineMissing, Path.Combine(outputDirectory, "engine-missing.png"));
    var engineCouldNotStart = new ModelSetupWindow();
    engineCouldNotStart.ShowEngineStartPrerequisite();
    Render(engineCouldNotStart, Path.Combine(outputDirectory, "engine-could-not-start.png"));
    Render(
        new StartupVersionPromptWindow(new Version(2, 4, 0), new Version(1, 9, 3)),
        Path.Combine(outputDirectory, "startup-version-prompt.png"));
    Render(
        new StartupVersionPromptWindow(null, null),
        Path.Combine(outputDirectory, "startup-version-prompt-unknown.png"));
    VerifyStartupVersionPromptChoice(useCurrent: false);
    VerifyStartupVersionPromptChoice(useCurrent: true);

    var panel = new DictationBubble();
    panel.ShowReady(PortableSettings.Default, modelLoaded: true);
    Render(panel, Path.Combine(outputDirectory, "status-panel.png"));

    var unloadedPanel = new DictationBubble();
    unloadedPanel.ShowReady(PortableSettings.Default, modelLoaded: false);
    Render(unloadedPanel, Path.Combine(outputDirectory, "status-panel-model-unloaded.png"));

    var hintPanel = new DictationBubble();
    hintPanel.ShowReady(PortableSettings.Default, modelLoaded: true);
    typeof(DictationBubble).GetMethod("ExpandHints", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hintPanel, [null, null]);
    Render(hintPanel, Path.Combine(outputDirectory, "status-panel-hint.png"));

    var recordingPanel = new DictationBubble();
    recordingPanel.ShowReady(PortableSettings.Default, modelLoaded: true);
    recordingPanel.ShowRecording(RecognitionLanguage.English);
    recordingPanel.ShowAudioMeter(new AudioMeter(0.72, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.95 : 0.24).ToArray()));
    recordingPanel.ShowTranscript("First transcript line with enough realistic words to wrap.\nSecond transcript line.\nThird transcript line.\nFourth transcript line stays latest.");
    Render(recordingPanel, Path.Combine(outputDirectory, "status-panel-recording.png"));

    var quietRecordingPanel = new DictationBubble();
    quietRecordingPanel.ShowReady(PortableSettings.Default, modelLoaded: true);
    quietRecordingPanel.ShowRecording(RecognitionLanguage.English);
    quietRecordingPanel.ShowAudioMeter(new AudioMeter(0.05, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.16 : 0.03).ToArray()));
    quietRecordingPanel.ShowTranscript("Quiet speech still has a readable spectrum.");
    Render(quietRecordingPanel, Path.Combine(outputDirectory, "status-panel-recording-quiet.png"));

    var modelLoadingPanel = new DictationBubble();
    modelLoadingPanel.ShowReady(PortableSettings.Default, modelLoaded: false);
    modelLoadingPanel.ShowModelLoading();
    Render(modelLoadingPanel, Path.Combine(outputDirectory, "status-panel-model-loading.png"));

    var finalizingPanel = new DictationBubble();
    finalizingPanel.ShowReady(PortableSettings.Default, modelLoaded: true);
    finalizingPanel.ShowRecording(RecognitionLanguage.English);
    finalizingPanel.ShowFinalizing();
    Render(finalizingPanel, Path.Combine(outputDirectory, "status-panel-finalizing.png"));

    var cancelledPanel = new DictationBubble();
    cancelledPanel.ShowReady(PortableSettings.Default, modelLoaded: true);
    cancelledPanel.ShowCancellation("Target window changed — dictation was cancelled.");
    Render(cancelledPanel, Path.Combine(outputDirectory, "status-panel-cancelled.png"));

    var errorPanel = new DictationBubble();
    errorPanel.ShowReady(PortableSettings.Default, modelLoaded: true);
    errorPanel.ShowError("Couldn't reach the microphone.");
    Render(errorPanel, Path.Combine(outputDirectory, "status-panel-error.png"));

    VerifyPointerMonitorPlacement();
    VerifyTranscriptPreviewClearsTaskbars();

    Console.WriteLine($"PASS: Win32 x64 INPUT layout is {inputSize} bytes.");
    Console.WriteLine($"Rendered settings: {Path.Combine(outputDirectory, "settings.png")}");
    Console.WriteLine($"Rendered empty diagnostics: {Path.Combine(outputDirectory, "diagnostics-empty.png")}");
    Console.WriteLine($"Rendered open-source licenses: {Path.Combine(outputDirectory, "open-source-licenses.png")}");
    Console.WriteLine($"Rendered shared model consent: {Path.Combine(outputDirectory, "model-consent-shared.png")}");
    Console.WriteLine($"Rendered portable model consent: {Path.Combine(outputDirectory, "model-consent-portable.png")}");
    Console.WriteLine($"Rendered first-run setup: {Path.Combine(outputDirectory, "model-setup.png")}");
    Console.WriteLine($"Rendered missing engine prerequisite: {Path.Combine(outputDirectory, "engine-missing.png")}");
    Console.WriteLine($"Rendered engine start prerequisite: {Path.Combine(outputDirectory, "engine-could-not-start.png")}");
    Console.WriteLine($"Rendered startup version prompt: {Path.Combine(outputDirectory, "startup-version-prompt.png")}");
    Console.WriteLine($"Rendered unknown startup version prompt: {Path.Combine(outputDirectory, "startup-version-prompt-unknown.png")}");
    Console.WriteLine($"Rendered status panel: {Path.Combine(outputDirectory, "status-panel.png")}");
    Console.WriteLine($"Rendered unloaded-model status panel: {Path.Combine(outputDirectory, "status-panel-model-unloaded.png")}");
    Console.WriteLine($"Rendered hint panel: {Path.Combine(outputDirectory, "status-panel-hint.png")}");
    Console.WriteLine($"Rendered recording panel: {Path.Combine(outputDirectory, "status-panel-recording.png")}");
    Console.WriteLine($"Rendered quiet recording panel: {Path.Combine(outputDirectory, "status-panel-recording-quiet.png")}");
    Console.WriteLine($"Rendered model-loading panel: {Path.Combine(outputDirectory, "status-panel-model-loading.png")}");
    Console.WriteLine($"Rendered finalizing panel: {Path.Combine(outputDirectory, "status-panel-finalizing.png")}");
    Console.WriteLine($"Rendered cancelled panel: {Path.Combine(outputDirectory, "status-panel-cancelled.png")}");
    Console.WriteLine($"Rendered error panel: {Path.Combine(outputDirectory, "status-panel-error.png")}");
}

static void RunCacheWorker(string cacheDirectory, string markerPath, bool offline)
{
    var payload = "synthetic two-process model"u8.ToArray();
    var manifest = new ModelManifest(
        "process-test",
        new Uri("https://example.test/process-model"),
        "process-model.gguf",
        payload.Length,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)));
    IModelDownloadClient downloader = offline
        ? new OfflineFailDownloader()
        : new ProcessMarkerDownloader(payload, markerPath);
    var provisioner = new ModelProvisioner(cacheDirectory, manifest, downloader);
    var modelPath = provisioner.EnsureAvailableAsync(null, CancellationToken.None).GetAwaiter().GetResult();
    if (!provisioner.IsAvailable())
        throw new InvalidOperationException($"Process worker did not produce a verified model: {modelPath}");

    Console.WriteLine($"PASS: synthetic {(offline ? "offline " : string.Empty)}cache worker reused/promoted {modelPath}");
}

static void VerifyModelTermsLinkAndSharedCopy(Window window)
{
    var link = window.FindName("ModelTermsLink") as System.Windows.Documents.Hyperlink
        ?? throw new InvalidOperationException("Model terms were not rendered as a hyperlink.");
    if (link.NavigateUri != new Uri("https://openmdw.ai/license/1-1/"))
        throw new InvalidOperationException($"Model terms hyperlink targets an unexpected URI: {link.NavigateUri}");
    if (!link.Focus() || !link.IsKeyboardFocused)
        throw new InvalidOperationException("Model terms hyperlink could not receive keyboard focus.");

    var storageNotice = (System.Windows.Controls.TextBlock)window.FindName("StorageNoticeText");
    if (!storageNotice.Text.Contains("shared", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Shared model setup copy does not explain shared storage.");

    Console.WriteLine("PASS: Model terms use the canonical HTTPS hyperlink and receive keyboard focus.");
}

static void VerifyPortableCopy(Window window)
{
    var storageNotice = (System.Windows.Controls.TextBlock)window.FindName("StorageNoticeText");
    if (!storageNotice.Text.Contains("app\\models", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Portable model setup copy does not explain the app\\models opt-in.");

    Console.WriteLine("PASS: Portable model setup copy explains the explicit app\\models opt-in.");
}

static void VerifyModelProgressGeometry(Window window)
{
    var setup = (ModelSetupWindow)window;
    var progress = (System.Windows.Controls.ProgressBar)setup.FindName("ProgressBar");
    progress.ApplyTemplate();
    var track = progress.Template.FindName("PART_Track", progress) as FrameworkElement
        ?? throw new InvalidOperationException("Model progress template is missing PART_Track.");
    var indicator = progress.Template.FindName("PART_Indicator", progress) as FrameworkElement
        ?? throw new InvalidOperationException("Model progress template is missing PART_Indicator.");

    VerifyModelProgressWidth(setup, track, indicator, downloaded: 0, total: 240);
    VerifyModelProgressWidth(setup, track, indicator, downloaded: 148, total: 240);
    VerifyModelProgressWidth(setup, track, indicator, downloaded: 240, total: 240);
    setup.ShowProgress(148L * 1024 * 1024, 240L * 1024 * 1024);
    setup.UpdateLayout();

    Console.WriteLine("PASS: Model progress indicator matches 0%, partial, and complete download values.");
}

static void VerifyModelProgressWidth(
    ModelSetupWindow setup,
    FrameworkElement track,
    FrameworkElement indicator,
    long downloaded,
    long total)
{
    setup.ShowProgress(downloaded * 1024 * 1024, total * 1024 * 1024);
    setup.UpdateLayout();
    var expectedWidth = track.ActualWidth * downloaded / total;
    if (Math.Abs(indicator.ActualWidth - expectedWidth) > 1)
        throw new InvalidOperationException($"Model progress indicator width was {indicator.ActualWidth:F1} at {downloaded}/{total}; expected {expectedWidth:F1}.");
}

static void VerifyTranscriptPreviewClearsTaskbars()
{
    var previews = new[]
    {
        "First preview line.\nSecond preview line.\nThird preview line.",
        "First preview line.\nSecond preview line.\nThird preview line.\nFourth preview line.",
        "First preview line."
    };

    foreach (var screen in Forms.Screen.AllScreens)
    {
        foreach (var preview in previews)
            VerifyTranscriptPreviewClearsTaskbar(screen, preview);
    }

    Console.WriteLine($"PASS: Growing transcript previews clear the taskbar on {Forms.Screen.AllScreens.Length} monitor(s).");
}

static void VerifyTranscriptPreviewClearsTaskbar(Forms.Screen screen, string preview)
{
    const double bottomClearance = 8;
    var panel = new DictationBubble();
    panel.ShowReady(PortableSettings.Default, modelLoaded: true);
    panel.Left = screen.WorkingArea.Left + 16;
    panel.Top = screen.WorkingArea.Bottom - panel.ActualHeight;
    panel.UpdateLayout();
    panel.ShowRecording(RecognitionLanguage.English);
    FlushRender(panel);
    var leftBeforePreview = panel.Left;

    panel.ShowTranscript(preview);
    panel.UpdateLayout();
    FlushRender(panel);
    var actualBottom = panel.Top + panel.ActualHeight;
    var leftAfterPreview = panel.Left;
    panel.Close();

    var maximumBottom = screen.WorkingArea.Bottom - bottomClearance;
    if (actualBottom > maximumBottom + 1)
        throw new InvalidOperationException($"Transcript preview ended at {actualBottom:F1} on {screen.DeviceName}; expected at or above {maximumBottom:F1}.");
    if (Math.Abs(leftAfterPreview - leftBeforePreview) > 1)
        throw new InvalidOperationException($"Transcript preview moved horizontally on {screen.DeviceName}.");
}

static void FlushRender(Window window)
{
    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    window.UpdateLayout();
}

static void VerifyStartupVersionPromptChoice(bool useCurrent)
{
    var prompt = new StartupVersionPromptWindow(new Version(2, 4, 0), new Version(1, 9, 3));
    Exception? loadedFailure = null;
    var loadedTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.ApplicationIdle,
        prompt.Dispatcher)
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };
    loadedTimer.Tick += (_, _) =>
    {
        if (!prompt.IsLoaded)
            return;

        loadedTimer.Stop();
        try
        {
            prompt.Activate();
            var keepButton = (System.Windows.Controls.Button)prompt.FindName("KeepButton");
            if (!keepButton.IsKeyboardFocused)
                throw new InvalidOperationException("The safe startup-version choice did not receive initial keyboard focus.");

            var buttonName = useCurrent ? "UseButton" : "KeepButton";
            var selectedButton = (System.Windows.Controls.Button)prompt.FindName(buttonName);
            selectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        }
        catch (Exception exception)
        {
            loadedFailure = exception;
            prompt.Close();
        }
    };

    loadedTimer.Start();
    var result = prompt.ShowDialog();
    if (loadedFailure is not null)
        throw new InvalidOperationException("Startup-version prompt verification failed after loading.", loadedFailure);
    if (result != useCurrent)
        throw new InvalidOperationException($"Startup-version prompt returned {result} for {useCurrent}.");

    Console.WriteLine($"PASS: Startup-version prompt returned {result} for {(useCurrent ? "Use this version" : "Keep registered version")}.");
}

static void VerifyPointerMonitorPlacement()
{
    var targetScreen = Forms.Screen.FromPoint(Forms.Cursor.Position);
    var sourceScreen = Forms.Screen.AllScreens.FirstOrDefault(
        screen => !string.Equals(screen.DeviceName, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase));
    if (sourceScreen is null)
    {
        Console.WriteLine("SKIP: Pointer-monitor transition requires at least two monitors.");
        return;
    }

    VerifyPointerMonitorPlacementThrough(
        "ready",
        panel => { },
        sourceScreen,
        targetScreen);
    VerifyPointerMonitorPlacementThrough(
        "model loading",
        panel => panel.ShowModelLoading(),
        sourceScreen,
        targetScreen);
    VerifyPointerMonitorPlacementThrough(
        "recording",
        panel => panel.ShowRecording(RecognitionLanguage.English),
        sourceScreen,
        targetScreen);
}

static void VerifyPointerMonitorPlacementThrough(
    string state,
    Action<DictationBubble> showState,
    Forms.Screen sourceScreen,
    Forms.Screen targetScreen)
{
    var panel = new DictationBubble();
    panel.ShowReady(PortableSettings.Default, modelLoaded: true);
    panel.Left = sourceScreen.WorkingArea.Left;
    panel.Top = sourceScreen.WorkingArea.Top;
    panel.UpdateLayout();
    panel.MoveToPointerScreen();
    showState(panel);
    panel.UpdateLayout();

    var center = new System.Drawing.Point(
        (int)Math.Round(panel.Left + (panel.ActualWidth / 2)),
        (int)Math.Round(panel.Top + (panel.ActualHeight / 2)));
    var actualScreen = Forms.Screen.FromPoint(center);
    panel.Close();

    if (!string.Equals(actualScreen.DeviceName, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Bubble entered {state} on {actualScreen.DeviceName} instead of pointer monitor {targetScreen.DeviceName}.");

    Console.WriteLine($"PASS: Bubble entered {state} after moving from {sourceScreen.DeviceName} to pointer monitor {targetScreen.DeviceName}.");
}

static void Render(Window window, string outputPath, Action<Window>? verify = null)
{
    if (window is not DictationBubble)
        VerifyWindowIcon(window);

    window.ShowInTaskbar = false;
    window.Show();
    window.UpdateLayout();
    verify?.Invoke(window);
    var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(window);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var output = File.Create(outputPath);
    encoder.Save(output);
    window.Close();
}

static void VerifyWindowIcon(Window window)
{
    if (window.Icon is null)
        throw new InvalidOperationException($"{window.GetType().Name} did not resolve the PrivateType window icon.");
}

sealed class ProcessMarkerDownloader(byte[] payload, string markerPath) : IModelDownloadClient
{
    public async Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        await Task.Delay(350, cancellationToken);
        await using (var marker = new FileStream(markerPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        await using (var writer = new StreamWriter(marker))
            await writer.WriteLineAsync("download");

        await destination.WriteAsync(payload, cancellationToken);
        progress?.Report(payload.Length);
    }
}

sealed class OfflineFailDownloader : IModelDownloadClient
{
    public Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("Offline cache worker attempted an unexpected download."));
}
