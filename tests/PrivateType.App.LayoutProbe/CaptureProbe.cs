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
        Draw(NewSettings(), output, "settings", "settings-general-picker-open", window =>
        {
            var picker = (ComboBox)window.FindName("InsertionModeBox");
            FocusKeyboard(window, picker);
            picker.IsDropDownOpen = true;
        });
        // A pointer over a picker and over a switch row: each window is placed so that control lies under the pointer where it already is.
        Draw(NewSettings(), output, "settings", "settings-general-hover", window => Hover(window, (FrameworkElement)window.FindName("MicrophoneBox")));
        Draw(NewSettings(), output, "settings", "settings-general-hover-switch", window => Hover(window, (FrameworkElement)window.FindName("ClipboardHistoryCheckBox")));
        var threeShortcuts = Sample with { Shortcuts = [.. ShortcutBinding.Defaults, new("de-DE", 0x47)] };
        Draw(new SettingsWindow(threeShortcuts, Microphones, new ProbeModelStore(SpeechModelCatalog.DefaultId)), output, "settings", "settings-general-added-shortcut");
        Draw(NewSettings(), output, "settings", "settings-dirty", window => ((ComboBox)window.FindName("ShortcutModeBox")).SelectedIndex = 1);
        // An edit on General, then another page: what the footer shows for a change made elsewhere.
        Draw(NewSettings(), output, "settings", "settings-dirty-cross-page", window =>
        {
            ((ComboBox)window.FindName("ShortcutModeBox")).SelectedIndex = 1;
            Flush(window);
            Page(window, "VocabularyTab");
        });
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

    // The one window drawn with the pointer over it, and where; every other window is drawn with hit testing off.
    private static Window? hovered;
    private static Point hoverPoint;

    // Marks the element's centre as the point to hover. The real pointer never moves: just before drawing, the window is put
    // under it where it already is and told the mouse moved there.
    private static void Hover(Window window, FrameworkElement element)
    {
        hovered = window;
        hoverPoint = element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), window);
    }

    // Windows and WPF only take a mouse move over a window that is really under the pointer.
    private static void MoveMouseOver(Window window, Point at)
    {
        var toDevice = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
        NativeMethods.GetCursorPos(out var cursor);
        var pointer = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformFromDevice.Transform(new Point(cursor.X, cursor.Y));
        window.Topmost = true;
        window.Left = pointer.X - at.X;
        window.Top = pointer.Y - at.Y;
        Flush(window);
        var device = toDevice.Transform(at);
        var position = ((int)Math.Round(device.Y) << 16) | ((int)Math.Round(device.X) & 0xFFFF);
        SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, WmMouseMove, 0, position);
        System.Windows.Input.Mouse.Synchronize();
        window.UpdateLayout();
    }

    private const int WmMouseMove = 0x0200;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

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
        quiet.ShowAudioMeter(new AudioMeter(0.05, Enumerable.Range(0, 44).Select(index => index is > 15 and < 28 ? 0.16 : 0.03).ToArray()));
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
        var loadFailed = Ready(loaded: false);
        loadFailed.ShowError(DictationApplication.ModelLoadFailedTitle, DictationApplication.ModelLoadFailedText);
        Draw(loadFailed, output, "bubble", "bubble-load-failed");
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
        // A portable-local copy has the longest storage note.
        Draw(Consent(SpeechModelCatalog.ParakeetId, ticked: false, ModelStorageMode.Portable), output, "model-setup", "setup-consent-portable");
        Draw(Consent(SpeechModelCatalog.NemotronId, ticked: false), output, "model-setup", "setup-consent-focus", window =>
            FocusKeyboard(window, Descendants((ItemsControl)window.FindName("ModelChoices")).OfType<RadioButton>().ElementAt(1)));
        // The app reports the chosen model's manifest size as the download total.
        var total = SpeechModelCatalog.Parakeet.Manifest.ExpectedBytes;
        var downloadStart = new ModelSetupWindow();
        downloadStart.ShowProgress(0, total);
        Draw(downloadStart, output, "model-setup", "setup-download-start");
        var download = new ModelSetupWindow();
        download.ShowProgress(400L * 1024 * 1024, total);
        Draw(download, output, "model-setup", "setup-download");
        var verifying = new ModelSetupWindow();
        verifying.ShowProgress(total, total);
        Draw(verifying, output, "model-setup", "setup-verifying");
        var failure = new ModelSetupWindow();
        failure.ShowProgress(400L * 1024 * 1024, total);
        failure.ShowFailure("Unable to read data from the transport connection.", connectionDropped: true);
        Draw(failure, output, "model-setup", "setup-failure");
        // A failure that is not a dropped connection, with a system message long enough to wrap.
        var otherFailure = new ModelSetupWindow();
        otherFailure.ShowProgress(400L * 1024 * 1024, total);
        otherFailure.ShowFailure("The downloaded file did not match the size the server reported. Expected 714080256 bytes and received 419430400 bytes.", connectionDropped: false);
        Draw(otherFailure, output, "model-setup", "setup-failure-other");
        var missing = new ModelSetupWindow();
        missing.ShowMissingEnginePrerequisite();
        Draw(missing, output, "model-setup", "setup-engine-missing");
        var couldNotStart = new ModelSetupWindow();
        couldNotStart.ShowEngineStartPrerequisite();
        Draw(couldNotStart, output, "model-setup", "setup-engine-start");
    }

    private static ModelSetupWindow Consent(string modelId, bool ticked, ModelStorageMode storage = ModelStorageMode.Shared)
    {
        var consent = new ModelSetupWindow(modelId);
        consent.ShowDownloadConsent(storage);
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
        // The not-inserted row selected from the keyboard: its actions are the row's own keys (Enter pastes, Del removes).
        Draw(new DictationHistoryWindow(history, clock, () => 100), output, "history", "history-list-focus", window =>
        {
            ((DictationHistoryWindow)window).Refresh(selectIndex: 1);
            Flush(window);
            var entries = (ListBox)window.FindName("EntriesList");
            FocusKeyboard(window, (IInputElement)entries.ItemContainerGenerator.ContainerFromIndex(1));
        });
        Draw(new DictationHistoryWindow(new DictationHistory(clock), clock, () => 100, Sample), output, "history", "history-empty");
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
            AddFix(teach, 8, 9, "Kubernetes");
            teach.ActivateWord(17);
            teach.ActivateWord(18);
            Flush(window);
            ((TextBox)teach.FindName("DesiredBox")).Text = "PostgreSQL";
        });
        // A range that runs from the end of one line of words onto the next, beside a set-aside fix longer than the words it replaces.
        Draw(new TeachWindow(new FinalizedDictation(longSentence, "en-US"), _ => null), output, "teach", "teach-range-wrap", window =>
        {
            var teach = (TeachWindow)window;
            AddFix(teach, 25, 26, "WireGuard VPN tunnel");
            teach.ActivateWord(17);
            teach.ActivateWord(19);
            Flush(window);
            ((TextBox)teach.FindName("DesiredBox")).Text = "PostgreSQL migration";
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
        Draw(new ExportPhrasesWindow("Software development", Development.Phrases, "Software development"), output, "export", "export-review");
        const string longPack = "Mechanical engineering and additive manufacturing for production tooling";
        string[] longPhrases = ["fused deposition modelling", "selective laser sintering", "thermoplastic polyurethane filament",
            "Fusion 360 parametric timeline", "PrusaSlicer variable layer height"];
        Draw(new ExportPhrasesWindow(longPack, longPhrases, longPack), output, "export", "export-review-long");
        // Some and none of the same five phrases ticked.
        Draw(new ExportPhrasesWindow(longPack, longPhrases, longPack), output, "export", "export-review-partial", window => Untick(window, 2));
        Draw(new ExportPhrasesWindow(longPack, longPhrases, longPack), output, "export", "export-review-none", window => Untick(window, longPhrases.Length));
    }

    private static void Untick(Window window, int count)
    {
        foreach (var box in Descendants(window).OfType<CheckBox>().Take(count))
            box.IsChecked = false;
    }

    private static void Prompts(string output)
    {
        var copy = new EarlierCopy(@"D:\Apps\PrivateType 1.3.0", new Version(1, 3, 0), new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));
        Draw(new ImportSettingsPromptWindow(copy), output, "import-prompt", "import-prompt");
        var deepCopy = new EarlierCopy(@"D:\Users\Shared\Portable applications\Dictation tools\PrivateType portable releases\PrivateType 1.3.0 (September build)",
            new Version(1, 3, 0), new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc));
        Draw(new ImportSettingsPromptWindow(deepCopy), output, "import-prompt", "import-prompt-long-path");
        Draw(new StartupVersionPromptWindow(new Version(2, 4, 0), new Version(1, 9, 3)), output, "version-prompt", "version-prompt");
        Draw(new DiagnosticsWindow(new InMemoryDiagnostics(), Clock), output, "diagnostics", "diagnostics-empty");
        Draw(new DiagnosticsWindow(SampleDiagnostics(), Clock), output, "diagnostics", "diagnostics-entries");
        // The Error row reached with the arrow keys: selected, focused and open on its raw line.
        Draw(new DiagnosticsWindow(SampleDiagnostics(), Clock), output, "diagnostics", "diagnostics-row-focus", window =>
        {
            var entries = (ListBox)window.FindName("Entries");
            entries.SelectedIndex = 0;
            Flush(window);
            FocusKeyboard(window, (IInputElement)entries.ItemContainerGenerator.ContainerFromIndex(0));
        });
        Draw(new DiagnosticsWindow(BusyDiagnostics(), Clock), output, "diagnostics", "diagnostics-busy");
        Draw(new DiagnosticsWindow(InformationOnlyDiagnostics(), Clock), output, "diagnostics", "diagnostics-info-only");
        Draw(new OpenSourceLicensesWindow(), output, "licenses", "licenses");
        Draw(new OpenSourceLicensesWindow(BundledLicenses(everyFile: false)), output, "licenses", "licenses-bundled");
        Draw(new OpenSourceLicensesWindow(BundledLicenses(everyFile: false)), output, "licenses", "licenses-bundled-end",
            window => ((RichTextBox)window.FindName("CreditsPage")).ScrollToEnd());
        Draw(new OpenSourceLicensesWindow(BundledLicenses(everyFile: true)), output, "licenses", "licenses-bundled-full");
    }

    // The person's clock in every diagnostics capture: two hours ahead of the UTC timestamps the log keeps, whatever zone the capture runs in.
    private static readonly TimeZoneInfo Clock = TimeZoneInfo.CreateCustomTimeZone("capture", TimeSpan.FromHours(2), "capture", "capture");

    // Three warnings and one error, as the code records them: the application's own events under "application", each
    // dictation under its own session ID. The newest problem is a warning, and an older error sits under it.
    private static InMemoryDiagnostics SampleDiagnostics()
    {
        var diagnostics = new InMemoryDiagnostics();
        void Record(string session, int minute, string name, Exception? error = null, params (string Key, string Value)[] details) =>
            diagnostics.Record(new DictationDiagnostic(session, Now.AddMinutes(minute), name, details.ToDictionary(pair => pair.Key, pair => pair.Value), error));
        Record("application", -14, "engine.warm-up.failed", details: [("device", "vulkan:1")]);
        Record("d41a07", -9, "injection.skipped", details: [("reason", "empty-transcript")]);
        Record("e8802c", -6, "session.failed", new InvalidOperationException("synthetic"), ("phase", "capture.start"), ("state", "Ready"));
        Record("f3c95b", -2, "injection.skipped", details: [("reason", "target-ineligible"), ("targetEligibility", "Changed"), ("characters", "142")]);
        return diagnostics;
    }

    // A real log: the application and four dictations, each under its own session ID. Most entries are information;
    // the four problems of the short sample are among them. Only event names the code records and the details the log keeps.
    private static InMemoryDiagnostics BusyDiagnostics()
    {
        var diagnostics = new InMemoryDiagnostics();
        var at = Now.AddMinutes(-21);
        void Record(string session, int secondsLater, string name, (string Key, string Value)[] details, Exception? error = null)
        {
            at = at.AddSeconds(secondsLater);
            diagnostics.Record(new DictationDiagnostic(session, at, name, details.ToDictionary(pair => pair.Key, pair => pair.Value), error));
        }

        void Dictation(string session, int secondsLater, string recorded, string took, string characters, string? skipped = null)
        {
            Record(session, secondsLater, "session.started", [("locale", "en-US"), ("vocabularyPhrases", "4")]);
            Record(session, 9, "session.state", [("state", "Finalizing")]);
            Record(session, 0, "session.finalizing", [("timeoutMilliseconds", "15000")]);
            Record("application", 0, "cue.transcribing", [("recordedSeconds", recorded), ("predictedSeconds", took), ("announced", "False")]);
            Record("application", 1, "transcription.coverage", [("voicedSeconds", recorded), ("lastVoicedSeconds", recorded), ("lastWordSeconds", recorded), ("recoveredCharacters", "0")]);
            Record("application", 0, "transcription.timed", [("recordedSeconds", recorded), ("tookSeconds", took)]);
            if (skipped is null)
            {
                Record(session, 0, "injection.started", [("characters", characters)]);
                Record(session, 0, "injection.completed", [("characters", characters)]);
            }
            else
            {
                Record(session, 0, "injection.skipped", skipped == "empty-transcript"
                    ? [("reason", skipped)]
                    : [("reason", skipped), ("targetEligibility", "Changed"), ("characters", characters)]);
            }

            Record(session, 0, "session.completed", []);
            Record(session, 0, "session.state", [("state", "Ready")]);
        }

        Record("application", 0, "model.standby", []);
        Record("application", 0, "model.loading", []);
        Record("application", 1, "engine.started", [("device", "vulkan:1")]);
        Record("application", 2, "engine.warm-up.failed", [("device", "vulkan:1")]);
        Record("application", 0, "engine.fallback", [("device", "vulkan:1")]);
        Record("application", 3, "engine.started", [("device", "cpu")]);
        Record("application", 1, "engine.warmed", [("device", "cpu")]);
        Record("application", 0, "model.loaded", []);
        Dictation("a5c210", 290, "8.4", "1.2", "96", skipped: "empty-transcript");
        Dictation("d41a07", 62, "13.2", "1.1", "142", skipped: "target-ineligible");
        Record("application", 44, "history.opened", [("source", "shortcut"), ("foreground", "True")]);
        Record("application", 6, "history.pasted", []);
        Record("application", 0, "history.closed", [("reason", "pasted"), ("heldForeground", "True")]);
        Record("e8802c", 214, "session.started", [("locale", "pl-PL")]);
        Record("e8802c", 1, "session.failed", [("phase", "capture.start"), ("state", "Ready")], new InvalidOperationException("synthetic"));
        Record("e8802c", 0, "session.state", [("state", "Error")]);
        Dictation("f3c95b", 240, "21.4", "1.8", "264");
        Record("application", 30, "model.unload.scheduled", [("minutes", "10")]);
        return diagnostics;
    }

    // Nothing failed, but the log is not empty: one start and one dictation, all information.
    private static InMemoryDiagnostics InformationOnlyDiagnostics()
    {
        var diagnostics = new InMemoryDiagnostics();
        void Record(string session, int second, string name, params (string Key, string Value)[] details) =>
            diagnostics.Record(new DictationDiagnostic(session, Now.AddMinutes(-4).AddSeconds(second), name, details.ToDictionary(pair => pair.Key, pair => pair.Value)));
        Record("application", 0, "model.standby");
        Record("application", 0, "model.loading");
        Record("application", 1, "engine.started", ("device", "vulkan:1"));
        Record("application", 4, "engine.warmed", ("device", "vulkan:1"));
        Record("application", 4, "model.loaded");
        Record("d41a07", 62, "session.started", ("locale", "en-US"));
        Record("application", 75, "transcription.timed", ("recordedSeconds", "11.8"), ("tookSeconds", "0.9"));
        Record("d41a07", 75, "injection.completed", ("characters", "126"));
        return diagnostics;
    }

    // The licenses folder a portable release ships. The window shows the app's own credits and only opens the notices, so
    // they are a stand-in; with every file, the other license texts lie beside them under the names the release uses:
    // PrivateType's own license in full, and a short stand-in for each text the release copies from a package or the engine.
    private static string BundledLicenses(bool everyFile, [CallerFilePath] string source = "")
    {
        var repository = Path.Combine(Path.GetDirectoryName(source)!, "..", "..");
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), everyFile ? "privatetype-capture-licenses-full" : "privatetype-capture-licenses")).FullName;
        File.WriteAllText(Path.Combine(folder, OpenSourceLicensesWindow.NoticesFileName),
            "Stand-in for the capture: the release writes the notices here from Credits.json.");
        if (!everyFile)
            return folder;

        File.Copy(Path.Combine(repository, "LICENSE"), Path.Combine(folder, "PrivateType-MIT.txt"), overwrite: true);
        (string Name, string Text)[] standIns =
        [
            ("NAudio-MIT.txt", "MIT License\n\nStand-in for the capture: the release ships the NAudio 2.2.1 license text here."),
            ("VulkanRT-License.txt", "Stand-in for the capture: the release ships the Vulkan loader's MIT and Apache-2.0 texts here."),
            ("dotnet-LICENSE.txt", "The MIT License (MIT)\n\nStand-in for the capture: the release ships the .NET runtime license here."),
            ("dotnet-THIRD-PARTY-NOTICES.txt", "Stand-in for the capture: the release ships the .NET runtime's third-party notices here."),
            (Path.Combine("NeMo-Speech.cpp", "LICENSE"), "Apache License, Version 2.0\n\nStand-in for the capture: the release ships the engine's license here."),
            (Path.Combine("NeMo-Speech.cpp", "NOTICE"), "Stand-in for the capture: the release ships NVIDIA's NOTICE here."),
            (Path.Combine("NeMo-Speech.cpp", "third_party", "ggml", "LICENSE"), "MIT License\n\nStand-in for the capture: the release ships the ggml license here.")
        ];
        foreach (var (file, text) in standIns)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, file))!);
            File.WriteAllText(Path.Combine(folder, file), text);
        }

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
            // An open dropdown holds the mouse capture and would close without it, so its window keeps hit testing; it is off screen.
            if (window.Content is UIElement content && System.Windows.Input.Mouse.Captured is null && window != hovered)
                content.IsHitTestVisible = false;
            foreach (var popup in Descendants(window).OfType<Popup>().Where(popup => popup.Child is not null))
                popup.Child.IsHitTestVisible = false;
            Flush(window);
            System.Windows.Input.Mouse.Synchronize();
            Flush(window);
            // Last, so nothing settles between the pointer arriving and the drawing.
            if (window == hovered)
                MoveMouseOver(window, hoverPoint);
            // The window's own pixels, one to one: its size in whole pixels at the capture scale, so nothing is resampled.
            var width = Pixels(window.ActualWidth) / Scale;
            var height = Pixels(window.ActualHeight) / Scale;
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
            hovered = null;
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
            var origin = owner.TranslatePoint(new Point(0, owner.ActualHeight), window);
            // The brush draws the child at its own offset in the popup (its margin), so the viewbox is taken there and drawn there below the owner.
            var offset = VisualTreeHelper.GetOffset(child);
            var bounds = Rect.Union(VisualTreeHelper.GetContentBounds(child), VisualTreeHelper.GetDescendantBounds(child));
            bounds.Offset(offset);
            var brush = new VisualBrush(child) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds };
            context.DrawRectangle(brush, null, new Rect(Backdrop + origin.X + bounds.X, Backdrop + origin.Y + bounds.Y, bounds.Width, bounds.Height));
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
