namespace PrivateType.Core;

// A taught rewrite: when a dictation contains Heard, write Phrase instead. Heard is what the
// model produced, which the user chose to keep through Teach or Settings.
public sealed record VocabularyCorrection(string Heard, string Phrase, string Scope);

public static class VocabularyCorrectionRules
{
    public const int MaximumCorrections = 500;

    // Case and punctuation do not matter when matching, so they do not make wordings distinct.
    public static string Key(string heard) => string.Join(' ', MatchText.Tokens(VocabularyRules.Normalize(heard)).Select(token => token.Key));

    // Messages never include the wording, because corrections are private settings data.
    public static string? Validate(IReadOnlyList<VocabularyCorrection>? corrections)
    {
        if (corrections is null)
            return "Vocabulary corrections are missing.";
        if (corrections.Count > MaximumCorrections)
            return $"PrivateType can keep at most {MaximumCorrections} corrections.";

        var seen = new HashSet<(string Scope, string Key)>();
        foreach (var correction in corrections)
        {
            if (correction is null || !VocabularyScopes.IsSupported(correction.Scope))
                return "A correction has an unsupported language.";
            if (VocabularyRules.ValidatePhrase(VocabularyRules.Normalize(correction.Heard ?? string.Empty)) is { } heardError)
                return $"Heard wording: {heardError}";
            if (VocabularyRules.ValidatePhrase(VocabularyRules.Normalize(correction.Phrase ?? string.Empty)) is { } phraseError)
                return $"Correction: {phraseError}";

            var key = Key(correction.Heard!);
            if (key.Length == 0)
                return "Heard wording needs at least one letter or digit.";
            if (!seen.Add((correction.Scope, key)))
                return $"The same heard wording appears twice in {VocabularyScopes.Get(correction.Scope).DisplayName}.";
        }

        return null;
    }

    // Keeps individually valid corrections; used only to repair a damaged file.
    public static IReadOnlyList<VocabularyCorrection> KeepValid(IReadOnlyList<VocabularyCorrection>? corrections)
    {
        var kept = new List<VocabularyCorrection>();
        foreach (var correction in corrections ?? [])
        {
            if (correction is null)
                continue;
            var candidate = correction with { Heard = VocabularyRules.Normalize(correction.Heard ?? string.Empty), Phrase = VocabularyRules.Normalize(correction.Phrase ?? string.Empty) };
            if (Validate([.. kept, candidate]) is null)
                kept.Add(candidate);
        }

        return kept;
    }

    // A newly taught wording replaces an older rule for the same wording and language.
    public static IReadOnlyList<VocabularyCorrection> Merge(IReadOnlyList<VocabularyCorrection> existing, IEnumerable<VocabularyCorrection> taught)
    {
        var merged = existing.ToList();
        foreach (var correction in taught)
        {
            var key = Key(correction.Heard);
            merged.RemoveAll(item => item.Scope == correction.Scope && Key(item.Heard) == key);
            merged.Add(correction);
        }

        return merged;
    }
}

// Words for matching: runs of letters or digits, split where letters meet digits, so
// "3D", "3-D", and "3 D" all read as "3" "d".
internal static class MatchText
{
    public readonly record struct Token(int Start, int End, string Key);

    public static IReadOnlyList<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < text.Length)
        {
            if (!char.IsLetterOrDigit(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            var digit = char.IsDigit(text[index]);
            while (index < text.Length && char.IsLetterOrDigit(text[index]) && char.IsDigit(text[index]) == digit)
                index++;
            tokens.Add(new Token(start, index, text[start..index].ToLowerInvariant()));
        }

        return tokens;
    }

    // Words in one match may be separated by spaces or hyphens, never by sentence punctuation.
    public static bool IsJoiner(ReadOnlySpan<char> between)
    {
        foreach (var character in between)
        {
            if (!char.IsWhiteSpace(character) && character is not ('-' or '‐' or '‑'))
                return false;
        }

        return true;
    }
}

// How numbers and letters are read aloud, as the model writes them.
internal static class SpokenForms
{
    private const int MaximumVariants = 64;

