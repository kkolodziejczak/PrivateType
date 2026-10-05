namespace PrivateType.Core;

// Streaming models show words while you speak; offline models transcribe the whole hold on release.
public enum RecognitionStyle
{
    Streaming,
    Offline
}

public sealed record SpeechModelDefinition(
    string Id,
    string DisplayName,
    string Summary,
    ModelManifest Manifest,
    string LicenseName,
    Uri LicenseUri,
    RecognitionStyle Style,
    bool UsesShortcutLanguage,
    bool UsesVocabularyBoost);

// The single owner of downloadable model identity. Ids are persisted in settings.
public static class SpeechModelCatalog
{
    public const string NemotronId = "nemotron-3.5-asr-streaming-0.6b";
    public const string ParakeetId = "parakeet-tdt-0.6b-v3";
    // Recommended for new installs. Settings saved before the model choice existed ran Nemotron and keep it.
    public const string DefaultId = ParakeetId;
    public const string PreChoiceId = NemotronId;

    public static SpeechModelDefinition Nemotron { get; } = new(
        NemotronId,
        "Nemotron 3.5 ASR Streaming 0.6B",
        "Shows words while you speak. Uses each shortcut's language and vocabulary boosting. 32 languages.",
        new ModelManifest(
            "nemotron-3.5-asr-streaming-0.6b-q8_0-ea30d66",
            new Uri("https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b/resolve/ea30d66debe3740a08b573244286791d423d6b3e/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf"),
            "nemotron-3.5-asr-streaming-0.6b.q8_0.gguf",
            742090464L,
            "3fc991d3badad7277c11030a7519832cddaf2057aafed6d4b25147e953a070b1"),
        "OpenMDW-1.1",
        new Uri("https://openmdw.ai/license/1-1/"),
        RecognitionStyle.Streaming,
        UsesShortcutLanguage: true,
        UsesVocabularyBoost: true);

    public static SpeechModelDefinition Parakeet { get; } = new(
        ParakeetId,
        "Parakeet TDT 0.6B v3",
        "Finishes the text when you release the shortcut, with an optional preview while you speak. Detects the language itself (25 European languages); vocabulary boosting is not available, but phrase fixes still apply.",
        new ModelManifest(
            "parakeet-tdt-0.6b-v3-q8_0-541d1f9",
            new Uri("https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3/resolve/541d1f99c6b0c3cd0b11a95167540bb8edefd82b/parakeet-tdt-0.6b-v3.q8_0.gguf"),
            "parakeet-tdt-0.6b-v3.q8_0.gguf",
            713975456L,
            "e3880d0aaaaf2c308ea2c35016b2b895c423eb3fda924c1b463d1c19b7f4d32e"),
        "CC-BY-4.0",
        new Uri("https://creativecommons.org/licenses/by/4.0/"),
        RecognitionStyle.Offline,
        UsesShortcutLanguage: false,
        UsesVocabularyBoost: false);

    public static IReadOnlyList<SpeechModelDefinition> All { get; } = [Nemotron, Parakeet];

    public static bool IsSupported(string? id) => All.Any(model => model.Id == id);

    // Each release folder starts without settings, but models are shared, so a copy with no saved
    // choice is usually an update. Earlier versions started such a copy on Nemotron, so a verified
    // Nemotron is reused first; the recommended model is downloaded only when nothing is cached.
    public static string ChooseWithoutSavedChoice(Func<SpeechModelDefinition, bool> isAvailable) =>
        new[] { Nemotron, Parakeet }.FirstOrDefault(isAvailable)?.Id ?? DefaultId;

    public static SpeechModelDefinition Get(string id) =>
        All.FirstOrDefault(model => model.Id == id)
        ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unsupported speech model.");

    public static string SizeLabel(SpeechModelDefinition model) => $"About {model.Manifest.ExpectedBytes / 1024d / 1024d:F0} MiB";
}
