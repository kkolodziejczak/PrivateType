using PrivateType.Core;

namespace PrivateType.App;

// Short status phrases bundled as recordings in Assets\Cues. They were generated once with
// Kokoro-82M (Apache-2.0): voice af_sky for female, am_echo for male, at speed 1.05.
internal static class SpokenCue
{
    internal const string LoadingModel = "loading-model";
    internal const string Transcribing = "transcribing";

    // An idle audio output swallows the first moments of playback, which cut off the quiet "Tr"
    // of "Transcribing". Silence in front lets the output start before the speech does.
    internal static readonly TimeSpan LeadingSilence = TimeSpan.FromMilliseconds(250);

    internal static ReadySoundClip? Load(string phrase, PortableSettings settings)
    {
        if (settings.SpokenCueVoice == SpokenCueVoices.Off)
            return null;

        using var stream = typeof(SpokenCue).Assembly.GetManifestResourceStream($"PrivateType.Cues.{settings.SpokenCueVoice}-{phrase}.wav")
            ?? throw new InvalidOperationException($"The spoken cue {settings.SpokenCueVoice}-{phrase} is missing.");
        var clip = ReadySoundAudio.Scale(ReadySoundAudio.ReadWave(stream), settings.ReadySoundVolume);
        return WithLeadingSilence(clip);
    }

    private static ReadySoundClip WithLeadingSilence(ReadySoundClip clip)
    {
        var format = clip.WaveFormat;
        var silence = (int)(format.SampleRate * LeadingSilence.TotalSeconds) * format.Channels;
        var samples = new float[silence + clip.Samples.Length];
        clip.Samples.CopyTo(samples, silence);
        return new ReadySoundClip(format, samples);
    }
}
