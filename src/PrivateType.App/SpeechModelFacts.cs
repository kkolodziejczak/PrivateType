using PrivateType.Core;

namespace PrivateType.App;

// What a person needs to pick a model, written once: the reason to pick it, then the same two facts for every model.
internal sealed record SpeechModelFacts(string Lead, string Languages, string Phrases)
{
    public static SpeechModelFacts For(SpeechModelDefinition model) => model.Id switch
    {
        SpeechModelCatalog.ParakeetId => new(
            "Finished text when you release the shortcut. It picks the language itself.",
            "25 European languages, detected automatically",
            "Fixes you save correct the text. It can't be taught to listen for your phrases."),
        SpeechModelCatalog.NemotronId => new(
            "Words appear as you speak. It listens for your own phrases.",
            "32 languages, including Asian ones; each shortcut sets its language",
            "Listens for the phrases you add. Fixes you save correct the text too."),
        _ => new(model.Summary, string.Empty, string.Empty)
    };
}
