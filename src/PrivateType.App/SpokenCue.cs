using PrivateType.Core;

namespace PrivateType.App;

// Short status phrases bundled as recordings in Assets\Cues. They were generated once with
// Kokoro-82M (Apache-2.0): voice af_sky for female, am_echo for male, at speed 1.05.
internal static class SpokenCue
{
    internal const string LoadingModel = "loading-model";
    internal const string Transcribing = "transcribing";

    internal static ReadySoundClip? Load(string phrase, PortableSettings settings)
    {
        if (settings.SpokenCueVoice == SpokenCueVoices.Off)
            return null;

        using var stream = typeof(SpokenCue).Assembly.GetManifestResourceStream($"PrivateType.Cues.{settings.SpokenCueVoice}-{phrase}.wav")
            ?? throw new InvalidOperationException($"The spoken cue {settings.SpokenCueVoice}-{phrase} is missing.");
        return ReadySoundAudio.Scale(ReadySoundAudio.ReadWave(stream), settings.ReadySoundVolume);
    }
}
