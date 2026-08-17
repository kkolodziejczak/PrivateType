using PrivateType.Core;
using System.Diagnostics;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;

namespace PrivateType.App;

internal sealed class DictationApplication : IDisposable
{
    private readonly EngineHost engine = new();
    private readonly HoldHotkeyHook hotkey = new();
    private readonly DictationBubble bubble = new();
    private readonly HttpModelDownloadClient downloader = new();
    private readonly TrayIconSet trayIcons = new();
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ToolStripMenuItem statusItem;
    private readonly Forms.ToolStripMenuItem settingsItem;
    private readonly DictationSessionCoordinator sessions;
    private readonly LatestPresentationQueue pendingPresentations = new();
    private readonly LatestAudioMeterQueue pendingAudioMeters = new();
    private readonly WindowsStartupRegistration windowsStartup = new();
    private readonly DispatcherTimer modelIdleTimer;
    private readonly InMemoryDiagnostics diagnostics = new();
    private PortableSettingsStore? settingsStore;
    private ModelProvisioner? modelProvisioner;
    private PortableSettings settings = PortableSettings.Default;
    private CancellationTokenSource? provisioningCancellation;
    private Task? engineLoadTask;
    private string? modelPath;
    private long heldGeneration;
    private bool shortcutHeld;
    private int presentationVersion;
    private bool disposed;

