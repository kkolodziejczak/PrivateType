using NAudio.Wave;
using PrivateType.Core;

namespace PrivateType.App;

// Plays one cue at a time: a newer cue cuts off the one still playing, so the ready
// sound is never delayed by a spoken phrase.
internal sealed class DictationCues : IDisposable
{
    private readonly object playbackLock = new();
    private WaveOutEvent? player;
    private WaveOutEvent? keepAwake;
    private bool disposed;

    public void PlayReady(PortableSettings settings) => Start(settings, () => ReadySoundAudio.Load(settings, fallbackToPing: true));

    public void Preview(PortableSettings settings) => Start(settings, () => ReadySoundAudio.Load(settings, fallbackToPing: false));

    public void Announce(string phrase, PortableSettings settings) => Start(settings, () => SpokenCue.Load(phrase, settings));

    // An idle audio output swallows the first moments of the next sound (the "Tr" of "Transcribing").
    // Streaming silence keeps it running, so a cue is heard from its first syllable.
    public void KeepOutputAwake()
    {
        lock (playbackLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (keepAwake is not null)
                return;
            var output = new WaveOutEvent();
            try
            {
                output.Init(new SilenceProvider(new WaveFormat(48_000, 16, 2)));
                output.Play();
                keepAwake = output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    public void LetOutputSleep()
    {
        lock (playbackLock)
            StopKeepingAwake();
    }

    private void StopKeepingAwake()
    {
        keepAwake?.Stop();
        keepAwake?.Dispose();
        keepAwake = null;
    }

    private void Start(PortableSettings settings, Func<ReadySoundClip?> loadClip)
    {
        lock (playbackLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            StopPlayback();
            var clip = loadClip();
            if (clip is null || settings.ReadySoundVolume == 0)
                return;
            var output = new WaveOutEvent();
            try
            {
                output.Init(clip);
                output.Play();
                player = output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (playbackLock)
            StopPlayback();
    }

    private void StopPlayback()
    {
        player?.Stop();
        player?.Dispose();
        player = null;
    }

    public void Dispose()
    {
        lock (playbackLock)
        {
            StopPlayback();
            StopKeepingAwake();
            disposed = true;
        }
    }
}
