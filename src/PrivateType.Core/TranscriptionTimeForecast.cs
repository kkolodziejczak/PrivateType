namespace PrivateType.Core;

// Predicts how long a model that transcribes on release will take, from how long the user spoke,
// and learns this computer's speed from every finished dictation. Kept in memory only.
public sealed class TranscriptionTimeForecast
{
    // Waits shorter than this feel instant, so they are not announced.
    public static readonly TimeSpan AnnouncedWait = TimeSpan.FromSeconds(1);

    // "A minute of speech takes a few seconds": a conservative start until real timings arrive.
    internal const double InitialSecondsPerSpokenSecond = 0.08;

    // Very short recordings are dominated by fixed overhead and would skew the rate.
    internal static readonly TimeSpan ShortestLearnedRecording = TimeSpan.FromSeconds(2);

    private const double LearningWeight = 0.3;
    private readonly object gate = new();
    private double secondsPerSpokenSecond = InitialSecondsPerSpokenSecond;

    public bool IsWorthAnnouncing(TimeSpan recorded) => Predict(recorded) >= AnnouncedWait;

    public TimeSpan Predict(TimeSpan recorded)
    {
        lock (gate)
            return recorded * secondsPerSpokenSecond;
    }

    public void Learn(TimeSpan recorded, TimeSpan took)
    {
        if (recorded < ShortestLearnedRecording || took <= TimeSpan.Zero)
            return;

        lock (gate)
            secondsPerSpokenSecond += (took / recorded - secondsPerSpokenSecond) * LearningWeight;
    }
}
