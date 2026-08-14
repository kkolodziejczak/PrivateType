using System.Threading.Channels;
using PrivateType.Core;

namespace PrivateType.Core.Tests;

internal sealed class FakeCapture : IAudioCapture
{
    public event Func<ReadOnlyMemory<byte>, ValueTask>? PcmAvailable;
    public event Action<Exception>? Faulted;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    public bool Disposed { get; private set; }
    public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    public async Task EmitAsync(params byte[] pcm)
    {
        var handler = PcmAvailable;
        if (handler is not null)
            await handler(pcm);
    }

    public void Fail(Exception exception)
    {
        Faulted?.Invoke(exception);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        DisposedSignal.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRecognizer : IStreamingRecognizer
{
    private readonly Channel<TranscriptUpdate> updates = Channel.CreateUnbounded<TranscriptUpdate>();

    public List<TranscriptUpdate> CompletionUpdates { get; } = [];
    public bool BlockPushUntilCancellation { get; set; }
    public bool Disposed { get; private set; }
    public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int MaximumConcurrentPushes { get; private set; }
    public TaskCompletionSource PushStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CompleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? CompleteGate { get; set; }
    private int activePushes;

    public Task StartAsync(RecognitionLanguage language, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task PushPcmAsync(ReadOnlyMemory<byte> pcm16KhzMono, CancellationToken cancellationToken)
    {
        var concurrent = Interlocked.Increment(ref activePushes);
        MaximumConcurrentPushes = Math.Max(MaximumConcurrentPushes, concurrent);
        PushStarted.TrySetResult();
        try
        {
            if (BlockPushUntilCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref activePushes);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        CompleteStarted.TrySetResult();
        if (CompleteGate is not null)
            await CompleteGate.Task.WaitAsync(cancellationToken);

        foreach (var update in CompletionUpdates)
            updates.Writer.TryWrite(update);
        updates.Writer.TryComplete();
    }

    public async IAsyncEnumerable<TranscriptUpdate> ReadUpdatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken))
            yield return update;
    }

    public void Fail(Exception exception)
    {
        updates.Writer.TryComplete(exception);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        DisposedSignal.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeForegroundTarget(TargetEligibility eligibility = TargetEligibility.Eligible) : IForegroundTarget
{
    public TargetEligibility Eligibility { get; set; } = eligibility;

    public DictationTarget Capture() => new("original-window");

    public TargetEligibility GetEligibility(DictationTarget target) => Eligibility;
}

internal sealed class FakeInjector : ITextInjector
{
    public List<string> Texts { get; } = [];
    public Exception? Failure { get; set; }

    public void Inject(string text)
    {
        if (Failure is not null)
            throw Failure;

        Texts.Add(text);
    }
}

internal sealed class FakeDiagnostics : IDictationDiagnostics
{
    public List<DictationDiagnostic> Entries { get; } = [];

    public void Record(DictationDiagnostic diagnostic) => Entries.Add(diagnostic);
}