    private static readonly string[] EnglishOnes =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
    ];
    private static readonly string[] EnglishTens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly string[] PolishOnes =
    [
        "zero", "jeden", "dwa", "trzy", "cztery", "pięć", "sześć", "siedem", "osiem", "dziewięć", "dziesięć", "jedenaście", "dwanaście",
        "trzynaście", "czternaście", "piętnaście", "szesnaście", "siedemnaście", "osiemnaście", "dziewiętnaście"
    ];
    private static readonly string[] PolishTens = ["", "", "dwadzieścia", "trzydzieści", "czterdzieści", "pięćdziesiąt", "sześćdziesiąt", "siedemdziesiąt", "osiemdziesiąt", "dziewięćdziesiąt"];
    private static readonly string[] PolishHundreds = ["", "sto", "dwieście", "trzysta", "czterysta", "pięćset", "sześćset", "siedemset", "osiemset", "dziewięćset"];
    private static readonly Dictionary<char, string> PolishLetters = new()
    {
        ['a'] = "a", ['b'] = "be", ['c'] = "ce", ['d'] = "de", ['e'] = "e", ['f'] = "ef", ['g'] = "gie", ['h'] = "ha", ['i'] = "i",
        ['j'] = "jot", ['k'] = "ka", ['l'] = "el", ['m'] = "em", ['n'] = "en", ['o'] = "o", ['p'] = "pe", ['q'] = "ku", ['r'] = "er",
        ['s'] = "es", ['t'] = "te", ['u'] = "u", ['v'] = "fau", ['w'] = "wu", ['x'] = "iks", ['y'] = "igrek", ['z'] = "zet"
    };

    // Every way the phrase may come out of the model, as word keys. The first is the phrase itself.
    public static IReadOnlyList<string[]> Variants(string phrase, IReadOnlyCollection<string> languages)
    {
        var tokens = MatchText.Tokens(phrase);
        var options = tokens.Select(token => TokenReadings(phrase[token.Start..token.End], token.Key, languages)).ToList();

        var variants = new List<string[]> { tokens.Select(token => token.Key).ToArray() };
        IEnumerable<IEnumerable<string>> product = [[]];
        foreach (var option in options)
            product = product.SelectMany(prefix => option.Select(reading => prefix.Concat(reading)));
        foreach (var variant in product.Take(MaximumVariants))
        {
            var words = variant.ToArray();
            if (!variants.Any(existing => existing.SequenceEqual(words)))
                variants.Add(words);
        }

        return variants;
    }

    private static List<string[]> TokenReadings(string original, string key, IReadOnlyCollection<string> languages)
    {
        List<string[]> readings = [[key]];
        if (key.All(char.IsAsciiDigit))
        {
            foreach (var language in languages)
                readings.AddRange(NumberReadings(key, language).Select(reading => reading.Split(' ')));
        }
        else if (original.Length <= 5 && original.All(character => char.IsAsciiLetterUpper(character)))
        {
            // Acronyms may be heard letter by letter: "API" as "a p i", "3D" in Polish as "trzy de".
            var letters = key.Select(letter => letter.ToString()).ToArray();
            if (letters.Length > 1)
                readings.Add(letters);
            if (languages.Contains("pl"))
                readings.Add(key.Select(letter => PolishLetters[letter]).ToArray());
        }

        return readings.DistinctBy(reading => string.Join(' ', reading)).ToList();
    }

    public static IEnumerable<string> NumberReadings(string digits, string language)
    {
        var ones = language == "pl" ? PolishOnes : EnglishOnes;
        var readings = new List<string> { string.Join(' ', digits.Select(digit => ones[digit - '0'])) };
        if (language == "en" && digits.Contains('0'))
            readings.Add(string.Join(' ', digits.Select(digit => digit == '0' ? "oh" : ones[digit - '0'])));

        if (digits.Length <= 4 && (digits.Length == 1 || digits[0] != '0'))
        {
            var number = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            readings.AddRange(language == "pl" ? [PolishCardinal(number)] : EnglishCardinal(number).Concat(EnglishPaired(number)));
        }

        return readings.Distinct();
    }

    private static string EnglishBelowHundred(int number) =>
        number < 20 ? EnglishOnes[number] : EnglishTens[number / 10] + (number % 10 > 0 ? " " + EnglishOnes[number % 10] : string.Empty);

    // 360 -> "three hundred sixty" and "three hundred and sixty".
    private static IEnumerable<string> EnglishCardinal(int number)
    {
        if (number < 100)
            return [EnglishBelowHundred(number)];
        if (number < 1000)
        {
            var hundreds = EnglishOnes[number / 100] + " hundred";
            var rest = number % 100;
            return rest == 0 ? [hundreds] : [$"{hundreds} {EnglishBelowHundred(rest)}", $"{hundreds} and {EnglishBelowHundred(rest)}"];
        }

        var thousands = EnglishOnes[number / 1000] + " thousand";
        var remainder = number % 1000;
        if (remainder == 0)
            return [thousands];
        var tails = EnglishCardinal(remainder).ToList();
        if (remainder < 100)
            tails.Add("and " + EnglishBelowHundred(remainder));
        return tails.Select(tail => $"{thousands} {tail}");
    }

    // 360 -> "three sixty", 305 -> "three oh five", 2024 -> "twenty twenty four".
    private static IEnumerable<string> EnglishPaired(int number)
    {
        if (number is >= 100 and < 1000 && number % 100 != 0)
        {
            var rest = number % 100;
            yield return $"{EnglishOnes[number / 100]} {(rest < 10 ? "oh " + EnglishOnes[rest] : EnglishBelowHundred(rest))}";
        }
        // Round thousands have no paired reading: 2000 is not "twenty hundred".
        else if (number >= 1000 && number % 1000 != 0)
        {
            var rest = number % 100;
            yield return $"{EnglishBelowHundred(number / 100)} {(rest == 0 ? "hundred" : rest < 10 ? "oh " + EnglishOnes[rest] : EnglishBelowHundred(rest))}";
        }
    }

    // 360 -> "trzysta sześćdziesiąt", 2024 -> "dwa tysiące dwadzieścia cztery".
    private static string PolishCardinal(int number)
    {
        if (number == 0)
            return PolishOnes[0];

        var words = new List<string>();
        var thousands = number / 1000;
        if (thousands == 1)
            words.Add("tysiąc");
        else if (thousands is >= 2 and <= 4)
            words.Add($"{PolishOnes[thousands]} tysiące");
        else if (thousands >= 5)
            words.Add($"{PolishOnes[thousands]} tysięcy");

        var rest = number % 1000;
        if (rest >= 100)
            words.Add(PolishHundreds[rest / 100]);
        rest %= 100;
        if (rest >= 20)
            words.Add(PolishTens[rest / 10] + (rest % 10 > 0 ? " " + PolishOnes[rest % 10] : string.Empty));
        else if (rest > 0)
            words.Add(PolishOnes[rest]);

        return string.Join(' ', words);
    }
}

