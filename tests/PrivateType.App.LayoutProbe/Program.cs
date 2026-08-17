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
    Render(new ModelSetupWindow(), Path.Combine(outputDirectory, "model-consent.png"), VerifyModelTermsLink);
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
    Console.WriteLine($"Rendered model consent: {Path.Combine(outputDirectory, "model-consent.png")}");
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

static void VerifyModelTermsLink(Window window)
{
    var link = window.FindName("ModelTermsLink") as System.Windows.Documents.Hyperlink
        ?? throw new InvalidOperationException("Model terms were not rendered as a hyperlink.");
    if (link.NavigateUri != new Uri("https://openmdw.ai/license/1-1/"))
        throw new InvalidOperationException($"Model terms hyperlink targets an unexpected URI: {link.NavigateUri}");
    if (!link.Focus() || !link.IsKeyboardFocused)
        throw new InvalidOperationException("Model terms hyperlink could not receive keyboard focus.");

    Console.WriteLine("PASS: Model terms use the canonical HTTPS hyperlink and receive keyboard focus.");
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
    prompt.Loaded += (_, _) =>
    {
        var keepButton = (System.Windows.Controls.Button)prompt.FindName("KeepButton");
        if (!keepButton.IsKeyboardFocused)
            throw new InvalidOperationException("The safe startup-version choice did not receive initial keyboard focus.");

        var buttonName = useCurrent ? "UseButton" : "KeepButton";
        var selectedButton = (System.Windows.Controls.Button)prompt.FindName(buttonName);
        prompt.Dispatcher.BeginInvoke(() => selectedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
    };

    var result = prompt.ShowDialog();
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

    var panel = new DictationBubble();
    panel.ShowReady(PortableSettings.Default, modelLoaded: true);
    panel.Left = sourceScreen.WorkingArea.Left;
    panel.Top = sourceScreen.WorkingArea.Top;
    panel.UpdateLayout();
    panel.MoveToPointerScreen();
    panel.UpdateLayout();

    var center = new System.Drawing.Point(
        (int)Math.Round(panel.Left + (panel.ActualWidth / 2)),
        (int)Math.Round(panel.Top + (panel.ActualHeight / 2)));
    var actualScreen = Forms.Screen.FromPoint(center);
    panel.Close();

    if (!string.Equals(actualScreen.DeviceName, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Bubble moved to {actualScreen.DeviceName} instead of pointer monitor {targetScreen.DeviceName}.");

    Console.WriteLine($"PASS: Bubble moved from {sourceScreen.DeviceName} to pointer monitor {targetScreen.DeviceName}.");
}

static void Render(Window window, string outputPath, Action<Window>? verify = null)
{
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
