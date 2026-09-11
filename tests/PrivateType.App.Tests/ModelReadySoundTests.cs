using System.Media;
using PrivateType.App;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class ModelReadySoundTests
{
    [Fact]
    public void Bundled_ping_loads_without_playback_or_a_system_sound_scheme()
    {
        using var stream = typeof(ModelReadySound).Assembly.GetManifestResourceStream("PrivateType.ModelReady.wav");
        Assert.NotNull(stream);
        using var player = new SoundPlayer(stream);

        player.Load();

        Assert.True(player.IsLoadCompleted);
    }
}
