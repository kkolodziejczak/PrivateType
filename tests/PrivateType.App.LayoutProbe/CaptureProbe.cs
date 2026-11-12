using System.IO;
using System.Runtime.CompilerServices;
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
        Draw(NewSettings(), output, "settings", "settings-general-focus", window => FocusKeyboard(window, (Control)window.FindName("InsertionModeBox")));
        Draw(NewSettings(), output, "settings", "settings-general-picker-open", window => ((ComboBox)window.FindName("InsertionModeBox")).IsDropDownOpen = true);
        Draw(NewSettings(), output, "settings", "settings-dirty", window => ((ComboBox)window.FindName("ShortcutModeBox")).SelectedIndex = 1);
        Draw(NewSettings(), output, "settings", "settings-short", window => ((SettingsWindow)window).FitToWorkArea(600));
        Draw(NewSettings(), output, "settings", "settings-vocabulary", window => Page(window, "VocabularyTab"));
        Draw(NewSettings(), output, "settings", "settings-vocabulary-end", window =>
        {
            Page(window, "VocabularyTab");
            Flush(window);
            Scroll(window).ScrollToBottom();
        });
        Draw(NewSettings(), output, "settings", "settings-vocabulary-edit", window =>
        {
            Page(window, "VocabularyTab");
            Flush(window);
            FocusKeyboard(window, Descendants((ItemsControl)window.FindName("VocabularyList")).OfType<TextBox>().First());
        });
        var nemotron = Sample with { SpeechModel = SpeechModelCatalog.NemotronId };
        Draw(new SettingsWindow(nemotron, Microphones, new ProbeModelStore(SpeechModelCatalog.NemotronId, SpeechModelCatalog.ParakeetId)),
            output, "settings", "settings-vocabulary-boost", window => Page(window, "VocabularyTab"));
        var graphicsCard = new EngineDevice("vulkan:1", "NVIDIA GeForce MX450", EngineDeviceKind.GraphicsCard);
        Draw(NewSettings(ModelDeviceStatus.Of(graphicsCard, fellBack: false, graphicsCard)), output, "settings", "settings-model",
            window => Page(window, "ModelTab"));
        var store = new ProbeModelStore(SpeechModelCatalog.DefaultId);
        Draw(new SettingsWindow(Sample, Microphones, store, deviceStatus: ModelDeviceStatus.Of(graphicsCard, fellBack: false, graphicsCard)),
            output, "settings", "settings-model-downloading", window =>
            {
                Page(window, "ModelTab");
                var models = (ModelLibraryEditor)((FrameworkElement)window.FindName("ModelPage")).DataContext;
                _ = models.DownloadAsync(models.Rows.Single(row => row.Model.Id == SpeechModelCatalog.NemotronId));
                Flush(window);
                store.PendingProgress!.Report(Nemotron.Manifest.ExpectedBytes * 2 / 5);
            });
    }

    private static SpeechModelDefinition Nemotron => SpeechModelCatalog.Nemotron;

    private static SettingsWindow NewSettings(ModelDeviceStatus? deviceStatus = null) =>
        new(Sample, Microphones, new ProbeModelStore(SpeechModelCatalog.DefaultId), deviceStatus: deviceStatus);

    private static void Page(Window window, string tab) => ((RadioButton)window.FindName(tab)).IsChecked = true;

    // Keyboard focus needs an active window; the window stays off screen.
    private static void FocusKeyboard(Window window, IInputElement element)
    {
        window.Activate();
        Flush(window);
        System.Windows.Input.Keyboard.Focus(element);
    }

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
        recording.ShowTranscript(SpokenText);
        Draw(recording, output, "bubble", "bubble-recording");
        var quiet = Ready(loaded: true);
        quiet.ShowRecording("en-US");
        quiet.ShowAudioMeter(new AudioMeter(0.72, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.95 : 0.24).ToArray()));
        Draw(quiet, output, "bubble", "bubble-recording-quiet");
        var finalizing = Ready(loaded: true);
        finalizing.ShowRecording("en-US");
        finalizing.ShowFinalizing();
        Draw(finalizing, output, "bubble", "bubble-finalizing");
        var finalizingPreview = Ready(loaded: true);
        finalizingPreview.ShowRecording("en-US");
        finalizingPreview.ShowTranscript(SpokenText);
        finalizingPreview.ShowFinalizing();
        Draw(finalizingPreview, output, "bubble", "bubble-finalizing-preview");
        var kept = Ready(loaded: true);
        kept.ShowCancellation($"Text not inserted: the active window changed. {DictationApplication.KeptTextHint(KeyChord.DefaultHistory)}");
        Draw(kept, output, "bubble", "bubble-kept");
        var error = Ready(loaded: true);
        error.ShowError(DictationApplication.DictationStoppedTitle, DictationApplication.DictationStoppedText("Couldn't reach the microphone."));
        Draw(error, output, "bubble", "bubble-error");
    }

    // What the recognizer hands the bubble: one running text, wrapped by the bubble itself.
    private const string SpokenText = "Quick update on the release before the weekend. The installer is ready and the notes are written. I will send the download link to the team today and check the feedback on Monday morning.";

    private static DictationBubble Ready(bool loaded)
    {
        var bubble = new DictationBubble();
        bubble.ShowReady(PortableSettings.Default, modelLoaded: loaded);
        return bubble;
    }

    private static void ModelSetup(string output)
    {
        Draw(Consent(SpeechModelCatalog.ParakeetId, ticked: false), output, "model-setup", "setup-consent");
        Draw(Consent(SpeechModelCatalog.ParakeetId, ticked: true), output, "model-setup", "setup-consent-ticked");
        Draw(Consent(SpeechModelCatalog.NemotronId, ticked: false), output, "model-setup", "setup-consent-nemotron");
        Draw(Consent(SpeechModelCatalog.NemotronId, ticked: true), output, "model-setup", "setup-consent-nemotron-ticked");
        // The app reports the chosen model's manifest size as the download total.
        var total = SpeechModelCatalog.Parakeet.Manifest.ExpectedBytes;
        var download = new ModelSetupWindow();
        download.ShowProgress(400L * 1024 * 1024, total);
        Draw(download, output, "model-setup", "setup-download");
        var verifying = new ModelSetupWindow();
        verifying.ShowProgress(total, total);
        Draw(verifying, output, "model-setup", "setup-verifying");
        var failure = new ModelSetupWindow();
        failure.ShowProgress(400L * 1024 * 1024, total);
        failure.ShowFailure("Unable to read data from the transport connection.");
        Draw(failure, output, "model-setup", "setup-failure");
        var missing = new ModelSetupWindow();
        missing.ShowMissingEnginePrerequisite();
        Draw(missing, output, "model-setup", "setup-engine-missing");
    }

    private static ModelSetupWindow Consent(string modelId, bool ticked)
    {
        var consent = new ModelSetupWindow(modelId);
        consent.ShowDownloadConsent();
        ((CheckBox)consent.FindName("TermsCheckBox")).IsChecked = ticked;
        return consent;
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
        Draw(new TeachWindow(new FinalizedDictation(sentence, "en-US"), _ => null), output, "teach", "teach-start");
        Draw(new TeachWindow(new FinalizedDictation(sentence, "en-US"), _ => null), output, "teach", "teach-fix", window =>
        {
            var chips = Descendants((ItemsControl)window.FindName("WordList")).OfType<ToggleButton>().ToList();
            Toggle(chips[3]);
            Flush(window);
            Toggle(chips[5]);
            Flush(window);
            ((TextBox)window.FindName("DesiredBox")).Text = "MVVM";
        });
        Draw(new TeachWindow(new FinalizedDictation(sentence, "en-US"), _ => null), output, "teach", "teach-fixes-pending", window =>
        {
            var teach = (TeachWindow)window;
            AddFix(teach, 3, 5, "MVVM");
            AddFix(teach, 8, 8, "settings view");
        });
        Draw(new TeachWindow(new FinalizedDictation(sentence, "en-US"), _ => null), output, "teach", "teach-saved", window =>
        {
            var teach = (TeachWindow)window;
            AddFix(teach, 3, 5, "MVVM");
            Click((Button)teach.FindName("SaveButton"));
        });
        const string longSentence = "After the stand-up we agreed to move the cube nets deployment to the staging cluster, rerun the post gress migration on Thursday, and ask the wire guard maintainers whether the new handshake timeout is configurable per peer.";
        Draw(new TeachWindow(new FinalizedDictation(longSentence, "en-US"), _ => null), output, "teach", "teach-long", window =>
        {
            var teach = (TeachWindow)window;
            AddFix(teach, 9, 10, "Kubernetes");
            teach.ActivateWord(17);
            teach.ActivateWord(18);
            Flush(window);
            ((TextBox)teach.FindName("DesiredBox")).Text = "PostgreSQL";
        });
    }

    // Selects the words first..last, types the correction and adds it as a pending fix.
    private static void AddFix(TeachWindow window, int first, int last, string correction)
    {
        window.ActivateWord(first);
        if (last != first)
            window.ActivateWord(last);
        Flush(window);
        ((TextBox)window.FindName("DesiredBox")).Text = correction;
        Flush(window);
        Click((Button)window.FindName("AddFixButton"));
        Flush(window);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

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
        const string longPack = "Mechanical engineering and additive manufacturing for production tooling";
        Draw(new ExportPhrasesWindow($"Export {longPack}", ["fused deposition modelling", "selective laser sintering", "thermoplastic polyurethane filament",
            "Fusion 360 parametric timeline", "PrusaSlicer variable layer height"], longPack), output, "export", "export-review-long");
    }

    private static void Prompts(string output)
    {
        var copy = new EarlierCopy(@"D:\Apps\PrivateType 1.3.0", new Version(1, 3, 0), new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));
        Draw(new ImportSettingsPromptWindow(copy), output, "import-prompt", "import-prompt");
        var deepCopy = new EarlierCopy(@"D:\Users\Shared\Portable applications\Dictation tools\PrivateType portable releases\PrivateType 1.3.0 (September build)",
            new Version(1, 3, 0), new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));
        Draw(new ImportSettingsPromptWindow(deepCopy), output, "import-prompt", "import-prompt-long-path");
        Draw(new StartupVersionPromptWindow(new Version(2, 4, 0), new Version(1, 9, 3)), output, "version-prompt", "version-prompt");
        Draw(new DiagnosticsWindow(new InMemoryDiagnostics()), output, "diagnostics", "diagnostics-empty");
        Draw(new DiagnosticsWindow(SampleDiagnostics()), output, "diagnostics", "diagnostics-entries");
        Draw(new OpenSourceLicensesWindow(), output, "licenses", "licenses");
        Draw(new OpenSourceLicensesWindow(BundledLicenses()), output, "licenses", "licenses-bundled");
    }

    // Three warnings and one error, with only the event names and details the log keeps; no dictated text.
    private static InMemoryDiagnostics SampleDiagnostics()
    {
        var diagnostics = new InMemoryDiagnostics();
        void Record(int minute, string name, Exception? error = null, params (string Key, string Value)[] details) =>
            diagnostics.Record(new DictationDiagnostic("a1b2c3", Now.AddMinutes(minute), name, details.ToDictionary(pair => pair.Key, pair => pair.Value), error));
        Record(-14, "insertion.skipped", details: [("reason", "foreground-changed"), ("characters", "142")]);
        Record(-9, "model.load.failed", details: [("device", "vulkan:1")]);
        Record(-6, "session.failed", new InvalidOperationException("synthetic"), ("phase", "capture.start"), ("state", "Recording"));
        Record(-2, "cue.skipped", details: [("source", "ready"), ("reason", "muted")]);
        return diagnostics;
    }

    // The licenses folder a portable release ships, written from the notices text in the release script.
    private static string BundledLicenses([CallerFilePath] string source = "")
    {
        var script = File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "Build-PortableRelease.ps1")).Replace("\r\n", "\n");
        var start = script.IndexOf("@'\nPrivateType third-party notices", StringComparison.Ordinal) + 3;
        var end = script.IndexOf("\n'@ | Set-Content", start, StringComparison.Ordinal);
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "privatetype-capture-licenses")).FullName;
        File.WriteAllText(Path.Combine(folder, "THIRD-PARTY-NOTICES.txt"), script[start..end]);
        return folder;
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
            // Some windows place themselves under the pointer; take the content out of hit testing so nothing draws hovered.
            if (window.Content is UIElement content)
                content.IsHitTestVisible = false;
            foreach (var popup in Descendants(window).OfType<Popup>().Where(popup => popup.Child is not null))
                popup.Child.IsHitTestVisible = false;
            Flush(window);
            System.Windows.Input.Mouse.Synchronize();
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
                DrawOpenPopups(window, context);
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

    // An open dropdown is its own window, so it is drawn where it opens: below the control that owns it.
    private static void DrawOpenPopups(Window window, DrawingContext context)
    {
        foreach (var popup in Descendants(window).OfType<Popup>().Where(popup => popup.IsOpen && popup.Child is FrameworkElement))
        {
            var child = (FrameworkElement)popup.Child;
            var owner = (FrameworkElement)VisualTreeHelper.GetParent(popup);
            var origin = owner.TranslatePoint(new Point(0, owner.ActualHeight + child.Margin.Top), window);
            var size = new Size(child.ActualWidth, child.ActualHeight);
            var brush = new VisualBrush(child) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size) };
            context.DrawRectangle(brush, null, new Rect(new Point(Backdrop + origin.X, Backdrop + origin.Y), size));
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
