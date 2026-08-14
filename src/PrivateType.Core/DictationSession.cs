using System.Collections.ObjectModel;
using System.Threading.Channels;

namespace PrivateType.Core;

public sealed class DictationSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultFinalizationTimeout = TimeSpan.FromSeconds(15);
    private readonly IAudioCapture capture;
    private readonly IStreamingRecognizer recognizer;
    private readonly ForegroundTargetGuard targetGuard;
    private readonly ITextInjector injector;
    private readonly RecognitionLanguage language;
    private readonly IDictationDiagnostics diagnostics;
    private readonly TimeSpan finalizationTimeout;
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private readonly Channel<ReadOnlyMemory<byte>> pcmFrames = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CommitCoordinator commits = new();
    private Task? pcmPump;
    private Task? updatePump;
    private Exception? failure;
    private double audioLevel;
    private bool started;
    private bool cleanedUp;
    private string phase = "created";

    public DictationSession(
        IAudioCapture capture,
        IStreamingRecognizer recognizer,
        ForegroundTargetGuard targetGuard,
        ITextInjector injector,
        RecognitionLanguage language,
        TimeSpan? finalizationTimeout = null,
        IDictationDiagnostics? diagnostics = null)
    {
        this.capture = capture;
        this.recognizer = recognizer;
        this.targetGuard = targetGuard;
        this.injector = injector;
        this.language = language;
        this.finalizationTimeout = finalizationTimeout ?? DefaultFinalizationTimeout;
        this.diagnostics = diagnostics ?? NullDictationDiagnostics.Instance;
    }

    public DictationState State { get; private set; } = DictationState.Ready;

    public event Action<DictationPresentation>? PresentationChanged;
    public event Action<AudioMeter>? AudioMeterChanged;
    public event Action<Exception>? Faulted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return RunSerializedAsync(() => StartCoreAsync(cancellationToken));
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (started)
            throw new InvalidOperationException("A dictation session can be started only once.");

        started = true;
        Diagnose("session.started", ("language", language));
        targetGuard.Capture();
        capture.PcmAvailable += QueuePcmAsync;
        capture.Faulted += ReportFailure;

        try
        {
            await recognizer.StartAsync(language, cancellationToken);
            pcmPump = PumpPcmAsync();
            updatePump = PumpUpdatesAsync();
            phase = "capture.start";
            await capture.StartAsync(cancellationToken);
            phase = "recording";
            Publish(DictationState.Recording);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            throw;
        }
    }

    public Task StopAsync()
    {
        return RunSerializedAsync(StopCoreAsync);
    }

    private async Task StopCoreAsync()
    {
        if (!started || cleanedUp)
            return;

        if (State == DictationState.Recording)
            Publish(DictationState.Finalizing);

        try
        {
            var deadline = DateTimeOffset.UtcNow + finalizationTimeout;
            Diagnose("session.finalizing", ("timeoutMilliseconds", finalizationTimeout.TotalMilliseconds));
            phase = "capture.stop";
            await capture.StopAsync(CancellationToken.None);
            pcmFrames.Writer.TryComplete();
            phase = "pcm.drain";
            await AwaitPumpBeforeDeadlineAsync(pcmPump, deadline);

            if (failure is null)
            {
                phase = "recognizer.complete";
                await recognizer.CompleteAsync(cancellation.Token).WaitAsync(GetRemainingTime(deadline));
                phase = "recognizer.updates";
                await AwaitPumpBeforeDeadlineAsync(updatePump, deadline);
                phase = "text.inject";
                InjectCommittedText();
                Diagnose("session.completed");
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            await CleanupAsync();
            Publish(failure is null ? DictationState.Ready : DictationState.Error, failure?.Message);
        }
    }

    private void InjectCommittedText()
    {
        var text = commits.TakeFinalText();
        if (text.Length == 0)
        {
            Diagnose("injection.skipped", ("reason", "empty-transcript"));
            return;
        }

        var eligibility = targetGuard.GetEligibility();
        if (eligibility != TargetEligibility.Eligible)
        {
            Diagnose("injection.skipped", ("reason", "target-ineligible"), ("targetEligibility", eligibility), ("characters", text.Length));
            Publish(DictationState.Finalizing, message: $"Nie wstawiono tekstu: cel dyktowania jest {eligibility}.");
            return;
        }

        Diagnose("injection.started", ("characters", text.Length));
        injector.Inject(text);
        Diagnose("injection.completed", ("characters", text.Length));
    }

    public async ValueTask DisposeAsync()
    {
        await RunSerializedAsync(CleanupAsync);
        cancellation.Dispose();
        operationGate.Dispose();
    }

    private Task RunSerializedAsync(Func<Task> operation)
    {
        return RunAsync();

        async Task RunAsync()
        {
            await operationGate.WaitAsync();
            try
            {
                await operation();
            }
            finally
            {
                operationGate.Release();
            }
        }
    }

    private async ValueTask QueuePcmAsync(ReadOnlyMemory<byte> pcm)
    {
        if (!ShouldQueuePcm(State))
            return;

        var meter = AudioSpectrumAnalyzer.Analyze(pcm.Span);
        Volatile.Write(ref audioLevel, meter.Level);
        AudioMeterChanged?.Invoke(meter);
        try
        {
            await pcmFrames.Writer.WriteAsync(pcm, cancellation.Token);
        }
        catch (ChannelClosedException) when (IsExpectedClosedPcmWrite(State, cancellation.IsCancellationRequested))
        {
        }
    }

    internal static bool ShouldQueuePcm(DictationState state) => state == DictationState.Recording;

    internal static bool IsExpectedClosedPcmWrite(DictationState state, bool cancellationRequested) =>
        state != DictationState.Recording || cancellationRequested;

    private async Task PumpPcmAsync()
    {
        try
        {
            await foreach (var pcm in pcmFrames.Reader.ReadAllAsync(cancellation.Token))
                await recognizer.PushPcmAsync(pcm, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            throw;
        }
    }

    private async Task PumpUpdatesAsync()
    {
        try
        {
            await foreach (var update in recognizer.ReadUpdatesAsync(cancellation.Token))
            {
                commits.Apply(update);
                Publish(State, commits.ProvisionalText);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            throw;
        }
    }

    private static TimeSpan GetRemainingTime(DateTimeOffset deadline)
    {
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("Dictation finalization exceeded its deadline.");

        return remaining;
    }

    private static async Task AwaitPumpBeforeDeadlineAsync(Task? pump, DateTimeOffset deadline)
    {
        if (pump is null)
            return;

        await pump.WaitAsync(GetRemainingTime(deadline));
    }

    private async Task AwaitPumpDuringCleanupAsync(Task? pump)
    {
        if (pump is null)
            return;

        try
        {
            await pump;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private void ReportFailure(Exception exception)
    {
        if (Interlocked.CompareExchange(ref failure, exception, null) is not null)
            return;

        Diagnose("session.failed", exception, ("phase", phase), ("state", State));
        cancellation.Cancel();
        pcmFrames.Writer.TryComplete(exception);
        Publish(DictationState.Error, message: exception.Message);
        Faulted?.Invoke(exception);
        _ = StopAsync();
    }

    private async Task CleanupAsync()
    {
        if (cleanedUp)
            return;

        cleanedUp = true;
        capture.PcmAvailable -= QueuePcmAsync;
        capture.Faulted -= ReportFailure;
        pcmFrames.Writer.TryComplete();
        cancellation.Cancel();

        try
        {
            await capture.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }

        await AwaitPumpDuringCleanupAsync(pcmPump);
        await AwaitPumpDuringCleanupAsync(updatePump);
        await capture.DisposeAsync();
        await recognizer.DisposeAsync();
    }

    private void Publish(DictationState state, string? provisionalText = null, string? message = null)
    {
        State = state;
        if (state != DictationState.Recording)
            Diagnose("session.state", ("state", state));
        PresentationChanged?.Invoke(new DictationPresentation(state, provisionalText ?? commits.ProvisionalText, message, Volatile.Read(ref audioLevel)));
    }

    private void Diagnose(string eventName, params (string Name, object? Value)[] properties) =>
        Diagnose(eventName, null, properties);

    private void Diagnose(string eventName, Exception? error, params (string Name, object? Value)[] properties)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in properties)
            details[name] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        try
        {
            diagnostics.Record(new DictationDiagnostic(sessionId, DateTimeOffset.UtcNow, eventName, new ReadOnlyDictionary<string, string>(details), error));
        }
        catch
        {
        }
    }
}
