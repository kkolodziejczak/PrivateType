using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;

namespace PrivateType.App;

// Short status phrases spoken by a local Windows voice. Nothing leaves the computer.
internal static class SpokenCue
{
    internal const string LoadingModel = "Loading model";
    internal const string Transcribing = "Transcribing";

    private static readonly ConcurrentDictionary<string, Lazy<ReadySoundClip>> rendered = new(StringComparer.Ordinal);

    // Rendering takes a moment, so it runs off the UI thread before the first shortcut press.
    internal static void Prepare()
    {
        _ = Rendered(LoadingModel);
        _ = Rendered(Transcribing);
    }

    // A phrase that is not rendered yet is skipped rather than holding up the dictation.
    internal static ReadySoundClip? LoadIfPrepared(string phrase, int volume) =>
        rendered.TryGetValue(phrase, out var clip) && clip.IsValueCreated
            ? ReadySoundAudio.Scale(clip.Value.Copy(), volume)
            : null;

    private static ReadySoundClip Rendered(string phrase) =>
        rendered.GetOrAdd(phrase, text => new Lazy<ReadySoundClip>(() => Render(text))).Value;

    private static ReadySoundClip Render(string phrase)
    {
        using var synthesizer = new SpeechSynthesizer();
        // The phrases are English, like the rest of the app's interface.
        var englishVoice = synthesizer.GetInstalledVoices(CultureInfo.GetCultureInfo("en-US")).FirstOrDefault(voice => voice.Enabled);
        if (englishVoice is not null)
            synthesizer.SelectVoice(englishVoice.VoiceInfo.Name);
        synthesizer.Rate = 1;
        using var wave = new MemoryStream();
        synthesizer.SetOutputToWaveStream(wave);
        synthesizer.Speak(phrase);
        synthesizer.SetOutputToNull();
        wave.Position = 0;
        return ReadySoundAudio.ReadWave(wave);
    }
}
