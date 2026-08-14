using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class AudioSpectrumAnalyzerTests
{
    [Fact]
    public void Locates_a_one_kilohertz_tone_in_its_middle_frequency_bands()
    {
        var meter = AudioSpectrumAnalyzer.Analyze(CreateTone(1_000));
        var loudestBand = meter.Spectrum
            .Select((value, index) => (value, index))
            .MaxBy(item => item.value)
            .index;

        Assert.InRange(meter.Level, 0.9, 1);
        Assert.InRange(loudestBand, 20, 28);
        Assert.True(meter.Spectrum[loudestBand] > 0.8, $"Peak magnitude was {meter.Spectrum[loudestBand]:F3}.");
    }

    [Fact]
    public void Produces_a_silent_meter_for_silence()
    {
        var meter = AudioSpectrumAnalyzer.Analyze(new byte[2_048]);

        Assert.Equal(0, meter.Level);
        Assert.All(meter.Spectrum, value => Assert.Equal(0, value));
    }

    private static byte[] CreateTone(double frequency)
    {
        var pcm = new byte[2_048];
        for (var index = 0; index < pcm.Length / 2; index++)
        {
            var sample = (short)(Math.Sin((2 * Math.PI * frequency * index) / 16_000) * short.MaxValue * 0.7);
            pcm[index * 2] = (byte)sample;
            pcm[(index * 2) + 1] = (byte)(sample >> 8);
        }

        return pcm;
    }
}
