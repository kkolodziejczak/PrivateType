using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrivateType.App;
using PrivateType.Core;

// Draws every window in its realistic states as <screen>/<state>-dark.png for design review, with fixed synthetic data,
// at the owner's display scale, composited on a plain desktop colour. It checks nothing; the layout probe does that.
internal static class CaptureProbe
{
    private const double Scale = 1.25;
    private const double Backdrop = 24;
    private static readonly Color Desktop = Color.FromRgb(0x5A, 0x63, 0x6E);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly MicrophoneOption[] Microphones = [new("default", "System default microphone")];
    private static readonly VocabularyPack Development = new("Software development", VocabularyScopes.Shared, IsEnabled: true,
        ["GitHub", "TypeScript", "pull request", "CI pipeline"]);

    private static PortableSettings Sample => PortableSettings.Default with
    {
        Vocabulary = [new("PrivateType", VocabularyScopes.Shared), new("Kubernetes", VocabularyScopes.Shared), new("PostgreSQL", VocabularyScopes.Shared), new("WireGuard", VocabularyScopes.Shared)],
        VocabularyCorrections = [new("three D printing", "3D printing", VocabularyScopes.Shared), new("fusion three sixty", "Fusion 360", VocabularyScopes.Shared)],
        VocabularyPacks = [Development]
    };

    public static void Run(string output)
    {
        Settings(output);
        Bubble(output);
        ModelSetup(output);
        History(output);
        Teach(output);
        Vocabulary(output);
        Prompts(output);
    }

    private static void Settings(string output)
    {
        Draw(NewSettings(), output, "settings", "settings-general");
        Draw(NewSettings(), output, "settings", "settings-general-end", window => Scroll(window).ScrollToBottom());
        Draw(NewSettings(), output, "settings", "settings-vocabulary", window => ((RadioButton)window.FindName("VocabularyTab")).IsChecked = true);
        var graphicsCard = new EngineDevice("vulkan:1", "NVIDIA GeForce MX450", EngineDeviceKind.GraphicsCard);
        Draw(NewSettings(ModelDeviceStatus.Of(graphicsCard, fellBack: false, graphicsCard)), output, "settings", "settings-model",
            window => ((RadioButton)window.FindName("ModelTab")).IsChecked = true);
    }

    private static SettingsWindow NewSettings(ModelDeviceStatus? deviceStatus = null) =>
        new(Sample, Microphones, new ProbeModelStore(SpeechModelCatalog.DefaultId), deviceStatus: deviceStatus);

    private static ScrollViewer Scroll(Window window) => (ScrollViewer)window.FindName("SettingsScrollViewer");

