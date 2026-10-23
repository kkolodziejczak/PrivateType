using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class VoiceActivityTests
{
    private const int BytesPerSecond = 32_000;
    private const int FrameBytes = 3_200;

    [Fact]
    public void Measures_the_root_mean_square_of_PCM16_samples()
    {
        // Two samples at half of full scale, one positive and one negative.
        byte[] pcm = [0x00, 0x40, 0x00, 0xC0];

        Assert.Equal(0.5, VoiceActivity.Rms(pcm), precision: 6);
        Assert.Equal(0d, VoiceActivity.Rms([]));
    }

    [Fact]
    public void Finds_where_the_voice_ends_after_a_quiet_room()
    {
        var frames = Frames((20, 0.001), (50, 0.1), (30, 0.001));

        var voice = VoiceActivity.Summarize(frames, BytesPerSecond);

        Assert.Equal(5, voice.VoicedSeconds, precision: 6);
        Assert.Equal(7, voice.LastVoicedSeconds, precision: 6);
    }

    [Fact]
    public void Ignores_a_click_shorter_than_a_syllable()
    {
        // Speech, then silence, then the shortcut's key coming up.
        var frames = Frames((20, 0.1), (30, 0.001), (1, 0.2), (5, 0.001));

        var voice = VoiceActivity.Summarize(frames, BytesPerSecond);

        Assert.Equal(2, voice.LastVoicedSeconds, precision: 6);
    }

    [Fact]
    public void Hears_speech_without_pauses()
    {
        // The quietest tenth is speech too, so the threshold falls back to about -46 dBFS.
        Assert.Equal(8, VoiceActivity.Summarize(Frames((40, 0.05), (40, 0.1)), BytesPerSecond).LastVoicedSeconds, precision: 6);
        Assert.Equal(69, VoiceActivity.Summarize(Frames((690, 0.005)), BytesPerSecond).LastVoicedSeconds, precision: 6);
    }

    [Fact]
    public void Keeps_short_pauses_between_syllables_inside_one_stretch_of_speech()
    {
        var syllables = Enumerable.Range(0, 10).SelectMany(_ => Frames((2, 0.1), (2, 0.0001))).ToList();
        var frames = Frames((20, 0.0001)).Concat(syllables).Concat(Frames((20, 0.0001))).ToList();

        var voice = VoiceActivity.Summarize(frames, BytesPerSecond);

        Assert.Equal(3.8, voice.VoicedSeconds, precision: 6);
        Assert.Equal(5.8, voice.LastVoicedSeconds, precision: 6);
    }

    [Fact]
    public void Needs_at_least_minus_fifty_dBFS_even_when_the_room_is_digitally_silent()
    {
        var voice = VoiceActivity.Summarize(Frames((20, 0), (20, 0.002), (20, 0.005)), BytesPerSecond);

        Assert.Equal(2, voice.VoicedSeconds, precision: 6);
    }

    [Fact]
    public void Hears_no_voice_in_a_silent_recording()
    {
        var voice = VoiceActivity.Summarize(Frames((50, 0.0005)), BytesPerSecond);

        Assert.Equal(new VoiceSummary(0, 0), voice);
        Assert.Equal(new VoiceSummary(0, 0), VoiceActivity.Summarize([], BytesPerSecond));
    }

    [Fact]
    public void Counts_a_short_final_frame_by_its_own_length()
    {
        var frames = Frames((10, 0.001), (10, 0.1)).Append(new AudioFrameLevel(0.1, FrameBytes / 2)).ToList();

        var voice = VoiceActivity.Summarize(frames, BytesPerSecond);

        Assert.Equal(2.05, voice.LastVoicedSeconds, precision: 6);
    }

    private static List<AudioFrameLevel> Frames(params (int Count, double Rms)[] runs) =>
        runs.SelectMany(run => Enumerable.Repeat(new AudioFrameLevel(run.Rms, FrameBytes), run.Count)).ToList();
}
