using System.Runtime.InteropServices;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class WindowsBoundaryTests
{
    [Theory]
    [InlineData(0x3000u, 0x2000u, true)]
    [InlineData(0x2000u, 0x2000u, false)]
    [InlineData(0x1000u, 0x2000u, false)]
    public void Detects_only_higher_integrity_targets_as_blocked(uint targetLevel, uint currentLevel, bool expected)
    {
        Assert.Equal(expected, WindowsProcessIntegrity.IsHigher(targetLevel, currentLevel));
    }

    [Fact]
    public void Uses_the_x64_input_layout_required_by_SendInput()
    {
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.Input>());
    }

    [Fact]
    public void Rejects_start_when_the_fixed_local_engine_endpoint_is_already_ready()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => EngineHost.EnsureEndpointIsAvailable(true));

        Assert.Contains("8098", exception.Message);
    }

    [Theory]
    [InlineData(false, false, "MissingEngine")]
    [InlineData(true, false, "CouldNotStart")]
    [InlineData(true, true, "Ready")]
    public void Distinguishes_a_missing_engine_from_an_engine_that_cannot_start(
        bool executableExists,
        bool versionProbeSucceeded,
        string expected)
    {
        Assert.Equal(expected, EngineHost.ClassifyPrerequisites(executableExists, versionProbeSucceeded).ToString());
    }

    [Fact]
    public void Recommends_Visual_Cpp_only_when_the_engine_exists_but_cannot_start()
    {
        Assert.DoesNotContain("Visual C++", ModelSetupWindow.MissingEngineStatus);
        Assert.Contains("complete PrivateType portable ZIP", ModelSetupWindow.MissingEngineStatus);
        Assert.Contains("Visual C++", ModelSetupWindow.EngineCouldNotStartStatus);
    }

    [Fact]
    public void Quotes_the_current_executable_for_the_per_user_Windows_startup_entry()
    {
        Assert.Equal("\"C:\\Program Files\\PrivateType\\PrivateType.exe\"", WindowsStartupRegistration.Quote("C:\\Program Files\\PrivateType\\PrivateType.exe"));
    }

    [Fact]
    public void Reads_the_executable_from_a_quoted_startup_command()
    {
        Assert.Equal(
            "C:\\Program Files\\PrivateType\\PrivateType.exe",
            WindowsStartupRegistration.ExecutablePathFrom("\"C:\\Program Files\\PrivateType\\PrivateType.exe\" --ignored"));
    }

    [Fact]
    public void Describes_known_and_unknown_versions_without_exposing_paths()
    {
        Assert.Equal("PrivateType 2.4.1", StartupVersionPromptWindow.VersionLabel(new Version(2, 4, 1)));
        Assert.Equal("PrivateType (version unknown)", StartupVersionPromptWindow.VersionLabel(null));
    }

    [Fact]
    public void Leaves_windows_startup_disabled_when_no_dictation_app_is_registered()
    {
        Assert.Equal(
            StartupOwnershipDecision.LeaveDisabled,
            StartupOwnershipPolicy.Decide(
                hasPrivateTypeRegistration: false,
                hasLegacyRegistration: false,
                registeredExecutablePath: null,
                registeredVersion: null,
                currentExecutablePath: "C:\\Apps\\PrivateType\\PrivateType.exe",
                currentVersion: new Version(2, 0)));
    }

    [Fact]
    public void Claims_windows_startup_from_the_legacy_application()
    {
        Assert.Equal(
            StartupOwnershipDecision.ClaimCurrent,
            StartupOwnershipPolicy.Decide(
                hasPrivateTypeRegistration: false,
                hasLegacyRegistration: true,
                registeredExecutablePath: "C:\\Apps\\LiveDictation\\LiveDictation.App.exe",
                registeredVersion: null,
                currentExecutablePath: "C:\\Apps\\PrivateType\\PrivateType.exe",
                currentVersion: new Version(1, 0)));
    }

    [Theory]
    [InlineData(2, 0, 1, 9)]
    [InlineData(2, 0, 2, 0)]
    public void Automatically_claims_windows_startup_for_the_same_or_a_newer_version(
        int currentMajor,
        int currentMinor,
        int registeredMajor,
        int registeredMinor)
    {
        Assert.Equal(
            StartupOwnershipDecision.ClaimCurrent,
            StartupOwnershipPolicy.Decide(
                hasPrivateTypeRegistration: true,
                hasLegacyRegistration: false,
                registeredExecutablePath: "C:\\Apps\\PrivateType-v1\\PrivateType.exe",
                registeredVersion: new Version(registeredMajor, registeredMinor),
                currentExecutablePath: "C:\\Apps\\PrivateType-v2\\PrivateType.exe",
                currentVersion: new Version(currentMajor, currentMinor)));
    }

    [Fact]
    public void Asks_before_a_deliberate_downgrade_reclaims_windows_startup()
    {
        Assert.Equal(
            StartupOwnershipDecision.ConfirmCurrent,
            StartupOwnershipPolicy.Decide(
                hasPrivateTypeRegistration: true,
                hasLegacyRegistration: false,
                registeredExecutablePath: "C:\\Apps\\PrivateType-v2\\PrivateType.exe",
                registeredVersion: new Version(2, 0),
                currentExecutablePath: "C:\\Apps\\PrivateType-v1\\PrivateType.exe",
                currentVersion: new Version(1, 5)));
    }

    [Fact]
    public void Asks_when_either_portable_version_is_unknown()
    {
        Assert.Equal(
            StartupOwnershipDecision.ConfirmCurrent,
            StartupOwnershipPolicy.Decide(
                hasPrivateTypeRegistration: true,
                hasLegacyRegistration: false,
                registeredExecutablePath: "C:\\Apps\\PrivateType-unknown\\PrivateType.exe",
                registeredVersion: null,
                currentExecutablePath: "C:\\Apps\\PrivateType-local\\PrivateType.exe",
                currentVersion: new Version(1, 0)));
    }

    [Fact]
    public void Keeps_the_startup_entry_when_it_already_targets_the_running_copy()
    {
        Assert.Equal(
            StartupOwnershipDecision.KeepRegistered,
            StartupOwnershipPolicy.Decide(
                hasPrivateTypeRegistration: true,
                hasLegacyRegistration: false,
                registeredExecutablePath: "C:\\Apps\\PrivateType\\PrivateType.exe",
                registeredVersion: null,
                currentExecutablePath: "c:\\apps\\privatetype\\PrivateType.exe",
                currentVersion: null));
    }

    [Theory]
    [InlineData(false, false, "NoChange")]
    [InlineData(true, true, "NoChange")]
    [InlineData(false, true, "ClaimCurrent")]
    [InlineData(true, false, "Disable")]
    public void Changes_startup_ownership_only_when_the_user_changes_the_setting(
        bool wasEnabled,
        bool requestedEnabled,
        string expected)
    {
        Assert.Equal(expected, StartupPreferencePolicy.DecideUpdate(wasEnabled, requestedEnabled).ToString());
    }

    [Fact]
    public void Restores_both_startup_entries_exactly_when_settings_save_fails()
    {
        var registration = new FakeStartupRegistration("private-before", "legacy-before");

        Assert.Throws<InvalidOperationException>(() => StartupPreferenceTransaction.Apply(
            StartupPreferenceUpdate.ClaimCurrent,
            registration,
            "C:\\Apps\\PrivateType-new\\PrivateType.exe",
            () => throw new InvalidOperationException("save failed")));

        Assert.Equal("private-before", registration.PrivateTypeCommand);
        Assert.Equal("legacy-before", registration.LegacyCommand);
    }

    [Fact]
    public void Reports_when_the_previous_startup_entries_cannot_be_restored()
    {
        var registration = new FakeStartupRegistration("private-before", "legacy-before") { FailRestore = true };

        var exception = Assert.Throws<StartupRegistrationRestoreException>(() => StartupPreferenceTransaction.Apply(
            StartupPreferenceUpdate.Disable,
            registration,
            "C:\\Apps\\PrivateType-old\\PrivateType.exe",
            () => throw new InvalidOperationException("save failed")));

        Assert.Contains("could not be restored", exception.Message);
    }

    [Fact]
    public void Assigns_the_local_engine_to_a_job_that_ends_with_the_app()
    {
        Assert.Equal(EngineProcessJob.KillOnJobClose, EngineProcessJob.AppOwnedEngineLimitFlags);
    }

    [Fact]
    public void Targets_tray_icons_at_the_current_application_assembly()
    {
        Assert.Equal(
            "/PrivateType;component/Assets/PrivateType.ready.ico",
            TrayIconSet.ResourceUri("PrivateType.ready.ico").OriginalString);
    }

    [Fact]
    public void Keeps_the_waveform_continuously_visible_while_recording()
    {
        Assert.False(DictationBubble.RecordingWaveformBlinks);
    }

    [Fact]
    public void Keeps_the_bubble_center_fixed_when_its_width_changes()
    {
        Assert.Equal(217, DictationBubble.CenteredLeft(350, 64, 330));
        Assert.Equal(350, DictationBubble.CenteredLeft(217, 330, 64));
    }

    [Fact]
    public void Maps_the_bubble_to_the_same_relative_position_on_the_pointer_monitor()
    {
        Assert.Equal(4416, DictationBubble.MapCoordinateToWorkArea(
            coordinate: 1856,
            bubbleLength: 64,
            sourceStart: 0,
            sourceLength: 1920,
            targetStart: 1920,
            targetLength: 2560));
    }

    [Theory]
    [InlineData(false, 0.45)]
    [InlineData(true, 1.0)]
    public void Uses_transparency_only_for_the_unloaded_ready_bubble(bool modelLoaded, double expected)
    {
        Assert.Equal(expected, DictationBubble.OpacityForReadyState(modelLoaded));
    }

    [Fact]
    public void Uses_forty_four_frequency_bars_for_the_live_spectrum()
    {
        Assert.Equal(44, DictationBubble.SpectrumBarCount);
    }

    [Fact]
    public void Uses_the_model_loading_copy_for_a_cold_start()
    {
        Assert.Contains("Loading local model", DictationBubble.ModelLoadingTitle);
        Assert.Contains("Keep holding", DictationBubble.ModelLoadingHint);
    }

    [Fact]
    public void Coalesces_audio_meter_updates_to_the_latest_value()
    {
        var queue = new LatestAudioMeterQueue();
        Assert.True(queue.Enqueue(new AudioMeter(0.2, [0.2])));
        Assert.False(queue.Enqueue(new AudioMeter(0.8, [0.8])));

        Assert.True(queue.TryTake(out var meter));
        Assert.Equal(0.8, meter!.Level);
    }

    [Fact]
    public void Keeps_hints_open_when_pointer_settles_on_the_expanded_bubble()
    {
        Assert.False(DictationBubble.ShouldCollapseHints(isActive: false, isPointerOverBubble: true));
        Assert.True(DictationBubble.ShouldCollapseHints(isActive: false, isPointerOverBubble: false));
        Assert.False(DictationBubble.ShouldCollapseHints(isActive: true, isPointerOverBubble: false));
    }

    [Fact]
    public void Moves_an_expanded_bubble_only_far_enough_to_clear_the_taskbar()
    {
        Assert.Equal(620, DictationBubble.BoundedTop(700, 0, 800, 180));
        Assert.Equal(320, DictationBubble.BoundedTop(320, 0, 800, 180));
    }

    [Fact]
    public void Maps_target_cancellation_to_a_visible_bubble_notice()
    {
        var presentation = new DictationPresentation(DictationState.Finalizing, string.Empty, "Nie wstawiono tekstu", 0.25);

        var bubble = BubblePresentationMapper.Map(presentation);

        Assert.Equal(BubblePresentationKind.Cancellation, bubble.Kind);
        Assert.Equal("Nie wstawiono tekstu", bubble.Text);
    }

    [Fact]
    public void Maps_recording_audio_level_to_the_bubble()
    {
        var presentation = new DictationPresentation(DictationState.Recording, "tekst", null, 0.75);

        var bubble = BubblePresentationMapper.Map(presentation);

        Assert.Equal(BubblePresentationKind.Recording, bubble.Kind);
        Assert.Equal(0.75, bubble.AudioLevel);
    }

    [Fact]
    public void Maps_recording_state_even_when_the_ready_panel_is_already_visible()
    {
        var presentation = new DictationPresentation(DictationState.Recording, "live text", null, 0.25);

        var bubble = BubblePresentationMapper.Map(presentation);

        Assert.Equal(BubblePresentationKind.Recording, bubble.Kind);
        Assert.Equal("live text", bubble.Text);
    }

    [Fact]
    public void Coalesces_audio_frame_presentations_to_the_latest_pending_render()
    {
        var queue = new LatestPresentationQueue();
        var first = new DictationPresentation(DictationState.Recording, "first", null, 0.25);
        var latest = new DictationPresentation(DictationState.Recording, "latest", null, 0.75);

        Assert.True(queue.Enqueue(RecognitionLanguage.English, first));
        Assert.False(queue.Enqueue(RecognitionLanguage.English, latest));
        Assert.True(queue.TryTake(out var rendered));
        Assert.Equal("latest", rendered!.Presentation.ProvisionalText);
        Assert.Equal(0.75, rendered.Presentation.AudioLevel);
        Assert.True(queue.Enqueue(RecognitionLanguage.English, first));
    }

    [Theory]
    [InlineData(RecognitionLanguage.Polish, "● Słucham")]
    [InlineData(RecognitionLanguage.English, "● Listening")]
    [InlineData(RecognitionLanguage.Auto, "● Listening")]
    public void Uses_a_recording_status_in_the_selected_recognition_language(RecognitionLanguage language, string expected)
    {
        Assert.Equal(expected, DictationStatusText.ForRecording(language));
    }

    [Theory]
    [InlineData(HotkeyMessage.KeyDown, true, false)]
    [InlineData(HotkeyMessage.SystemKeyDown, true, false)]
    [InlineData(HotkeyMessage.KeyUp, false, true)]
    [InlineData(HotkeyMessage.SystemKeyUp, false, true)]
    public void Recognizes_standard_and_alt_hotkey_messages(int message, bool expectedDown, bool expectedUp)
    {
        Assert.Equal(expectedDown, HotkeyMessage.IsKeyDown(message));
        Assert.Equal(expectedUp, HotkeyMessage.IsKeyUp(message));
    }

    [Theory]
    [InlineData(RecognitionLanguage.Polish, "pl-PL")]
    [InlineData(RecognitionLanguage.English, "en-US")]
    [InlineData(RecognitionLanguage.Auto, "auto")]
    public void Maps_each_stage_two_recognition_language_to_the_local_engine(RecognitionLanguage language, string expected)
    {
        Assert.Equal(expected, RealtimeRecognizer.ToEngineLanguage(language));
    }

    private sealed class FakeStartupRegistration(string? privateTypeCommand, string? legacyCommand) : IStartupRegistrationWriter
    {
        public string? PrivateTypeCommand { get; private set; } = privateTypeCommand;
        public string? LegacyCommand { get; private set; } = legacyCommand;
        public bool FailRestore { get; init; }

        public StartupRegistrationSnapshot Capture() => new(PrivateTypeCommand, LegacyCommand);

        public void Claim(string executablePath)
        {
            PrivateTypeCommand = WindowsStartupRegistration.Quote(executablePath);
            LegacyCommand = null;
        }

        public void Disable()
        {
            PrivateTypeCommand = null;
            LegacyCommand = null;
        }

        public void Restore(StartupRegistrationSnapshot snapshot)
        {
            if (FailRestore)
                throw new InvalidOperationException("restore failed");

            PrivateTypeCommand = snapshot.PrivateTypeCommand;
            LegacyCommand = snapshot.LegacyCommand;
        }
    }
}