    private static void Bubble(string output)
    {
        Draw(Ready(loaded: true), output, "bubble", "bubble-ready");
        Draw(Ready(loaded: false), output, "bubble", "bubble-unloaded");
        var loading = Ready(loaded: false);
        loading.ShowModelLoading();
        Draw(loading, output, "bubble", "bubble-loading");
        var recording = Ready(loaded: true);
        recording.ShowRecording("en-US");
        recording.ShowAudioMeter(new AudioMeter(0.72, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.95 : 0.24).ToArray()));
        recording.ShowTranscript("Quick update on the release before the weekend.\nThe installer is ready and the notes are written.\nI will send the download link to the team today\nand check the feedback on Monday morning.");
        Draw(recording, output, "bubble", "bubble-recording");
        var finalizing = Ready(loaded: true);
        finalizing.ShowRecording("en-US");
        finalizing.ShowFinalizing();
        Draw(finalizing, output, "bubble", "bubble-finalizing");
        var kept = Ready(loaded: true);
        kept.ShowCancellation($"Text not inserted: the active window changed. {DictationApplication.KeptTextHint(KeyChord.DefaultHistory)}");
        Draw(kept, output, "bubble", "bubble-kept");
        var error = Ready(loaded: true);
        error.ShowError("Couldn't reach the microphone.");
        Draw(error, output, "bubble", "bubble-error");
    }

    private static DictationBubble Ready(bool loaded)
    {
        var bubble = new DictationBubble();
        bubble.ShowReady(PortableSettings.Default, modelLoaded: loaded);
        return bubble;
    }

    private static void ModelSetup(string output)
    {
        var consent = new ModelSetupWindow();
        consent.ShowDownloadConsent();
        Draw(consent, output, "model-setup", "setup-consent");
        var download = new ModelSetupWindow();
        download.ShowProgress(148L * 1024 * 1024, 240L * 1024 * 1024);
        Draw(download, output, "model-setup", "setup-download");
        var missing = new ModelSetupWindow();
        missing.ShowMissingEnginePrerequisite();
        Draw(missing, output, "model-setup", "setup-engine-missing");
    }

    private static void History(string output)
    {
        var clock = new FixedClock(Now.AddDays(-1));
        var history = new DictationHistory(clock);
        history.Add(new FinalizedDictation("Order more filament before the weekend print run.", "en-US"));
        clock.Now = Now.AddMinutes(-42);
        history.Add(new FinalizedDictation("Przypomnij mi o spotkaniu z zespołem w czwartek rano.", "pl-PL"));
        clock.Now = Now.AddMinutes(-3);
        history.Add(new FinalizedDictation("Thanks for the review. I moved the retry into the download client and kept the timeout where it was, so the settings page no longer waits on the network before it opens.", "en-US") { Inserted = false });
        clock.Now = Now;
        history.Add(new FinalizedDictation("Short note.", "auto"));
        Draw(new DictationHistoryWindow(history, clock, () => 100), output, "history", "history-list");
        Draw(new DictationHistoryWindow(new DictationHistory(clock), clock, () => 100), output, "history", "history-empty");
    }

    private static void Teach(string output)
    {
        const string sentence = "The app uses model view model for the settings screen.";
        Draw(new TeachWindow(new FinalizedDictation(sentence, "en-US"), "normal", _ => null), output, "teach", "teach-start");
        Draw(new TeachWindow(new FinalizedDictation(sentence, "en-US"), "normal", _ => null), output, "teach", "teach-fix", window =>
        {
            var chips = Descendants((ItemsControl)window.FindName("WordList")).OfType<ToggleButton>().ToList();
            Toggle(chips[3]);
            Flush(window);
            Toggle(chips[5]);
            Flush(window);
            ((TextBox)window.FindName("DesiredBox")).Text = "MVVM";
        });
    }

    private static void Toggle(ToggleButton chip)
    {
        chip.IsChecked = chip.IsChecked != true;
        chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void Vocabulary(string output)
    {
        var importing = new VocabularyEditor([], "normal", [Development]);
        Draw(new ImportPackWindow("software-development" + VocabularyPackCodec.Extension, ["Grafana", "Terraform", "Zigbee"], importing),
            output, "import-pack", "import-pack-preview", window => ((ComboBox)window.FindName("ScopeBox")).SelectedValue = "en");
        var editing = new VocabularyEditor([], "normal", [Development]) { ShowAllPacks = true };
        Draw(new PackEditorWindow(editing.VisiblePacks.Single(), editing), output, "pack-editor", "pack-editor");
        Draw(new ExportPhrasesWindow("Export Software development", Development.Phrases, "Software development"), output, "export", "export-review");
    }

    private static void Prompts(string output)
    {
        var copy = new EarlierCopy(@"D:\Apps\PrivateType 1.3.0", new Version(1, 3, 0), new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));
        Draw(new ImportSettingsPromptWindow(copy), output, "import-prompt", "import-prompt");
        Draw(new StartupVersionPromptWindow(new Version(2, 4, 0), new Version(1, 9, 3)), output, "version-prompt", "version-prompt");
        Draw(new DiagnosticsWindow(new InMemoryDiagnostics()), output, "diagnostics", "diagnostics-empty");
        Draw(new OpenSourceLicensesWindow(), output, "licenses", "licenses");
    }

    // Shows the window without focus, lets it settle, stops animations so two captures match, and draws it on the desktop colour.
    private static void Draw(Window window, string output, string screen, string state, Action<Window>? arrange = null)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        if (window is not DictationBubble)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
        }

        window.Show();
        try
        {
            Flush(window);
            if (arrange is not null)
            {
                arrange(window);
                Flush(window);
            }

            StopAnimations(window);
            Flush(window);
            var width = Math.Ceiling(window.ActualWidth);
            var height = Math.Ceiling(window.ActualHeight);
            var shot = new RenderTargetBitmap(Pixels(width), Pixels(height), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            shot.Render(window);

            var scene = new DrawingVisual();
            using (var context = scene.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Desktop), null, new Rect(0, 0, width + 2 * Backdrop, height + 2 * Backdrop));
                context.DrawImage(shot, new Rect(Backdrop, Backdrop, width, height));
            }

            var composed = new RenderTargetBitmap(Pixels(width + 2 * Backdrop), Pixels(height + 2 * Backdrop), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            composed.Render(scene);
            var folder = Directory.CreateDirectory(Path.Combine(output, screen)).FullName;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(composed, PixelFormats.Rgb24, null, 0)));
            using var file = File.Create(Path.Combine(folder, $"{state}-dark.png"));
            encoder.Save(file);
        }
        finally
        {
            window.Close();
        }
    }

    private static int Pixels(double dips) => Math.Max(1, (int)Math.Round(dips * Scale));

    private static void StopAnimations(DependencyObject root)
    {
        foreach (var element in Descendants(root).Prepend(root))
        {
            if (element is not IAnimatable { HasAnimatedProperties: true } animated)
                continue;
            var properties = new List<DependencyProperty>();
            var local = element.GetLocalValueEnumerator();
            while (local.MoveNext())
                properties.Add(local.Current.Property);
            foreach (var property in properties.Append(UIElement.OpacityProperty).Distinct())
            {
                if (!property.ReadOnly && property.GetMetadata(element) is not UIPropertyMetadata { IsAnimationProhibited: true })
                    animated.BeginAnimation(property, null);
            }
        }
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

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
