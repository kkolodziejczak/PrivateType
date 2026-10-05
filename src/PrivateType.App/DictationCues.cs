using NAudio.Wave;
using PrivateType.Core;

namespace PrivateType.App;

// Plays one cue at a time: a newer cue cuts off the one still playing, so the ready
// sound is never delayed by a spoken phrase.
internal sealed class DictationCues : IDisposable
{
    private readonly object playbackLock = new();
    private WaveOutEvent? player;
    private bool disposed;

    public void PlayReady(PortableSettings settings) => Start(settings, () => ReadySoundAudio.Load(settings, fallbackToPing: true));

    public void Preview(PortableSettings settings) => Start(settings, () => ReadySoundAudio.Load(settings, fallbackToPing: false));

    public void Announce(string phrase, PortableSettings settings)
    {
        if (settings.SpokenStatusCues)
            Start(settings, () => SpokenCue.LoadIfPrepared(phrase, settings.ReadySoundVolume));
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
            disposed = true;
        }
    }
}
