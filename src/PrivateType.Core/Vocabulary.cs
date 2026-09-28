using System.Text;

namespace PrivateType.Core;

// A desired spelling of a word or phrase. Scope is "shared" or a catalog base-language code.
public sealed record VocabularyEntry(string Phrase, string Scope);

public sealed record VocabularyScope(string Code, string DisplayName);

public static class VocabularyScopes
{
    public const string Shared = "shared";

    public static IReadOnlyList<VocabularyScope> All { get; } =
    [
        new(Shared, "Shared across languages"),
        .. RecognitionLocaleCatalog.All
            .Where(locale => locale.BaseLanguageCode is not null)
            .GroupBy(locale => locale.BaseLanguageCode!)
            .Select(group => new VocabularyScope(group.Key, LanguageName(group.First().DisplayName)))
            .OrderBy(scope => scope.DisplayName, StringComparer.Ordinal)
    ];

    public static bool IsSupported(string? code) => All.Any(scope => scope.Code == code);

    public static VocabularyScope Get(string code) =>
        All.FirstOrDefault(scope => scope.Code == code) ?? throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported vocabulary scope.");

    // "English (United States)" -> "English"; "Arabic" stays "Arabic".
    private static string LanguageName(string displayName)
    {
        var region = displayName.IndexOf(" (", StringComparison.Ordinal);
        return region < 0 ? displayName : displayName[..region];
    }
}

// The pinned engine applies one boost to every phrase in a request, so strength is vocabulary-wide.
// Values come from the Stage 1 calibration on model revision ea30d66; 3 and above inserted phrases
// into unrelated speech too often.
public static class VocabularyStrengths
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string Strong = "strong";

    public static bool IsSupported(string? strength) => strength is Low or Normal or Strong;

    public static double Boost(string strength) => strength switch
    {
        Low => 0.5,
        Normal => 1.0,
        Strong => 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(strength), strength, "Unsupported vocabulary strength.")
    };
}

// A named, locally installed phrase collection. Strength stays vocabulary-wide.
public sealed record VocabularyPack(string Name, string Scope, bool IsEnabled, IReadOnlyList<string> Phrases);

public static class VocabularyRules
{
    public const int MaximumPhraseLength = 120;
    // Per dictation: what the engine accepted in Stage 1.
    public const int MaximumEntries = 200;
    public const int MaximumPayloadBytes = 16 * 1024;
    // Stored across personal vocabulary and every installed pack, enabled or not.
    public const int MaximumStoredPhrases = 1000;
    public const int MaximumPacks = 50;
    public const int MaximumPackNameLength = 80;

    public static string Normalize(string phrase) => phrase.Normalize(NormalizationForm.FormC).Trim();

    // Returns a content-free error for one normalized phrase, or null when it is valid.
    public static string? ValidatePhrase(string phrase)
    {
        if (phrase.Length == 0)
            return "Vocabulary phrases cannot be empty.";
        if (phrase.Any(char.IsControl))
            return "Vocabulary phrases must be on one line without control characters.";
        if (phrase.Length > MaximumPhraseLength)
            return $"Vocabulary phrases can be at most {MaximumPhraseLength} characters long.";
        return null;
    }

    // Messages never include phrase text, because vocabulary is private settings data.
    public static string? Validate(IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<VocabularyPack>? packs = null)
    {
        packs ??= [];
        var seen = new HashSet<(string Scope, string Phrase)>();
        foreach (var entry in entries)
        {
            if (!VocabularyScopes.IsSupported(entry.Scope))
                return "A vocabulary phrase has an unsupported language scope.";

            var phrase = Normalize(entry.Phrase ?? string.Empty);
            if (ValidatePhrase(phrase) is { } error)
                return error;
            if (!seen.Add((entry.Scope, phrase)))
                return $"The same phrase appears twice in {VocabularyScopes.Get(entry.Scope).DisplayName}.";
        }

        if (packs.Count > MaximumPacks)
            return $"PrivateType can keep at most {MaximumPacks} vocabulary packs.";

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in packs)
        {
            var name = (pack.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                return "Every vocabulary pack needs a name.";
            if (name.Length > MaximumPackNameLength || name.Any(char.IsControl))
                return $"Pack names must be one line of at most {MaximumPackNameLength} characters.";
            if (!names.Add(name))
                return "Two vocabulary packs have the same name.";
            if (!VocabularyScopes.IsSupported(pack.Scope))
                return "A vocabulary pack has an unsupported language.";

            var packPhrases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in pack.Phrases ?? [])
            {
                var phrase = Normalize(raw ?? string.Empty);
                if (ValidatePhrase(phrase) is { } error)
                    return error;
                if (!packPhrases.Add(phrase))
                    return "A vocabulary pack contains the same phrase twice.";
            }
        }

