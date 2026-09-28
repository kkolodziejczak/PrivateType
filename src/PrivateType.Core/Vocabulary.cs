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

public static class VocabularyRules
{
    public const int MaximumPhraseLength = 120;
    public const int MaximumEntries = 200;
    public const int MaximumPayloadBytes = 16 * 1024;

    public static string Normalize(string phrase) => phrase.Normalize(NormalizationForm.FormC).Trim();

    // Messages never include phrase text, because vocabulary is private settings data.
    public static string? Validate(IReadOnlyList<VocabularyEntry> entries)
    {
        if (entries.Count > MaximumEntries)
            return $"Vocabulary can hold at most {MaximumEntries} phrases.";

        var seen = new HashSet<(string Scope, string Phrase)>();
        var payload = 0;
        foreach (var entry in entries)
        {
            if (!VocabularyScopes.IsSupported(entry.Scope))
                return "A vocabulary phrase has an unsupported language scope.";

            var phrase = Normalize(entry.Phrase ?? string.Empty);
            if (phrase.Length == 0)
                return "Vocabulary phrases cannot be empty.";
            if (phrase.Any(char.IsControl))
                return "Vocabulary phrases must be on one line without control characters.";
            if (phrase.Length > MaximumPhraseLength)
                return $"Vocabulary phrases can be at most {MaximumPhraseLength} characters long.";
            if (!seen.Add((entry.Scope, phrase)))
                return $"The same phrase appears twice in {VocabularyScopes.Get(entry.Scope).DisplayName}.";

            payload += Encoding.UTF8.GetByteCount(phrase);
        }

        return payload > MaximumPayloadBytes
            ? $"Vocabulary is too large; keep all phrases under {MaximumPayloadBytes / 1024} KB in total."
            : null;
    }

    // Keeps individually valid, non-duplicate entries within the limits; used only to repair a damaged file.
    public static IReadOnlyList<VocabularyEntry> KeepValid(IReadOnlyList<VocabularyEntry>? entries)
    {
        var kept = new List<VocabularyEntry>();
        foreach (var entry in entries ?? [])
        {
            if (entry is null)
                continue;
            var candidate = entry with { Phrase = Normalize(entry.Phrase ?? string.Empty) };
            if (kept.Count < MaximumEntries && Validate([.. kept, candidate]) is null)
                kept.Add(candidate);
        }

        return kept;
    }
}

public static class VocabularyComposer
{
    // Explicit locales use Shared plus their base language; Automatic uses Shared only.
    // Output is distinct and ordinal-sorted so requests do not depend on editing order.
    public static IReadOnlyList<string> Compose(IReadOnlyList<VocabularyEntry> entries, string localeCode)
    {
        var baseLanguage = RecognitionLocaleCatalog.Get(localeCode).BaseLanguageCode;
        return entries
            .Where(entry => entry.Scope == VocabularyScopes.Shared || (baseLanguage is not null && entry.Scope == baseLanguage))
            .Select(entry => entry.Phrase)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