// Rewrites a finished dictation before it is inserted: spoken forms of vocabulary phrases
// ("three d printing") become the phrase ("3D printing"), and taught wordings become their
// correction. Only whole words are replaced, and a match never crosses sentence punctuation.
public sealed class TranscriptCorrector
{
    private sealed record Rule(string[] Words, string Replacement, bool Taught);

    private readonly Dictionary<string, List<Rule>> rulesByFirstWord = new(StringComparer.Ordinal);

    private TranscriptCorrector()
    {
    }

    public static TranscriptCorrector Empty { get; } = new();

    public int RuleCount => rulesByFirstWord.Values.Sum(rules => rules.Count);

    public static TranscriptCorrector Create(
        IReadOnlyList<VocabularyEntry> entries,
        IReadOnlyList<VocabularyPack> packs,
        IReadOnlyList<VocabularyCorrection> corrections,
        string localeCode)
    {
        var baseLanguage = RecognitionLocaleCatalog.Get(localeCode).BaseLanguageCode;
        bool Applies(string scope) => scope == VocabularyScopes.Shared || (baseLanguage is not null && scope == baseLanguage);

        var corrector = new TranscriptCorrector();
        var phrases = entries.Where(entry => Applies(entry.Scope)).Select(entry => (entry.Phrase, entry.Scope))
            .Concat(packs.Where(pack => pack.IsEnabled && Applies(pack.Scope)).SelectMany(pack => pack.Phrases.Select(phrase => (Phrase: phrase, pack.Scope))));
        foreach (var (raw, scope) in phrases)
        {
            var phrase = VocabularyRules.Normalize(raw);
            var variants = SpokenForms.Variants(phrase, SpokenLanguages(scope));
            if (variants[0].Length > 0 && ChangesCaseOnlySafely(phrase, variants[0]))
                corrector.Add(new Rule(variants[0], phrase, Taught: false));
            // A single spoken word ("de", "three") is too common to rewrite on its own.
            foreach (var variant in variants.Skip(1).Where(variant => variant.Length > 1))
                corrector.Add(new Rule(variant, phrase, Taught: false));
        }

        foreach (var correction in corrections.Where(correction => Applies(correction.Scope)))
        {
            var words = MatchText.Tokens(VocabularyRules.Normalize(correction.Heard)).Select(token => token.Key).ToArray();
            if (words.Length > 0)
                corrector.Add(new Rule(words, VocabularyRules.Normalize(correction.Phrase), Taught: true));
        }

        return corrector;
    }

