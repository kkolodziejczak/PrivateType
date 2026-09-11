using System.IO;
using System.Media;

namespace PrivateType.App;

internal sealed class ModelReadySound : IDisposable
{
    private SoundPlayer? player;
    private Stream? stream;

    public void Play()
    {
        if (player is null)
        {
            stream = typeof(ModelReadySound).Assembly.GetManifestResourceStream("PrivateType.ModelReady.wav")
                ?? throw new InvalidOperationException("The model-ready sound is missing.");
            player = new SoundPlayer(stream);
            player.Load();
        }

        player.Play();
    }

    public void Dispose()
    {
        player?.Dispose();
        stream?.Dispose();
    }
}
