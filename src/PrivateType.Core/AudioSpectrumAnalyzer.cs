namespace PrivateType.Core;

internal static class AudioSpectrumAnalyzer
{
    internal const int BandCount = 44;

    private const int SampleRate = 16_000;
    private const int AnalysisSampleCount = 1_024;
    private const double MinimumFrequency = 80;
    private const double MaximumFrequency = 7_500;
    private static readonly double[] Window = CreateHannWindow();
    private static readonly double[] GoertzelCoefficients = CreateGoertzelCoefficients();

    internal static AudioMeter Analyze(ReadOnlySpan<byte> pcm)
    {
        var sampleCount = Math.Min(AnalysisSampleCount, pcm.Length / 2);
        if (sampleCount == 0)
            return new AudioMeter(0, new double[BandCount]);

        var sumOfSquares = 0d;
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var sample = ReadSample(pcm, sampleIndex) / 32768d;
            sumOfSquares += sample * sample;
        }

        var spectrum = new double[BandCount];
        for (var bandIndex = 0; bandIndex < BandCount; bandIndex++)
            spectrum[bandIndex] = CalculateBandMagnitude(pcm, sampleCount, GoertzelCoefficients[bandIndex]);

        var rms = Math.Sqrt(sumOfSquares / sampleCount);
        return new AudioMeter(Math.Clamp(rms * 8, 0, 1), spectrum);
    }

    private static double CalculateBandMagnitude(ReadOnlySpan<byte> pcm, int sampleCount, double coefficient)
    {
        var previous = 0d;
        var previousPrevious = 0d;
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var sample = ReadSample(pcm, sampleIndex) / 32768d * Window[sampleIndex];
            var current = sample + coefficient * previous - previousPrevious;
            previousPrevious = previous;
            previous = current;
        }

        var power = Math.Max(0, previous * previous + previousPrevious * previousPrevious - coefficient * previous * previousPrevious);
        var decibels = 20 * Math.Log10((Math.Sqrt(power) / sampleCount) + 0.0000001);
        return Math.Clamp((decibels + 75) / 25, 0, 1);
    }

    private static short ReadSample(ReadOnlySpan<byte> pcm, int sampleIndex)
    {
        var offset = sampleIndex * 2;
        return (short)(pcm[offset] | (pcm[offset + 1] << 8));
    }

    private static double[] CreateHannWindow()
    {
        var window = new double[AnalysisSampleCount];
        for (var index = 0; index < window.Length; index++)
            window[index] = 0.5 * (1 - Math.Cos((2 * Math.PI * index) / (window.Length - 1)));

        return window;
    }

    private static double[] CreateGoertzelCoefficients()
    {
        var coefficients = new double[BandCount];
        var frequencyRatio = MaximumFrequency / MinimumFrequency;
        for (var index = 0; index < coefficients.Length; index++)
        {
            var frequency = MinimumFrequency * Math.Pow(frequencyRatio, index / (double)(coefficients.Length - 1));
            coefficients[index] = 2 * Math.Cos((2 * Math.PI * frequency) / SampleRate);
        }

        return coefficients;
    }
}
