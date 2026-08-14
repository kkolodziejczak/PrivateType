using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class AdaptiveAudioMeterTests
{
    [Fact]
    public void Lifts_quiet_speech_without_changing_the_spectrum_shape()
    {
        var meter = new AdaptiveAudioMeter();

        var normalized = meter.Normalize(new AudioMeter(0.05, [0.03, 0.08, 0.16, 0.04]));

        Assert.Equal(0.82, normalized.Spectrum[2], precision: 2);
        Assert.True(normalized.Spectrum[1] > normalized.Spectrum[0]);
        Assert.True(normalized.Level > 0.2);
    }

    [Fact]
    public void Keeps_silence_flat()
    {
        var meter = new AdaptiveAudioMeter();

        var normalized = meter.Normalize(new AudioMeter(0.01, [0.01, 0.01, 0.01]));

        Assert.Equal(0, normalized.Level);
        Assert.All(normalized.Spectrum, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Does_not_overamplify_loud_speech()
    {
        var meter = new AdaptiveAudioMeter();

        var normalized = meter.Normalize(new AudioMeter(0.8, [0.25, 0.6, 0.95]));

        Assert.Equal(0.95, normalized.Spectrum[2]);
        Assert.Equal(0.8, normalized.Level);
    }
}