        if (entries.Count + packs.Sum(pack => pack.Phrases?.Count ?? 0) > MaximumStoredPhrases)
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PrivateType can store at most {MaximumStoredPhrases:N0} phrases across your vocabulary and packs.");

        // Every scope combination a dictation can use must fit the engine's proven request budget.
        foreach (var baseLanguage in VocabularyScopes.All.Select(scope => scope.Code == VocabularyScopes.Shared ? null : scope.Code))
        {
            var phrases = VocabularyComposer.ComposeScope(entries, packs, baseLanguage);
            var label = baseLanguage is null ? "Automatic" : VocabularyScopes.Get(baseLanguage).DisplayName;
            if (phrases.Count > MaximumEntries)
                return $"{label} dictation would use more than {MaximumEntries} phrases. Remove phrases or turn off a pack.";
            if (phrases.Sum(phrase => Encoding.UTF8.GetByteCount(phrase)) > MaximumPayloadBytes)
                return $"{label} dictation would use more than {MaximumPayloadBytes / 1024} KB of phrases. Remove phrases or turn off a pack.";
        }

        return null;
    }

    // Keeps individually valid entries and packs within the limits; used only to repair a damaged file.
    public static IReadOnlyList<VocabularyEntry> KeepValid(IReadOnlyList<VocabularyEntry>? entries)
    {
        var kept = new List<VocabularyEntry>();
        foreach (var entry in entries ?? [])
        {
            if (entry is null)
                continue;
            var candidate = entry with { Phrase = Normalize(entry.Phrase ?? string.Empty) };
            if (Validate([.. kept, candidate]) is null)
                kept.Add(candidate);
        }

        return kept;
    }

    public static IReadOnlyList<VocabularyPack> KeepValidPacks(IReadOnlyList<VocabularyEntry> personal, IReadOnlyList<VocabularyPack>? packs)
    {
        var kept = new List<VocabularyPack>();
        foreach (var pack in packs ?? [])
        {
            if (pack is null)
                continue;
            var candidate = pack with { Name = (pack.Name ?? string.Empty).Trim(), Phrases = (pack.Phrases ?? []).Select(phrase => Normalize(phrase ?? string.Empty)).ToArray() };
            if (Validate(personal, [.. kept, candidate]) is null)
                kept.Add(candidate);
        }

        return kept;
    }
}

public static class VocabularyComposer
{
    // Explicit locales use Shared plus their base language; Automatic uses Shared only.
    // Output is distinct and ordinal-sorted so requests do not depend on editing order.
    public static IReadOnlyList<string> Compose(IReadOnlyList<VocabularyEntry> entries, string localeCode) =>
        Compose(entries, [], localeCode);

    public static IReadOnlyList<string> Compose(IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<VocabularyPack> packs, string localeCode) =>
        ComposeScope(entries, packs, RecognitionLocaleCatalog.Get(localeCode).BaseLanguageCode);

    // Personal and pack phrases are emitted once each; with one strength there is no precedence.
    public static IReadOnlyList<string> ComposeScope(IReadOnlyList<VocabularyEntry> entries, IReadOnlyList<VocabularyPack> packs, string? baseLanguage)
    {
        bool Applies(string scope) => scope == VocabularyScopes.Shared || (baseLanguage is not null && scope == baseLanguage);

        return entries
            .Where(entry => Applies(entry.Scope))
            .Select(entry => Normalize(entry.Phrase))
            .Concat(packs.Where(pack => pack.IsEnabled && Applies(pack.Scope)).SelectMany(pack => pack.Phrases.Select(Normalize)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Normalize(string phrase) => VocabularyRules.Normalize(phrase ?? string.Empty);
}
