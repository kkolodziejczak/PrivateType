using Microsoft.Win32;
using PrivateType.Core;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;

namespace PrivateType.App;

internal sealed class DictationApplication : IDisposable
{
    private readonly EngineHost engine = new();
    private readonly ModelReadySound modelReadySound = new();
    private readonly EngineLoadCoordinator engineLoads;
    private readonly HoldHotkeyHook hotkey = new();
    private readonly DictationBubble bubble = new();
    private readonly HttpModelDownloadClient downloader = new();
    private readonly TrayIconSet trayIcons = new();
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ToolStripMenuItem statusItem;
    private readonly Forms.ToolStripMenuItem settingsItem;
    private readonly Forms.ToolStripMenuItem vocabularyItem;
    private readonly DictationSessionCoordinator sessions;
    private readonly LatestPresentationQueue pendingPresentations = new();
    private readonly LatestAudioMeterQueue pendingAudioMeters = new();
    private readonly WindowsStartupRegistration windowsStartup = new();
    private readonly DispatcherTimer modelIdleTimer;
    private readonly DispatcherTimer heldKeyWatchdog;
    private readonly InMemoryDiagnostics diagnostics = new();
    private SettingsWindow? openSettingsWindow;
    private TeachWindow? openTeachWindow;
    private readonly EphemeralTranscriptBuffer lastDictation = new();
    private long dictationSequence;
    private PortableSettingsStore? settingsStore;
    private readonly SpeechModelLibrary models;
    private SpeechModelDefinition activeModel = SpeechModelCatalog.Get(SpeechModelCatalog.DefaultId);
    private PortableSettings settings = PortableSettings.Default;
    private CancellationTokenSource? provisioningCancellation;
    private string? modelPath;
    private long heldGeneration;
    private bool shortcutHeld;
    private int presentationVersion;
    private bool disposed;

