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
    Application.LoadComponent(application, new Uri("/PrivateType.App;component/App.xaml", UriKind.Relative));
    var outputDirectory = Path.Combine(Path.GetTempPath(), "live-dictation-layout-probe");
    Directory.CreateDirectory(outputDirectory);

    Render(
        new SettingsWindow(
            new PortableSettings("default", ShortcutBinding.Defaults),
            [new MicrophoneOption("default", "System default microphone")]),
        Path.Combine(outputDirectory, "settings.png"));
    Render(new DiagnosticsWindow(new InMemoryDiagnostics()), Path.Combine(outputDirectory, "diagnostics-empty.png"));
    Render(new OpenSourceLicensesWindow(), Path.Combine(outputDirectory, "open-source-licenses.png"));
    Render(new ModelSetupWindow(), Path.Combine(outputDirectory, "model-consent.png"));
    var modelSetup = new ModelSetupWindow();
    modelSetup.ShowProgress(148L * 1024 * 1024, 240L * 1024 * 1024);
    Render(modelSetup, Path.Combine(outputDirectory, "model-setup.png"));
    var runtimeRequired = new ModelSetupWindow();
    runtimeRequired.ShowRuntimePrerequisite("The bundled local speech runtime could not start.");
    Render(runtimeRequired, Path.Combine(outputDirectory, "runtime-required.png"));

    var panel = new DictationBubble();
    panel.ShowReady(PortableSettings.Default);
    Render(panel, Path.Combine(outputDirectory, "status-panel.png"));

    var hintPanel = new DictationBubble();
    hintPanel.ShowReady(PortableSettings.Default);
    typeof(DictationBubble).GetMethod("ExpandHints", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hintPanel, [null, null]);
    Render(hintPanel, Path.Combine(outputDirectory, "status-panel-hint.png"));

    var recordingPanel = new DictationBubble();
    recordingPanel.ShowReady(PortableSettings.Default);
    recordingPanel.ShowRecording(RecognitionLanguage.English);
    recordingPanel.ShowAudioMeter(new AudioMeter(0.72, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.95 : 0.24).ToArray()));
    recordingPanel.ShowTranscript("First transcript line with enough realistic words to wrap.\nSecond transcript line.\nThird transcript line.\nFourth transcript line stays latest.");
    Render(recordingPanel, Path.Combine(outputDirectory, "status-panel-recording.png"));

    var quietRecordingPanel = new DictationBubble();
    quietRecordingPanel.ShowReady(PortableSettings.Default);
    quietRecordingPanel.ShowRecording(RecognitionLanguage.English);
    quietRecordingPanel.ShowAudioMeter(new AudioMeter(0.05, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.16 : 0.03).ToArray()));
    quietRecordingPanel.ShowTranscript("Quiet speech still has a readable spectrum.");
    Render(quietRecordingPanel, Path.Combine(outputDirectory, "status-panel-recording-quiet.png"));

    var modelLoadingPanel = new DictationBubble();
    modelLoadingPanel.ShowReady(PortableSettings.Default);
    modelLoadingPanel.ShowModelLoading();
    Render(modelLoadingPanel, Path.Combine(outputDirectory, "status-panel-model-loading.png"));

    var finalizingPanel = new DictationBubble();
    finalizingPanel.ShowReady(PortableSettings.Default);
    finalizingPanel.ShowRecording(RecognitionLanguage.English);
    finalizingPanel.ShowFinalizing();
    Render(finalizingPanel, Path.Combine(outputDirectory, "status-panel-finalizing.png"));

    var cancelledPanel = new DictationBubble();
    cancelledPanel.ShowReady(PortableSettings.Default);
    cancelledPanel.ShowCancellation("Target window changed — dictation was cancelled.");
    Render(cancelledPanel, Path.Combine(outputDirectory, "status-panel-cancelled.png"));

    var errorPanel = new DictationBubble();
    errorPanel.ShowReady(PortableSettings.Default);
    errorPanel.ShowError("Couldn't reach the microphone.");
    Render(errorPanel, Path.Combine(outputDirectory, "status-panel-error.png"));

    Console.WriteLine($"PASS: Win32 x64 INPUT layout is {inputSize} bytes.");
    Console.WriteLine($"Rendered settings: {Path.Combine(outputDirectory, "settings.png")}");
    Console.WriteLine($"Rendered empty diagnostics: {Path.Combine(outputDirectory, "diagnostics-empty.png")}");
    Console.WriteLine($"Rendered open-source licenses: {Path.Combine(outputDirectory, "open-source-licenses.png")}");
    Console.WriteLine($"Rendered model consent: {Path.Combine(outputDirectory, "model-consent.png")}");
    Console.WriteLine($"Rendered first-run setup: {Path.Combine(outputDirectory, "model-setup.png")}");
    Console.WriteLine($"Rendered runtime prerequisite: {Path.Combine(outputDirectory, "runtime-required.png")}");
    Console.WriteLine($"Rendered status panel: {Path.Combine(outputDirectory, "status-panel.png")}");
    Console.WriteLine($"Rendered hint panel: {Path.Combine(outputDirectory, "status-panel-hint.png")}");
    Console.WriteLine($"Rendered recording panel: {Path.Combine(outputDirectory, "status-panel-recording.png")}");
    Console.WriteLine($"Rendered quiet recording panel: {Path.Combine(outputDirectory, "status-panel-recording-quiet.png")}");
    Console.WriteLine($"Rendered model-loading panel: {Path.Combine(outputDirectory, "status-panel-model-loading.png")}");
    Console.WriteLine($"Rendered finalizing panel: {Path.Combine(outputDirectory, "status-panel-finalizing.png")}");
    Console.WriteLine($"Rendered cancelled panel: {Path.Combine(outputDirectory, "status-panel-cancelled.png")}");
    Console.WriteLine($"Rendered error panel: {Path.Combine(outputDirectory, "status-panel-error.png")}");
}

static void Render(Window window, string outputPath)
{
    window.ShowInTaskbar = false;
    window.Show();
    window.UpdateLayout();
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
