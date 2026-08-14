using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class DictationSessionTests
{
    [Fact]
    public async Task Injects_overlapping_committed_text_once_when_the_original_target_is_still_eligible()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("Ala ma ", true, "commit-1"));
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("ma kota", true, "commit-2", BoundaryOverlap: 3));
        var injector = new FakeInjector();
        await using var session = CreateSession(capture, recognizer, new FakeForegroundTarget(), injector);

        await session.StartAsync();
        await session.StopAsync();

        Assert.Equal(["Ala ma kota"], injector.Texts);
        Assert.Equal(DictationState.Ready, session.State);
        Assert.True(capture.Disposed);
        Assert.True(recognizer.Disposed);
    }

    [Theory]
    [InlineData(TargetEligibility.Changed)]
    [InlineData(TargetEligibility.Invalid)]
    [InlineData(TargetEligibility.Ineligible)]
    public async Task Does_not_inject_when_the_captured_target_is_not_eligible(TargetEligibility eligibility)
    {
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("bezpieczny tekst", true, "commit-1"));
        var injector = new FakeInjector();
        await using var session = CreateSession(new FakeCapture(), recognizer, new FakeForegroundTarget(eligibility), injector);

        await session.StartAsync();
        await session.StopAsync();

        Assert.Empty(injector.Texts);
        Assert.Equal(DictationState.Ready, session.State);
    }

    [Fact]
    public async Task Publishes_a_non_modal_cancellation_message_when_the_target_changes()
    {
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("bezpieczny tekst", true, "commit-1"));
        var presentations = new List<DictationPresentation>();
        await using var session = CreateSession(new FakeCapture(), recognizer, new FakeForegroundTarget(TargetEligibility.Changed), new FakeInjector());
        session.PresentationChanged += presentations.Add;

        await session.StartAsync();
        await session.StopAsync();

        Assert.Contains(presentations, presentation => presentation.Message?.Contains("Changed", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Serializes_pcm_delivery_and_finishes_every_accepted_frame_before_cleanup()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer();
        await using var session = CreateSession(capture, recognizer, new FakeForegroundTarget(), new FakeInjector());

        await session.StartAsync();
        await capture.EmitAsync(1, 2, 3);
        await capture.EmitAsync(4, 5, 6);
        await session.StopAsync();

        Assert.Equal(1, recognizer.MaximumConcurrentPushes);
        Assert.True(capture.Disposed);
        Assert.True(recognizer.Disposed);
    }

    [Fact]
    public async Task Does_not_republish_recording_state_for_every_microphone_frame()
    {
        var capture = new FakeCapture();
        var presentations = new List<DictationPresentation>();
        await using var session = CreateSession(capture, new FakeRecognizer(), new FakeForegroundTarget(), new FakeInjector());
        session.PresentationChanged += presentations.Add;

        await session.StartAsync();
        var presentationCount = presentations.Count;
        await capture.EmitAsync(0, 0, 255, 127);
        await session.StopAsync();

        Assert.Equal(presentationCount, presentations.Count(presentation => presentation.State == DictationState.Recording));
    }

    [Fact]
    public async Task Publishes_live_spectrum_data_without_republishing_the_recording_state()
    {
        var capture = new FakeCapture();
        var meters = new List<AudioMeter>();
        await using var session = CreateSession(capture, new FakeRecognizer(), new FakeForegroundTarget(), new FakeInjector());
        session.AudioMeterChanged += meters.Add;

        await session.StartAsync();
        await capture.EmitAsync(0, 0, 255, 127, 0, 0, 255, 127);
        await session.StopAsync();

        var meter = Assert.Single(meters);
        Assert.Equal(44, meter.Spectrum.Count);
        Assert.True(meter.Level > 0);
    }

    [Fact]
    public void Ignores_late_pcm_callbacks_after_the_session_starts_finalizing()
    {
        Assert.False(DictationSession.ShouldQueuePcm(DictationState.Finalizing));
        Assert.True(DictationSession.IsExpectedClosedPcmWrite(DictationState.Finalizing, cancellationRequested: false));
        Assert.True(DictationSession.IsExpectedClosedPcmWrite(DictationState.Recording, cancellationRequested: true));
        Assert.False(DictationSession.IsExpectedClosedPcmWrite(DictationState.Recording, cancellationRequested: false));
    }

    [Fact]
    public async Task Cancels_a_pending_pcm_send_and_disposes_all_resources_after_a_capture_fault()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer { BlockPushUntilCancellation = true };
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession(capture, recognizer, new FakeForegroundTarget(), new FakeInjector());
        session.Faulted += exception => faulted.TrySetResult(exception);

        await session.StartAsync();
        var pcm = capture.EmitAsync(1, 2, 3);
        await recognizer.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        capture.Fail(new InvalidOperationException("microphone failed"));
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await pcm;
        await session.StopAsync();

        Assert.Equal(DictationState.Error, session.State);
        Assert.True(capture.Disposed);
        Assert.True(recognizer.Disposed);
    }

    [Fact]
    public async Task Records_the_injection_phase_when_Windows_rejects_the_final_text()
    {
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("final text", true, "commit-1"));
        var diagnostics = new FakeDiagnostics();
        var injector = new FakeInjector { Failure = new InvalidOperationException("SendInput failed") };
        await using var session = CreateSession(new FakeCapture(), recognizer, new FakeForegroundTarget(), injector, diagnostics: diagnostics);

        await session.StartAsync();
        await session.StopAsync();

        var failure = Assert.Single(diagnostics.Entries, entry => entry.EventName == "session.failed");
        Assert.Equal("text.inject", failure.Details["phase"]);
        Assert.Equal(typeof(InvalidOperationException), failure.Error!.GetType());
        Assert.DoesNotContain("final text", failure.Details.Values);
    }

    [Fact]
    public async Task Queues_a_new_hold_until_the_previous_session_is_disposed()
    {
        var firstRecognizer = new FakeRecognizer
        {
            CompleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var firstCapture = new FakeCapture();
        var createdSessions = 0;
        var languages = new List<RecognitionLanguage>();
        var coordinator = new DictationSessionCoordinator(language =>
        {
            languages.Add(language);
            createdSessions++;
            if (createdSessions == 1)
                return CreateSession(firstCapture, firstRecognizer, new FakeForegroundTarget(), new FakeInjector());

            Assert.True(firstCapture.Disposed);
            Assert.True(firstRecognizer.Disposed);
            return CreateSession(new FakeCapture(), new FakeRecognizer(), new FakeForegroundTarget(), new FakeInjector());
        });

        await coordinator.HoldAsync(RecognitionLanguage.Polish);
        var release = coordinator.ReleaseAsync();
        await firstRecognizer.CompleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var nextHold = coordinator.HoldAsync(RecognitionLanguage.English);
        firstRecognizer.CompleteGate.SetResult();
        await release;
        await nextHold;

        Assert.Equal(2, createdSessions);
        Assert.Equal([RecognitionLanguage.Polish, RecognitionLanguage.English], languages);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task Cleans_up_after_a_recognizer_fault_without_waiting_for_a_release()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer();
        await using var session = CreateSession(capture, recognizer, new FakeForegroundTarget(), new FakeInjector());

        await session.StartAsync();
        recognizer.Fail(new InvalidOperationException("recognizer stopped"));

        await capture.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await recognizer.DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(DictationState.Error, session.State);
    }

    [Fact]
    public async Task Times_out_a_stalled_pcm_push_on_release_and_disposes_resources()
    {
        var capture = new FakeCapture();
        var recognizer = new FakeRecognizer { BlockPushUntilCancellation = true };
        await using var session = CreateSession(capture, recognizer, new FakeForegroundTarget(), new FakeInjector(), TimeSpan.FromMilliseconds(100));

        await session.StartAsync();
        await capture.EmitAsync(1, 2, 3);
        await recognizer.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await session.StopAsync();

        Assert.Equal(DictationState.Error, session.State);
        Assert.True(capture.Disposed);
        Assert.True(recognizer.Disposed);
    }

    [Fact]
    public async Task Starts_the_next_held_session_after_a_stalled_previous_push_times_out()
    {
        var firstCapture = new FakeCapture();
        var firstRecognizer = new FakeRecognizer { BlockPushUntilCancellation = true };
        var createdSessions = 0;
        var coordinator = new DictationSessionCoordinator(_ =>
        {
            createdSessions++;
            if (createdSessions == 1)
                return CreateSession(firstCapture, firstRecognizer, new FakeForegroundTarget(), new FakeInjector(), TimeSpan.FromMilliseconds(100));

            Assert.True(firstCapture.Disposed);
            Assert.True(firstRecognizer.Disposed);
            return CreateSession(new FakeCapture(), new FakeRecognizer(), new FakeForegroundTarget(), new FakeInjector());
        });

        await coordinator.HoldAsync(RecognitionLanguage.Polish);
        await firstCapture.EmitAsync(1, 2, 3);
        await firstRecognizer.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var release = coordinator.ReleaseAsync();
        var nextHold = coordinator.HoldAsync(RecognitionLanguage.English);
        await release;
        await nextHold;

        Assert.Equal(2, createdSessions);
        await coordinator.DisposeAsync();
    }

    private static DictationSession CreateSession(
        FakeCapture capture,
        FakeRecognizer recognizer,
        FakeForegroundTarget target,
        FakeInjector injector,
        TimeSpan? finalizationTimeout = null,
        IDictationDiagnostics? diagnostics = null)
    {
        return new DictationSession(
            capture,
            recognizer,
            new ForegroundTargetGuard(target),
            injector,
            RecognitionLanguage.Polish,
            finalizationTimeout ?? TimeSpan.FromSeconds(1),
            diagnostics);
    }
}