    public string Correct(string text)
    {
        if (rulesByFirstWord.Count == 0 || text.Length == 0)
            return text;

        text = VocabularyRules.Normalize(text);
        var tokens = MatchText.Tokens(text);
        var output = new System.Text.StringBuilder(text.Length);
        var copied = 0;
        var index = 0;
        while (index < tokens.Count)
        {
            var best = BestMatch(text, tokens, index);
            if (best is null)
            {
                index++;
                continue;
            }

            var start = tokens[index].Start;
            var end = tokens[index + best.Words.Length - 1].End;
            if (!string.Equals(text[start..end], best.Replacement, StringComparison.Ordinal))
            {
                output.Append(text, copied, start - copied).Append(best.Replacement);
                copied = end;
            }
            index += best.Words.Length;
        }

        return copied == 0 ? text : output.Append(text, copied, text.Length - copied).ToString();
    }

    // The longest match wins; on a tie a taught wording beats a generated one.
    private Rule? BestMatch(string text, IReadOnlyList<MatchText.Token> tokens, int index)
    {
        if (!rulesByFirstWord.TryGetValue(tokens[index].Key, out var rules))
            return null;

        Rule? best = null;
        foreach (var rule in rules)
        {
            if (index + rule.Words.Length > tokens.Count)
                continue;
            if (best is not null && (rule.Words.Length < best.Words.Length || (rule.Words.Length == best.Words.Length && (best.Taught || !rule.Taught))))
                continue;

            var matches = true;
            for (var offset = 0; offset < rule.Words.Length && matches; offset++)
            {
                var token = tokens[index + offset];
                matches = token.Key == rule.Words[offset]
                    && (offset == 0 || MatchText.IsJoiner(text.AsSpan(tokens[index + offset - 1].End, token.Start - tokens[index + offset - 1].End)));
            }
            if (matches)
                best = rule;
        }

        return best;
    }

    private void Add(Rule rule)
    {
        if (!rulesByFirstWord.TryGetValue(rule.Words[0], out var rules))
            rulesByFirstWord[rule.Words[0]] = rules = [];
        if (!rules.Any(existing => existing.Words.SequenceEqual(rule.Words) && existing.Replacement == rule.Replacement))
            rules.Add(rule);
    }

    // Number and letter words exist for English and Polish only. Both are used, because English
    // terms are often read aloud inside Polish dictation and the other way round.
    private static IReadOnlyCollection<string> SpokenLanguages(string scope) =>
        scope is VocabularyScopes.Shared or "en" or "pl" ? ["en", "pl"] : [];

    // Fixing only capitalization is safe for several words or unusual casing ("visual studio",
    // "postgresql"), but a lone ordinary word may just be that word, so it is left alone.
    private static bool ChangesCaseOnlySafely(string phrase, string[] words) =>
        words.Length > 1 || phrase.Skip(1).Any(char.IsUpper);
}
