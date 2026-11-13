using PrivateType.Core;

namespace PrivateType.App;

// What a person needs to pick a model, written once. Setup leads with how the words appear, shown on one synthetic phrase
// at the same two moments for every model; then the same four facts for each: when the text appears, the languages,
// how the language is chosen, and your own phrases.
internal sealed record SpeechModelFacts(string Behaviour, string Appears, string WhileHolding, string Languages, string LanguageChoice, string Phrases)
{
    // The demonstration's phrase. It is the same for every model, because every model ends with the whole phrase.
    public const string SamplePhrase = "Send the notes to the team today.";

    public static SpeechModelFacts For(SpeechModelDefinition model) => model.Id switch
    {
        SpeechModelCatalog.ParakeetId => new(
            "all-at-once",
            "All at once, when you let go",
            string.Empty,
            "25 European languages",
            "Picks the language for you",
            "Fixes you save correct the text. It can't be taught to listen for your phrases."),
        SpeechModelCatalog.NemotronId => new(
            "word-by-word",
            "Word by word, as you speak",
            "Send the notes to",
            "32 languages, including Asian ones",
            "Each shortcut sets its language",
            "Listens for the phrases you add. Fixes you save correct the text too."),
        _ => new(ModelLibraryEditor.ShortName(model), model.Summary, string.Empty, string.Empty, string.Empty, string.Empty)
    };
}
