using PrivateType.Core;

namespace PrivateType.App;

internal enum BubblePresentationKind
{
    Recording,
    Finalizing,
    Cancellation,
    Error,
    Hide
}

internal sealed record BubblePresentation(BubblePresentationKind Kind, string Text, double AudioLevel);

internal sealed record BubbleRenderRequest(RecognitionLanguage Language, DictationPresentation Presentation);

internal sealed class LatestPresentationQueue
{
    private readonly object gate = new();
    private BubbleRenderRequest? latest;
    private bool renderScheduled;

    public bool Enqueue(RecognitionLanguage language, DictationPresentation presentation)
    {
        lock (gate)
        {
            latest = new BubbleRenderRequest(language, presentation);
            if (renderScheduled)
                return false;

            renderScheduled = true;
            return true;
        }
    }

    public bool TryTake(out BubbleRenderRequest? request)
    {
        lock (gate)
        {
            request = latest;
            latest = null;
            renderScheduled = false;
            return request is not null;
        }
    }
}

internal sealed class LatestAudioMeterQueue
{
    private readonly object gate = new();
    private AudioMeter? latest;
    private bool renderScheduled;

    public bool Enqueue(AudioMeter meter)
    {
        lock (gate)
        {
            latest = meter;
            if (renderScheduled)
                return false;

            renderScheduled = true;
            return true;
        }
    }

    public bool TryTake(out AudioMeter? meter)
    {
        lock (gate)
        {
            meter = latest;
            latest = null;
            renderScheduled = false;
            return meter is not null;
        }
    }
}

internal static class BubblePresentationMapper
{
    public static BubblePresentation Map(DictationPresentation presentation)
    {
        return presentation.State switch
        {
            DictationState.Recording => new(BubblePresentationKind.Recording, presentation.ProvisionalText, presentation.AudioLevel),
            DictationState.Finalizing when presentation.Message is not null => new(BubblePresentationKind.Cancellation, presentation.Message, presentation.AudioLevel),
            DictationState.Finalizing => new(BubblePresentationKind.Finalizing, string.Empty, presentation.AudioLevel),
            DictationState.Error => new(BubblePresentationKind.Error, presentation.Message ?? "Dyktowanie zostało przerwane.", presentation.AudioLevel),
            _ => new(BubblePresentationKind.Hide, string.Empty, presentation.AudioLevel)
        };
    }
}
