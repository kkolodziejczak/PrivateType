using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class DictationCueTests
{
    [Fact]
    public void Follows_the_window_being_dictated_into()
    {
        Assert.True(DictationBubble.FollowsForegroundWindow(0x1234, 42, 7, "Chrome_WidgetWin_1"));
    }

    [Theory]
    [InlineData(0, 42u, "Chrome_WidgetWin_1")]
    [InlineData(0x1234, 0u, "Chrome_WidgetWin_1")]
    [InlineData(0x1234, 7u, "HwndWrapper")]
    [InlineData(0x1234, 42u, "Progman")]
    [InlineData(0x1234, 42u, "WorkerW")]
    public void Falls_back_to_the_pointer_without_a_dictation_window(long foreground, uint processId, string className)
    {
        Assert.False(DictationBubble.FollowsForegroundWindow((nint)foreground, processId, 7, className));
    }

    [Theory]
    [InlineData(SpokenCueVoices.Female, SpokenCue.LoadingModel)]
    [InlineData(SpokenCueVoices.Female, SpokenCue.Transcribing)]
    [InlineData(SpokenCueVoices.Male, SpokenCue.LoadingModel)]
    [InlineData(SpokenCueVoices.Male, SpokenCue.Transcribing)]
    public void Bundled_spoken_cues_play_at_the_chosen_volume(string voice, string phrase)
    {
        var loud = SpokenCue.Load(phrase, PortableSettings.Default with { SpokenCueVoice = voice, ReadySoundVolume = 100 })!;
        var muted = SpokenCue.Load(phrase, PortableSettings.Default with { SpokenCueVoice = voice, ReadySoundVolume = 0 })!;

        Assert.InRange(loud.Samples.Max(Math.Abs), 0.9f, 0.96f);
        Assert.All(muted.Samples, sample => Assert.Equal(0, sample));
        var channels = loud.WaveFormat.Channels;
        var leadingSilence = (int)(loud.WaveFormat.SampleRate * SpokenCue.LeadingSilence.TotalSeconds) * channels;
        Assert.All(loud.Samples.Take(leadingSilence), sample => Assert.Equal(0, sample));
        Assert.InRange(loud.Samples.Length / (double)(loud.WaveFormat.SampleRate * channels), 0.75, 1.75);
    }

    [Fact]
    public void Spoken_cues_stay_silent_when_turned_off()
    {
        Assert.Null(SpokenCue.Load(SpokenCue.LoadingModel, PortableSettings.Default with { SpokenCueVoice = SpokenCueVoices.Off }));
    }
}