    public DictationApplication()
    {
        sessions = new DictationSessionCoordinator(CreateSession);
        trayIcon = new Forms.NotifyIcon
        {
            Icon = trayIcons.Ready,
            Text = "PrivateType",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        statusItem = (Forms.ToolStripMenuItem)trayIcon.ContextMenuStrip.Items.Add("Starting…");
        statusItem.Enabled = false;
        settingsItem = (Forms.ToolStripMenuItem)trayIcon.ContextMenuStrip.Items.Add("Settings…", null, (_, _) => ShowSettings());
        settingsItem.Enabled = false;
        trayIcon.ContextMenuStrip.Items.Add("Quit", null, (_, _) => Wpf.Application.Current.Shutdown());
        modelIdleTimer = new DispatcherTimer();
        modelIdleTimer.Tick += UnloadModelWhenIdle;
        hotkey.Held += language => Wpf.Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = BeginDictationAsync(language)));
        hotkey.Released += () => Wpf.Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = EndDictationAsync()));
        bubble.PositionChanged += SavePanelPosition;
        bubble.SettingsRequested += ShowSettings;
        bubble.QuitRequested += () => Wpf.Application.Current.Shutdown();
        bubble.RecordingIndicatorChanged += visible => trayIcon.Icon = visible ? trayIcons.Listening : trayIcons.Ready;
    }

    public void Start() => _ = InitializeAsync();

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        modelIdleTimer.Stop();
        provisioningCancellation?.Cancel();
        hotkey.Dispose();
        trayIcon.Dispose();
        trayIcons.Dispose();
        sessions.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bubble.Close();
        engine.Dispose();
        downloader.Dispose();
        provisioningCancellation?.Dispose();
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
            modelProvisioner = new ModelProvisioner(PortablePaths.ModelsDirectory, PinnedModel.Manifest, downloader);
            if (loaded.Warning is not null)
                trayIcon.ShowBalloonTip(5000, "PrivateType", loaded.Warning, Forms.ToolTipIcon.Warning);
            if (!MicrophoneCatalog.Enumerate().Any(microphone => microphone.Id == settings.MicrophoneId))
                trayIcon.ShowBalloonTip(5000, "PrivateType", "The saved microphone is unavailable; the system default will be used until you choose another microphone.", Forms.ToolTipIcon.Warning);

            if (modelProvisioner.IsAvailable())
                await ConfigureReadyAsync(modelProvisioner.ModelPath);
            else
                ShowModelSetup();
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

    private void ShowModelSetup()
    {
        statusItem.Text = "Model setup required";
        var window = new ModelSetupWindow();
        window.RetryRequested += () => ShowModelDownloadOrRuntimeRequirement(window);
        window.DownloadRequested += () => _ = ProvisionModelAsync(window);
        window.CancelRequested += () => Wpf.Application.Current.Shutdown();
        window.Show();
        ShowModelDownloadOrRuntimeRequirement(window);
    }

    private static void ShowModelDownloadOrRuntimeRequirement(ModelSetupWindow window)
    {
        if (EngineHost.TryVerifyPrerequisites(out var message))
            window.ShowDownloadConsent();
        else
            window.ShowRuntimePrerequisite(message);
    }

    private async Task ProvisionModelAsync(ModelSetupWindow window)
    {
        if (modelProvisioner is null || provisioningCancellation is not null)
            return;

        provisioningCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<long>(downloaded => window.ShowProgress(downloaded, PinnedModel.Manifest.ExpectedBytes));
            var modelPath = await modelProvisioner.EnsureAvailableAsync(progress, provisioningCancellation.Token);
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

    private async Task ConfigureReadyAsync(string modelPath)
    {
        this.modelPath = modelPath;
        var availability = hotkey.Start(HotkeyCatalog.FromBindings(settings.Shortcuts));
        settingsItem.Enabled = true;
        statusItem.Text = $"{DescribeReady(availability)} — model loads on first use";
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
    }

    private static string DescribeReady(HotkeyAvailability availability)
    {
        return availability.DisabledHotkeys.Count == 0
            ? "Dictation ready"
            : $"Dictation ready — unavailable: {availability.DescribeDisabledHotkeys()}";
    }

    private void ShowSettings()
    {
        if (settingsStore is null || modelProvisioner is null)
            return;

        hotkey.Suspend();
        var window = new SettingsWindow(settings, MicrophoneCatalog.Enumerate());
        window.Loaded += (_, _) => BringToForeground(window);
        window.DiagnosticsRequested += () => ShowDiagnostics(window);
        window.LicensesRequested += () => new OpenSourceLicensesWindow { Owner = window }.ShowDialog();
        if (window.ShowDialog() != true)
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
                () => settingsStore.Save(newSettings));
            settings = newSettings;
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
            $"{message}\n\nPrivateType needs a writable portable folder.",
            "PrivateType setup error",
            Wpf.MessageBoxButton.OK,
            Wpf.MessageBoxImage.Error);
        Wpf.Application.Current.Shutdown();
    }

    private DictationSession CreateSession(RecognitionLanguage language)
    {
        var session = new DictationSession(
            new DefaultMicrophoneCapture(settings.MicrophoneId),
            new RealtimeRecognizer(engine.RealtimeEndpoint),
            new ForegroundTargetGuard(new Win32ForegroundTarget()),
            new UnicodeTextInjector(),
            language,
            diagnostics: diagnostics);
        session.PresentationChanged += presentation => Present(language, presentation);
        session.AudioMeterChanged += PresentAudioMeter;
        return session;
    }

    private async Task BeginDictationAsync(RecognitionLanguage language)
    {
        shortcutHeld = true;
        var generation = ++heldGeneration;
        modelIdleTimer.Stop();
        bubble.MoveToPointerScreen();
        if (!engine.IsRunning)
            bubble.ShowModelLoading();
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

            await sessions.HoldAsync(language);
        }
        catch (Exception exception)
        {
            RecordDiagnostic("model.load.failed", exception);
            statusItem.Text = "Model could not load";
            trayIcon.ShowBalloonTip(5000, "PrivateType", $"The local model could not load: {exception.Message}", Forms.ToolTipIcon.Error);
            bubble.ShowError("The local model could not load.");
        }
    }

    private async Task EnsureEngineLoadedAsync()
    {
        if (engine.IsRunning)
            return;

        if (modelPath is null)
            throw new InvalidOperationException("The local speech model is not available.");

        engineLoadTask ??= LoadEngineAsync(modelPath);
        try
        {
            await engineLoadTask;
        }
        catch
        {
            engineLoadTask = null;
            throw;
        }
    }

    private async Task LoadEngineAsync(string path)
    {
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
        await sessions.ReleaseAsync();
        if (!engine.IsRunning)
            ShowReadyPanel();
        ScheduleModelUnload();
    }

    private void ScheduleModelUnload()
    {
        modelIdleTimer.Stop();
        if (shortcutHeld || !engine.IsRunning)
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
        engineLoadTask = null;
        statusItem.Text = "Dictation ready — model unloaded";
        trayIcon.Text = $"PrivateType — {statusItem.Text}";
        ShowReadyPanel();
        RecordDiagnostic("model.unloaded");
    }

    private void Present(RecognitionLanguage language, DictationPresentation presentation)
    {
        if (!pendingPresentations.Enqueue(language, presentation))
            return;

        _ = Wpf.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderLatestPresentation));
    }

    private void RenderLatestPresentation()
    {
        if (!pendingPresentations.TryTake(out var request))
            return;

        RenderPresentation(request!.Language, request.Presentation);
    }

    private void RenderPresentation(RecognitionLanguage language, DictationPresentation presentation)
    {
        var version = ++presentationVersion;
        var bubblePresentation = BubblePresentationMapper.Map(presentation);
        switch (bubblePresentation.Kind)
        {
            case BubblePresentationKind.Recording:
                bubble.ShowRecording(language);
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
        bubble.ShowReady(settings, engine.IsRunning);
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
