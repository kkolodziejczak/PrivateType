using NAudio.Wave;
using PrivateType.Core;

namespace PrivateType.App;

// Played the moment the shortcut is released, as exactly one sound: a soft falling blip meaning
// "the microphone is off" (the opposite of the rising ready ping), or "Transcribing" when a long
// dictation will take a moment. "Transcribing" already says the microphone is off.
internal static class ReleaseCue
{
    // Quieter than the ready sound: it confirms an action rather than asking for attention.
    internal const double BlipLoudness = 0.5;
    internal static readonly TimeSpan BlipLength = TimeSpan.FromMilliseconds(70);
    // People release the shortcut while finishing their last word; a blip at that instant talks
    // over their own voice. The clip starts with this silence instead.
    internal static readonly TimeSpan DelayAfterRelease = TimeSpan.FromMilliseconds(300);
    private static readonly WaveFormat BlipFormat = WaveFormat.CreateIeeeFloatWaveFormat(24_000, 1);

    internal static ReadySoundClip Load(PortableSettings settings, bool sayTranscribing)
    {
        // With the voice turned off, the blip still says the microphone is off.
        var sound = (sayTranscribing ? SpokenCue.Load(SpokenCue.Transcribing, settings) : null)
            ?? ReadySoundAudio.Scale(Blip(BlipFormat), (int)Math.Round(settings.ReadySoundVolume * BlipLoudness));
        var delay = Frames(sound.WaveFormat, DelayAfterRelease);
        var samples = new float[delay + sound.Samples.Length];
        sound.Samples.CopyTo(samples, delay);
        return new ReadySoundClip(sound.WaveFormat, samples);
    }

    // Glides from 1100 Hz down to 700 Hz with a quick attack and a soft decay.
    private static ReadySoundClip Blip(WaveFormat format)
    {
        var frames = (int)(format.SampleRate * BlipLength.TotalSeconds);
        var samples = new float[frames * format.Channels];
        var phase = 0.0;
        for (var frame = 0; frame < frames; frame++)
        {
            var progress = (double)frame / frames;
            phase += 2 * Math.PI * (1100 - 400 * progress) / format.SampleRate;
            var attack = Math.Min(frame / (format.SampleRate * 0.004), 1);
            var value = (float)(Math.Sin(phase) * attack * Math.Exp(-4 * progress) * (1 - progress));
            for (var channel = 0; channel < format.Channels; channel++)
                samples[frame * format.Channels + channel] = value;
        }
        return new ReadySoundClip(format, samples);
    }

    private static int Frames(WaveFormat format, TimeSpan length) =>
        (int)(format.SampleRate * length.TotalSeconds) * format.Channels;
}
