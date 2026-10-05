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
        Assert.InRange(loud.Samples.Length / (double)(loud.WaveFormat.SampleRate * loud.WaveFormat.Channels), 0.5, 1.5);
    }

    [Fact]
    public void Spoken_cues_stay_silent_when_turned_off()
    {
        Assert.Null(SpokenCue.Load(SpokenCue.LoadingModel, PortableSettings.Default with { SpokenCueVoice = SpokenCueVoices.Off }));
    }

    [Fact]
    public void Release_plays_a_short_soft_blip_on_its_own()
    {
        var blip = ReleaseCue.Load(PortableSettings.Default with { ReadySoundVolume = 100 }, sayTranscribing: false);

        var rate = blip.WaveFormat.SampleRate * blip.WaveFormat.Channels;
        var delay = (int)(rate * ReleaseCue.DelayAfterRelease.TotalSeconds);
        Assert.All(blip.Samples[..delay], sample => Assert.Equal(0, sample));
        Assert.InRange((blip.Samples.Length - delay) / (double)rate, 0.05, 0.1);
        Assert.InRange(blip.Samples.Max(Math.Abs), 0.4f, 0.5f);
    }

    [Fact]
    public void A_long_dictation_hears_only_transcribing_after_the_same_delay()
    {
        var settings = PortableSettings.Default with { ReadySoundVolume = 100 };
        var voice = SpokenCue.Load(SpokenCue.Transcribing, settings)!;

        var cue = ReleaseCue.Load(settings, sayTranscribing: true);

        var delay = (int)(voice.WaveFormat.SampleRate * voice.WaveFormat.Channels * ReleaseCue.DelayAfterRelease.TotalSeconds);
        Assert.Equal(voice.WaveFormat, cue.WaveFormat);
        Assert.Equal(delay + voice.Samples.Length, cue.Samples.Length);
        Assert.All(cue.Samples[..delay], sample => Assert.Equal(0, sample));
        Assert.Equal(voice.Samples, cue.Samples[delay..]);
    }

    [Theory]
    [InlineData(SpokenCueVoices.Off, 100, false)]
    [InlineData(SpokenCueVoices.Female, 0, true)]
    public void Release_stays_a_blip_without_a_voice_and_silent_when_muted(string voice, int volume, bool silent)
    {
        var cue = ReleaseCue.Load(PortableSettings.Default with { SpokenCueVoice = voice, ReadySoundVolume = volume }, sayTranscribing: true);

        Assert.True(cue.Samples.Length / (double)cue.WaveFormat.SampleRate < ReleaseCue.DelayAfterRelease.TotalSeconds + 0.1 || silent);
        Assert.Equal(silent, cue.Samples.All(sample => sample == 0));
    }
}
