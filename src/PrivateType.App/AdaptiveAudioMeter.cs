using PrivateType.Core;

namespace PrivateType.App;

internal sealed class AdaptiveAudioMeter
{
    private const double SilenceThreshold = 0.015;
    private const double MinimumReferencePeak = 0.13;
    private const double TargetPeak = 0.82;
    private const double MaximumGain = 6;
    private const double ReferenceDecay = 0.94;
    private double referencePeak = MinimumReferencePeak;

    public AudioMeter Normalize(AudioMeter meter)
    {
        var peak = meter.Spectrum.Count == 0 ? 0 : meter.Spectrum.Max();
        if (peak < SilenceThreshold && meter.Level < SilenceThreshold)
        {
            referencePeak = Math.Max(MinimumReferencePeak, referencePeak * ReferenceDecay);
            return new AudioMeter(0, new double[meter.Spectrum.Count]);
        }

        referencePeak = Math.Max(peak, Math.Max(referencePeak * ReferenceDecay, MinimumReferencePeak));
        var gain = Math.Clamp(TargetPeak / referencePeak, 1, MaximumGain);
        var spectrum = meter.Spectrum.Select(value => Math.Clamp(value * gain, 0, 1)).ToArray();
        return new AudioMeter(Math.Clamp(meter.Level * gain, 0, 1), spectrum);
    }

    public void Reset() => referencePeak = MinimumReferencePeak;
}
