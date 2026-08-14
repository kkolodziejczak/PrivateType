namespace PrivateType.Core;

public enum RecognitionLanguage
{
    Polish,
    English,
    Auto
}

public enum DictationState
{
    Ready,
    Recording,
    Finalizing,
    Error
}

public sealed record TranscriptUpdate(string Text, bool IsCommitted, string? CommitId = null, int BoundaryOverlap = 0);

public sealed record DictationPresentation(DictationState State, string ProvisionalText, string? Message = null, double AudioLevel = 0);

public sealed record AudioMeter(double Level, IReadOnlyList<double> Spectrum);

public sealed record DictationTarget(string Value)
{
    public static readonly DictationTarget None = new(string.Empty);
}

public enum TargetEligibility
{
    Eligible,
    Changed,
    Invalid,
    Ineligible
}

public interface IStreamingRecognizer : IAsyncDisposable
{
    Task StartAsync(RecognitionLanguage language, CancellationToken cancellationToken);
    Task PushPcmAsync(ReadOnlyMemory<byte> pcm16KhzMono, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<TranscriptUpdate> ReadUpdatesAsync(CancellationToken cancellationToken);
}

public interface IAudioCapture : IAsyncDisposable
{
    event Func<ReadOnlyMemory<byte>, ValueTask>? PcmAvailable;
    event Action<Exception>? Faulted;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IForegroundTarget
{
    DictationTarget Capture();
    TargetEligibility GetEligibility(DictationTarget target);
}

public interface ITextInjector
{
    void Inject(string text);
}
