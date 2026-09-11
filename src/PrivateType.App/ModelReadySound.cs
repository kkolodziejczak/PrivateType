using NAudio.Wave;
using PrivateType.Core;

namespace PrivateType.App;

internal sealed class ModelReadySound : IDisposable
{
    private readonly object playbackLock = new();
    private WaveOutEvent? player;
    private bool disposed;

    public void Play(PortableSettings settings) => Start(settings, fallbackToPing: true);

    public void Preview(PortableSettings settings) => Start(settings, fallbackToPing: false);

    private void Start(PortableSettings settings, bool fallbackToPing)
    {
        lock (playbackLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            StopPlayback();
            var clip = ReadySoundAudio.Load(settings, fallbackToPing);
            if (settings.ReadySoundVolume == 0)
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
