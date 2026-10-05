using NAudio.Wave;
using PrivateType.Core;

namespace PrivateType.App;

// Played the moment the shortcut is released: a soft falling blip meaning "the microphone is off",
// the opposite of the rising ready ping. A long dictation adds "Transcribing" right after it,
// in one clip, because a newer cue would cut off the one still playing.
internal static class ReleaseCue
{
    // Quieter than the ready sound: it confirms an action rather than asking for attention.
    internal const double BlipLoudness = 0.5;
    internal static readonly TimeSpan BlipLength = TimeSpan.FromMilliseconds(70);
    // People release the shortcut while finishing their last word; a blip at that instant talks
    // over their own voice. The clip starts with this silence instead.
    internal static readonly TimeSpan DelayAfterRelease = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PauseBeforeVoice = TimeSpan.FromMilliseconds(120);
    private static readonly WaveFormat BlipOnlyFormat = WaveFormat.CreateIeeeFloatWaveFormat(24_000, 1);

    internal static ReadySoundClip Load(PortableSettings settings, bool sayTranscribing)
    {
        var voice = sayTranscribing ? SpokenCue.Load(SpokenCue.Transcribing, settings) : null;
        var format = voice?.WaveFormat ?? BlipOnlyFormat;
        var blip = ReadySoundAudio.Scale(Blip(format), (int)Math.Round(settings.ReadySoundVolume * BlipLoudness));
        var delay = Frames(format, DelayAfterRelease);
        var pause = voice is null ? 0 : Frames(format, PauseBeforeVoice);
        var samples = new float[delay + blip.Samples.Length + pause + (voice?.Samples.Length ?? 0)];
        blip.Samples.CopyTo(samples, delay);
        voice?.Samples.CopyTo(samples, delay + blip.Samples.Length + pause);
        return new ReadySoundClip(format, samples);
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