    public DictationApplication()
    {
        models = new SpeechModelLibrary(downloader);
        engineLoads = new EngineLoadCoordinator(LoadEngineAsync, () => engine.IsReady && engine.LoadedModelPath == modelPath);
        sessions = new DictationSessionCoordinator(CreateSession);
        trayIcon = new Forms.NotifyIcon
        {
            Icon = trayIcons.Ready,
            Text = "PrivateType",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        var versionItem = (Forms.ToolStripMenuItem)trayIcon.ContextMenuStrip.Items.Add(TrayVersionText(ApplicationVersion.Current));
        versionItem.Enabled = false;
        statusItem = (Forms.ToolStripMenuItem)trayIcon.ContextMenuStrip.Items.Add("Starting…");
        statusItem.Enabled = false;
        settingsItem = (Forms.ToolStripMenuItem)trayIcon.ContextMenuStrip.Items.Add("Settings…", null, (_, _) => ShowSettings());
        settingsItem.Enabled = false;
        vocabularyItem = (Forms.ToolStripMenuItem)trayIcon.ContextMenuStrip.Items.Add("Vocabulary…", null, (_, _) => ShowSettings(openVocabulary: true));
        vocabularyItem.Enabled = false;
        trayIcon.ContextMenuStrip.Items.Add("Quit", null, (_, _) => Quit());
        modelIdleTimer = new DispatcherTimer();
        modelIdleTimer.Tick += UnloadModelWhenIdle;
        heldKeyWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        heldKeyWatchdog.Tick += ReleaseShortcutIfKeyIsUp;
        hotkey.Held += localeCode => Wpf.Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = BeginDictationAsync(localeCode)));
        hotkey.Released += () => Wpf.Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = EndDictationAsync()));
        bubble.PositionChanged += SavePanelPosition;
        bubble.SettingsRequested += () => ShowSettings();
        bubble.VocabularyRequested += () => ShowSettings(openVocabulary: true);
        bubble.TeachRequested += ShowTeach;
        lastDictation.Changed += () => bubble.SetTeachAvailable(lastDictation.HasValue);
        bubble.QuitRequested += Quit;
        bubble.RecordingIndicatorChanged += visible => trayIcon.Icon = visible ? trayIcons.Listening : trayIcons.Ready;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    internal static string TrayVersionText(Version? version) => ApplicationVersion.Label(version);

    internal static readonly TimeSpan SessionShutdownTimeout = TimeSpan.FromSeconds(3);

    public void Start() => _ = InitializeAsync();

    private void Quit()
    {
        // Close the bubble menu first so its popup window is torn down by a live dispatcher.
        bubble.CloseMenu();
        Wpf.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Wpf.Application.Current.Shutdown));
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        // Each step is isolated so one failure cannot skip the rest of the teardown.
        DisposeStep(lastDictation.Clear);
        DisposeStep(modelIdleTimer.Stop);
        DisposeStep(heldKeyWatchdog.Stop);
        DisposeStep(() => provisioningCancellation?.Cancel());
        DisposeStep(hotkey.Dispose);
        DisposeStep(WaitForSessionsToFinish);
        DisposeStep(bubble.CloseMenu);
        DisposeStep(bubble.Close);
        DisposeStep(trayIcon.Dispose);
        DisposeStep(trayIcons.Dispose);
        DisposeStep(engine.Dispose);
        DisposeStep(modelReadySound.Dispose);
        DisposeStep(downloader.Dispose);
        DisposeStep(() => provisioningCancellation?.Dispose());
    }

    // Bounded: the UI thread waits here, so a stuck session must not hang exit forever.
    private void WaitForSessionsToFinish()
    {
        var finish = sessions.DisposeAsync().AsTask();
        if (!finish.Wait(SessionShutdownTimeout))
            RecordDiagnostic("shutdown.sessions.timeout");
    }

    private void DisposeStep(Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            RecordDiagnostic("shutdown.step.failed", exception);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RecoverBubbleLater("display");

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            RecoverBubbleLater("resume");
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            RecoverBubbleLater("session");
    }

    // System events arrive on their own thread and before displays settle, so
    // recover on the dispatcher after a short delay.
    private void RecoverBubbleLater(string reason)
    {
        _ = Wpf.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            RecoverBubble(reason);
        }));
    }

    private void RecoverBubble(string reason)
    {
        // Only once dictation is configured; during first-run setup the bubble is intentionally unshown.
        if (disposed || settingsStore is null || modelPath is null)
            return;

        try
        {
            bubble.RecoverPlacement(settings);
            RecordDiagnostic("bubble.recovered", details: [("reason", reason)]);
        }
        catch (Exception exception)
        {
            RecordDiagnostic("bubble.recover.failed", exception);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            PortablePaths.EnsureWritable();
            LegacyDiagnosticsCleanup.DeleteKnownLogs(PortablePaths.DataDirectory, diagnostics);
            settingsStore = new PortableSettingsStore(PortablePaths.DataDirectory);
            var loaded = settingsStore.Load();
            settings = ReconcileStartupPreference(loaded.Settings);
            activeModel = SpeechModelCatalog.Get(settings.SpeechModel);
            var modelStorage = models.Storage(activeModel);
            var modelProvisioner = models.Provisioner(activeModel);
            if (loaded.Warning is not null)
                trayIcon.ShowBalloonTip(5000, "PrivateType", loaded.Warning, Forms.ToolTipIcon.Warning);
            var microphones = MicrophoneCatalog.Enumerate();
            settings = settings with { MicrophoneId = MicrophoneCatalog.MigrateLegacyId(settings.MicrophoneId, microphones) };
            if (!microphones.Any(microphone => microphone.Id == settings.MicrophoneId))
                trayIcon.ShowBalloonTip(5000, "PrivateType", "The saved microphone is unavailable; the system default will be used until you choose another microphone.", Forms.ToolTipIcon.Warning);

            if (modelProvisioner.IsAvailable())
                await ConfigureReadyAsync(modelProvisioner.ModelPath);
            else
                ShowModelSetup(modelStorage);
        }
        catch (Exception exception)
        {
            ShowStartupFailure(exception.Message);
        }
    }

    private PortableSettings ReconcileStartupPreference(PortableSettings loadedSettings)
    {
        try
        {
            var registration = windowsStartup.Capture();
            if (!registration.HasPrivateTypeRegistration && !registration.HasLegacyRegistration)
                return loadedSettings with { StartWithWindows = false };

            if (registration.HasPrivateTypeRegistration && registration.HasLegacyRegistration)
            {
                windowsStartup.RemoveLegacy();
                registration = registration with { LegacyCommand = null };
            }

            var currentExecutablePath = ExecutablePath();
            var registeredTarget = windowsStartup.ReadTarget(registration);
            var currentVersion = WindowsStartupRegistration.VersionOf(currentExecutablePath);
            var decision = StartupOwnershipPolicy.Decide(
                registration.HasPrivateTypeRegistration,
                registration.HasLegacyRegistration,
                registeredTarget.ExecutablePath,
                registeredTarget.Version,
                currentExecutablePath,
                currentVersion);

            if (decision == StartupOwnershipDecision.ClaimCurrent)
            {
                windowsStartup.Claim(currentExecutablePath);
                RecordDiagnostic("startup.claimed");
            }
            else if (decision == StartupOwnershipDecision.ConfirmCurrent)
            {
                var prompt = new StartupVersionPromptWindow(registeredTarget.Version, currentVersion);
                if (prompt.ShowDialog() == true)
                {
                    windowsStartup.Claim(currentExecutablePath);
                    RecordDiagnostic("startup.claimed.confirmed");
                }
                else
                {
                    RecordDiagnostic("startup.claim.declined");
                }
            }

            return loadedSettings with { StartWithWindows = true };
        }
        catch (Exception exception)
        {
            RecordDiagnostic("startup.failed", exception);
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"Windows startup setting could not be updated: {exception.Message}", Forms.ToolTipIcon.Warning);
            return loadedSettings;
        }
    }

    private void ShowModelSetup(ModelStorageLocation modelStorage)
    {
        statusItem.Text = "Model setup required";
        var window = new ModelSetupWindow(activeModel.Id);
        window.RetryRequested += () => ShowModelDownloadOrRuntimeRequirement(window, modelStorage.Mode);
        window.DownloadRequested += model => _ = ProvisionModelAsync(window, model);
        window.CancelRequested += () => Wpf.Application.Current.Shutdown();
        window.SetStorageMode(modelStorage.Mode);
        window.Show();
        ShowModelDownloadOrRuntimeRequirement(window, modelStorage.Mode);
    }

    private static void ShowModelDownloadOrRuntimeRequirement(ModelSetupWindow window, ModelStorageMode storageMode)
    {
        switch (EngineHost.VerifyPrerequisites())
        {
            case EnginePrerequisiteStatus.Ready:
                window.ShowDownloadConsent(storageMode);
                break;
            case EnginePrerequisiteStatus.MissingEngine:
                window.ShowMissingEnginePrerequisite(storageMode);
                break;
            case EnginePrerequisiteStatus.CouldNotStart:
                window.ShowEngineStartPrerequisite(storageMode);
                break;
            default:
                throw new InvalidOperationException("Unknown local engine prerequisite state.");
        }
    }

    private async Task ProvisionModelAsync(ModelSetupWindow window, SpeechModelDefinition model)
    {
        if (settingsStore is null || provisioningCancellation is not null)
            return;

        provisioningCancellation = new CancellationTokenSource();
        try
        {
            var storageMode = models.Storage(model).Mode;
            var progress = new Progress<long>(downloaded => window.ShowProgress(downloaded, model.Manifest.ExpectedBytes, storageMode));
            var modelPath = await models.Provisioner(model).EnsureAvailableAsync(progress, provisioningCancellation.Token);
            if (settings.SpeechModel != model.Id)
            {
                var chosen = settings with { SpeechModel = model.Id };
                settingsStore.Save(chosen);
                settings = chosen;
            }
            activeModel = model;
            window.CloseAfterSuccess();
            await StartEngineAfterProvisioningAsync(modelPath);
        }
        catch (OperationCanceledException)
        {
            statusItem.Text = "Model setup cancelled";
        }
        catch (Exception exception)
        {
            window.ShowFailure(exception.Message);
            statusItem.Text = "Model setup failed";
        }
        finally
        {
            provisioningCancellation?.Dispose();
            provisioningCancellation = null;
        }
    }

    private async Task StartEngineAfterProvisioningAsync(string modelPath)
    {
        try
        {
            await ConfigureReadyAsync(modelPath);
        }
        catch (Exception exception)
        {
            ShowStartupFailure($"The local model is verified, but PrivateType could not become ready: {exception.Message}");
        }
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            settingsStore?.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            RecordDiagnostic("model.choice.save.failed", exception);
        }
    }

    // Verifies the newly chosen model off the UI thread, then reloads the engine with it.
    // A damaged file keeps the previous model and says how to recover.
    private async Task SwitchModelAsync(SpeechModelDefinition model)
    {
        RecordDiagnostic("model.switch", details: [("model", model.Id)]);
        var provisioner = models.Provisioner(model);
        bool verified;
        try
        {
            verified = await Task.Run(provisioner.IsAvailable);
        }
        catch (Exception exception)
        {
            RecordDiagnostic("model.switch.failed", exception);
            verified = false;
        }

        // A later save chose another model while this one was being verified.
        if (settings.SpeechModel != model.Id)
            return;

        if (!verified)
        {
            settings = settings with { SpeechModel = activeModel.Id };
            SaveSettingsQuietly();
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"{model.DisplayName} could not be verified, so {activeModel.DisplayName} stays in use. Delete and download it again in Settings → Model.", Forms.ToolTipIcon.Warning);
            return;
        }

        activeModel = model;
        modelPath = provisioner.ModelPath;
        modelIdleTimer.Stop();
        // A dictation in progress keeps the old engine; the next shortcut loads the new model.
        if (shortcutHeld)
            return;

        ShowReadyPanel();
        try
        {
            await EnsureEngineLoadedAsync();
            ShowReadyPanel();
            ScheduleModelUnload();
        }
        catch (Exception exception)
        {
            RecordDiagnostic("model.load.failed", exception);
            statusItem.Text = "Model could not load";
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"The local model could not load: {exception.Message}", Forms.ToolTipIcon.Error);
        }
    }

    // Offline models transcribe the whole hold after release, so long dictations need longer.
    internal static TimeSpan FinalizationTimeout(SpeechModelDefinition model)
        => model.Style == RecognitionStyle.Offline ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(15);

    private async Task ConfigureReadyAsync(string modelPath)
    {
        this.modelPath = modelPath;
        hotkey.ToggleMode = settings.ShortcutMode == DictationShortcutModes.Toggle;
        var availability = hotkey.Start(HotkeyCatalog.FromBindings(settings.Shortcuts));
        settingsItem.Enabled = true;
        vocabularyItem.Enabled = true;
        statusItem.Text = $"{DescribeReady(availability)} — loading local model";
        trayIcon.Text = $"PrivateType — {statusItem.Text}";
        ShowReadyPanel();
        if (availability.DisabledHotkeys.Count > 0)
        {
            trayIcon.ShowBalloonTip(
                5000,
                "PrivateType",
                $"Unavailable shortcuts: {availability.DescribeDisabledHotkeys()}.",
                Forms.ToolTipIcon.Warning);
        }

        RecordDiagnostic("model.standby");
        await EnsureEngineLoadedAsync();
        ShowReadyPanel();
        ScheduleModelUnload();
    }

    private static string DescribeReady(HotkeyAvailability availability)
    {
        return availability.DisabledHotkeys.Count == 0
            ? "Dictation ready"
            : $"Dictation ready — unavailable: {availability.DescribeDisabledHotkeys()}";
    }

    private void ShowSettings(bool openVocabulary = false, string? vocabularyScope = null)
    {
        if (settingsStore is null || modelPath is null)
            return;

        // The tray menu stays clickable while a modal window is open. A second window would
        // interleave hotkey suspend/resume and leave the first dialog disabling the bubble,
        // so reuse the open window instead.
        if (openSettingsWindow is not null)
        {
            if (openVocabulary)
                openSettingsWindow.ShowVocabularyPage();
            BringToForeground(openSettingsWindow);
            return;
        }
        if (openTeachWindow is not null)
        {
            BringToForeground(openTeachWindow);
            return;
        }

        hotkey.Suspend();
        var window = new SettingsWindow(settings, MicrophoneCatalog.Enumerate(), new LibraryModelStore(models), openVocabulary, vocabularyScope);
        window.Loaded += (_, _) => BringToForeground(window);
        window.DiagnosticsRequested += () => ShowDiagnostics(window);
        window.LicensesRequested += () => new OpenSourceLicensesWindow { Owner = window }.ShowDialog();
        openSettingsWindow = window;
        bool? result;
        try
        {
            result = window.ShowDialog();
        }
        finally
        {
            openSettingsWindow = null;
        }

        if (result != true)
        {
            RestoreHotkeys(settings.Shortcuts);
            return;
        }

        if (window.SavedSettings is null)
        {
            RestoreHotkeys(settings.Shortcuts);
            return;
        }

        var newSettings = window.SavedSettings;
        var availability = hotkey.Resume(HotkeyCatalog.FromBindings(newSettings.Shortcuts));
        if (availability is null)
        {
            trayIcon.ShowBalloonTip(
                5000,
                "PrivateType",
                "Neither chosen shortcut is available. The previous shortcuts remain active.",
                Forms.ToolTipIcon.Warning);
            RestoreHotkeys(settings.Shortcuts);
            return;
        }

        var startupUpdate = StartupPreferencePolicy.DecideUpdate(settings.StartWithWindows, newSettings.StartWithWindows);
        try
        {
            StartupPreferenceTransaction.Apply(
                startupUpdate,
                windowsStartup,
                ExecutablePath(),
                () =>
                {
                    if (newSettings.ReadySound == "custom" &&
                        (newSettings.CustomReadySoundPath != settings.CustomReadySoundPath || settings.ReadySound != "custom"))
                    {
                        newSettings = newSettings with
                        {
                            CustomReadySoundPath = ReadySoundStorage.Import(newSettings.CustomReadySoundPath!, PortablePaths.DataDirectory)
                        };
                    }
                    settingsStore.Save(newSettings);
                });
            var previousModelId = settings.SpeechModel;
            settings = newSettings;
            if (settings.SpeechModel != previousModelId)
                _ = SwitchModelAsync(SpeechModelCatalog.Get(settings.SpeechModel));
            hotkey.ToggleMode = settings.ShortcutMode == DictationShortcutModes.Toggle;
            statusItem.Text = DescribeReady(availability);
            trayIcon.Text = $"PrivateType — {statusItem.Text}";
            ShowReadyPanel();
            ScheduleModelUnload();
            RecordDiagnostic(newSettings.StartWithWindows ? "startup.enabled" : "startup.disabled");
        }
        catch (Exception exception)
        {
            RecordDiagnostic("settings.save.failed", exception);
            hotkey.Suspend();
            RestoreHotkeys(settings.Shortcuts);
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"Settings were not saved: {exception.Message}", Forms.ToolTipIcon.Error);
        }
    }

    private static void BringToForeground(Wpf.Window window)
    {
        window.WindowState = Wpf.WindowState.Normal;
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
    }

    private void RestoreHotkeys(IReadOnlyList<ShortcutBinding> bindings)
    {
        if (hotkey.Resume(HotkeyCatalog.FromBindings(bindings)) is null)
        {
            ShowStartupFailure("The previous dictation shortcuts could not be re-registered after closing Settings.");
        }
    }

    private void ShowDiagnostics(Wpf.Window owner)
    {
        var window = new DiagnosticsWindow(diagnostics) { Owner = owner };
        window.ShowDialog();
    }

    private static string ExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("The current executable path is unavailable.");

    private void SavePanelPosition(string displayDeviceName, double leftFraction, double topFraction)
    {
        if (settingsStore is null)
            return;

        settings = settings with
        {
            PanelDisplayDeviceName = displayDeviceName,
            PanelLeftFraction = leftFraction,
            PanelTopFraction = topFraction
        };
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception exception)
        {
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"Panel position was not saved: {exception.Message}", Forms.ToolTipIcon.Warning);
        }
    }

    private void ShowStartupFailure(string message)
    {
        statusItem.Text = "Setup error";
        Wpf.MessageBox.Show(
            StartupFailureMessage(message),
            "PrivateType setup error",
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Error);
        Wpf.Application.Current.Shutdown();
    }

    internal static string StartupFailureMessage(string message) => message;

    // Opens on demand only. The sentence is discarded when the dialog closes, however it closes.
    private void ShowTeach()
    {
        if (openSettingsWindow is not null || openTeachWindow is not null || lastDictation.Current is not { } dictation)
            return;

        hotkey.Suspend();
        var window = new TeachWindow(dictation, settings.VocabularyStrength, SaveTaughtPhrases);
        window.Loaded += (_, _) => BringToForeground(window);
        openTeachWindow = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            openTeachWindow = null;
            lastDictation.Clear();
            RestoreHotkeys(settings.Shortcuts);
        }

        if (window.VocabularyScopeToOpen is { } scope)
            ShowSettings(openVocabulary: true, vocabularyScope: scope);
    }

    // Adds the phrases and the wordings they replace in one save, or none of them. In-memory
    // settings change only after the file is saved.
    private string? SaveTaughtPhrases(TaughtTerms taught)
    {
        if (settingsStore is null)
            return "Settings are not available yet.";

        var candidate = settings with
        {
            Vocabulary = TaughtVocabulary.Merge(settings.Vocabulary, taught.Entries),
            VocabularyCorrections = VocabularyCorrectionRules.Merge(settings.VocabularyCorrections, taught.Corrections)
        };
        if (PortableSettingsValidator.Validate(candidate) is { } error)
            return error;

        try
        {
            settingsStore.Save(candidate);
        }
        catch (Exception exception)
        {
            RecordDiagnostic("vocabulary.teach.save.failed", exception);
            return "The phrases could not be saved. Check that the PrivateType folder is writable, then try again.";
        }

        settings = candidate;
        RecordDiagnostic("vocabulary.taught");
        return null;
    }

    private DictationSession CreateSession(string localeCode)
    {
        var sequence = Interlocked.Read(ref dictationSequence);
        var session = new DictationSession(
            new DefaultMicrophoneCapture(settings.MicrophoneId),
            activeModel.Style == RecognitionStyle.Offline
                ? new OfflineRecognizer(engine.TranscriptionEndpoint)
                : new RealtimeRecognizer(engine.RealtimeEndpoint),
            new ForegroundTargetGuard(new Win32ForegroundTarget()),
            settings.InsertionMode == TextInsertionModes.Paste ? new ClipboardPasteInjector() : new UnicodeTextInjector(),
            new RecognitionRequest(localeCode, VocabularyComposer.Compose(settings.Vocabulary, settings.VocabularyPacks, localeCode), settings.VocabularyStrength),
            finalizationTimeout: FinalizationTimeout(activeModel),
            diagnostics: diagnostics,
            correctText: settings.CorrectAfterDictation
                ? TranscriptCorrector.Create(settings.Vocabulary, settings.VocabularyPacks, settings.VocabularyCorrections, localeCode).Correct
                : null);
        session.PresentationChanged += presentation => Present(localeCode, presentation);
        session.AudioMeterChanged += PresentAudioMeter;
        // Keep the result only if no newer dictation has started since this one.
        session.Finalized += result => Wpf.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!disposed && sequence == Interlocked.Read(ref dictationSequence))
                lastDictation.Replace(result);
        }));
        return session;
    }

    private async Task BeginDictationAsync(string localeCode)
    {
        // Discard the previous sentence before anything else happens for the new dictation.
        lastDictation.Clear();
        Interlocked.Increment(ref dictationSequence);
        shortcutHeld = true;
        var generation = ++heldGeneration;
        modelIdleTimer.Stop();
        // In toggle mode the key is up while dictating, so only hold mode needs the watchdog.
        if (!hotkey.ToggleMode)
            heldKeyWatchdog.Start();
        bubble.MoveToPointerScreen();
        var waitedForModel = !engineLoads.IsLoaded;
        if (waitedForModel)
            bubble.ShowModelLoading(hotkey.ToggleMode);
        try
        {
            await EnsureEngineLoadedAsync();
            if (!shortcutHeld || generation != heldGeneration)
            {
                if (!shortcutHeld)
                    ShowReadyPanel();
                ScheduleModelUnload();
                return;
            }

            await sessions.HoldAsync(localeCode);
            if (waitedForModel && shortcutHeld && generation == heldGeneration && sessions.IsRecording)
                PlayModelReadySound();
        }
        catch (Exception exception)
        {
            RecordDiagnostic("model.load.failed", exception);
            statusItem.Text = "Model could not load";
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"The local model could not load: {exception.Message}", Forms.ToolTipIcon.Error);
            bubble.ShowError("The local model could not load.");
        }
    }

    private void PlayModelReadySound()
    {
        try
        {
            modelReadySound.Play(settings);
        }
        catch (Exception exception)
        {
            RecordDiagnostic("model.ready.sound.failed", exception);
        }
    }

    private async Task EnsureEngineLoadedAsync()
    {
        if (modelPath is null)
            throw new InvalidOperationException("The local speech model is not available.");

        // A load already pending may be for the previous model; after a switch, load again.
        for (var attempt = 0; attempt < 3 && !engineLoads.IsLoaded; attempt++)
            await engineLoads.EnsureLoadedAsync();
        if (!engineLoads.IsLoaded)
            throw new InvalidOperationException("The local speech model changed while it was loading.");
    }

    private async Task LoadEngineAsync()
    {
        // Restarting the engine for another model must not cut off a dictation that is still finalizing.
        if (engine.IsRunning)
            await sessions.WhenIdleAsync();
        var path = modelPath ?? throw new InvalidOperationException("The local speech model is not available.");
        RecordDiagnostic("model.loading");
        statusItem.Text = "Loading local model";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await engine.StartAsync(path, timeout.Token);
        statusItem.Text = "Dictation ready";
        trayIcon.Text = $"PrivateType — {statusItem.Text}";
        RecordDiagnostic("model.loaded");
    }

    private async Task EndDictationAsync()
    {
        shortcutHeld = false;
        heldGeneration++;
        heldKeyWatchdog.Stop();
        await sessions.ReleaseAsync();
        if (!engineLoads.IsLoaded)
            ShowReadyPanel();
        ScheduleModelUnload();
    }

    private void ReleaseShortcutIfKeyIsUp(object? sender, EventArgs e)
    {
        if (hotkey.ReleaseIfKeyIsUp())
            RecordDiagnostic("shortcut.release.recovered");
    }

    private void ScheduleModelUnload()
    {
        modelIdleTimer.Stop();
        if (shortcutHeld || !engineLoads.IsLoaded)
            return;

        modelIdleTimer.Interval = TimeSpan.FromMinutes(settings.ModelIdleTimeoutMinutes);
        modelIdleTimer.Start();
        RecordDiagnostic("model.unload.scheduled", details: [("minutes", settings.ModelIdleTimeoutMinutes)]);
    }

    private void UnloadModelWhenIdle(object? sender, EventArgs e)
    {
        modelIdleTimer.Stop();
        if (shortcutHeld)
            return;

        engine.Stop();
        statusItem.Text = "Dictation ready — model unloaded";
        trayIcon.Text = $"PrivateType — {statusItem.Text}";
        ShowReadyPanel();
        RecordDiagnostic("model.unloaded");
    }

    private void Present(string localeCode, DictationPresentation presentation)
    {
        if (!pendingPresentations.Enqueue(localeCode, presentation))
            return;

        _ = Wpf.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderLatestPresentation));
    }

    private void RenderLatestPresentation()
    {
        if (!pendingPresentations.TryTake(out var request))
            return;

        RenderPresentation(request!.LocaleCode, request.Presentation);
    }

    private void RenderPresentation(string localeCode, DictationPresentation presentation)
    {
        var version = ++presentationVersion;
        var bubblePresentation = BubblePresentationMapper.Map(presentation);
        switch (bubblePresentation.Kind)
        {
            case BubblePresentationKind.Recording:
                bubble.ShowRecording(localeCode);
                bubble.ShowTranscript(bubblePresentation.Text);
                break;
            case BubblePresentationKind.Finalizing:
                bubble.ShowFinalizing();
                break;
            case BubblePresentationKind.Cancellation:
                trayIcon.Icon = trayIcons.Ready;
                bubble.ShowCancellation(bubblePresentation.Text);
                break;
            case BubblePresentationKind.Error:
                trayIcon.Icon = trayIcons.Ready;
                bubble.ShowError(bubblePresentation.Text);
                _ = ReturnToReadyWhenCurrentAsync(version, TimeSpan.FromSeconds(2));
                break;
            case BubblePresentationKind.Hide:
                _ = ReturnToReadyWhenCurrentAsync(version, TimeSpan.FromMilliseconds(700));
                break;
        }
    }

    private async Task ReturnToReadyWhenCurrentAsync(int version, TimeSpan delay)
    {
        await Task.Delay(delay);
        if (version == presentationVersion)
            ShowReadyPanel();
    }

    private void ShowReadyPanel()
    {
        trayIcon.Icon = trayIcons.Ready;
        bubble.ShowReady(settings, engineLoads.IsLoaded);
    }

    private void PresentAudioMeter(AudioMeter meter)
    {
        if (!pendingAudioMeters.Enqueue(meter))
            return;

        _ = Wpf.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderLatestAudioMeter));
    }

    private void RenderLatestAudioMeter()
    {
        if (pendingAudioMeters.TryTake(out var meter))
            bubble.ShowAudioMeter(meter!);
    }

    private void RecordDiagnostic(string eventName, Exception? error = null, (string Name, object? Value)[]? details = null)
    {
        if (diagnostics is null)
            return;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in details ?? [])
            values[name] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        diagnostics.Record(new DictationDiagnostic("application", DateTimeOffset.UtcNow, eventName, values, error));
    }
}
