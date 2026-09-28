namespace PrivateType.Core;

public enum RecognitionSupportTier
{
    Automatic,
    TranscriptionReady,
    BroadCoverage
}

public sealed record RecognitionLocaleDefinition(
    string Code,
    string? BaseLanguageCode,
    string DisplayName,
    RecognitionSupportTier Tier);

// The single owner of recognition locale identity. Codes are sent verbatim to the engine and
// persisted in settings. The pinned model's eight adaptation-only locales (el-GR, lt-LT, lv-LV,
// mt-MT, sl-SI, he-IL, th-TH, nn-NO) need fine-tuning and are deliberately absent.
public static class RecognitionLocaleCatalog
{
    public const string Automatic = "auto";
    public const string Polish = "pl-PL";
    public const string English = "en-US";

    private static readonly RecognitionLocaleDefinition[] Explicit =
    [
        Ready("en-US", "English (United States)"),
        Ready("en-GB", "English (United Kingdom)"),
        Ready("es-US", "Spanish (United States)"),
        Ready("es-ES", "Spanish (Spain)"),
        Ready("fr-FR", "French (France)"),
        Ready("fr-CA", "French (Canada)"),
        Ready("it-IT", "Italian (Italy)"),
        Ready("pt-BR", "Portuguese (Brazil)"),
        Ready("pt-PT", "Portuguese (Portugal)"),
        Ready("nl-NL", "Dutch (Netherlands)"),
        Ready("de-DE", "German (Germany)"),
        Ready("tr-TR", "Turkish (Turkey)"),
        Ready("ru-RU", "Russian (Russia)"),
        Ready("ar-AR", "Arabic"),
        Ready("hi-IN", "Hindi (India)"),
        Ready("ja-JP", "Japanese (Japan)"),
        Ready("ko-KR", "Korean (South Korea)"),
        Ready("vi-VN", "Vietnamese (Vietnam)"),
        Ready("uk-UA", "Ukrainian (Ukraine)"),
        Broad("pl-PL", "Polish (Poland)"),
        Broad("sv-SE", "Swedish (Sweden)"),
        Broad("cs-CZ", "Czech (Czechia)"),
        Broad("nb-NO", "Norwegian Bokmål (Norway)"),
        Broad("da-DK", "Danish (Denmark)"),
        Broad("bg-BG", "Bulgarian (Bulgaria)"),
        Broad("fi-FI", "Finnish (Finland)"),
        Broad("hr-HR", "Croatian (Croatia)"),
        Broad("sk-SK", "Slovak (Slovakia)"),
        Broad("zh-CN", "Chinese (Simplified, China)"),
        Broad("hu-HU", "Hungarian (Hungary)"),
        Broad("ro-RO", "Romanian (Romania)"),
        Broad("et-EE", "Estonian (Estonia)")
    ];

    // Automatic first, then explicit locales alphabetically by display language and region.
    public static IReadOnlyList<RecognitionLocaleDefinition> All { get; } =
        [new(Automatic, null, "Automatic", RecognitionSupportTier.Automatic),
         .. Explicit.OrderBy(locale => locale.DisplayName, StringComparer.Ordinal)];

    private static readonly Dictionary<string, RecognitionLocaleDefinition> ByCode =
        All.ToDictionary(locale => locale.Code, StringComparer.Ordinal);

    public static bool IsSupported(string? code) => code is not null && ByCode.ContainsKey(code);

    public static RecognitionLocaleDefinition Get(string code) =>
        ByCode.TryGetValue(code, out var locale)
            ? locale
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported recognition locale.");

    private static RecognitionLocaleDefinition Ready(string code, string displayName) =>
        new(code, BaseLanguage(code), displayName, RecognitionSupportTier.TranscriptionReady);

    private static RecognitionLocaleDefinition Broad(string code, string displayName) =>
        new(code, BaseLanguage(code), displayName, RecognitionSupportTier.BroadCoverage);

    private static string BaseLanguage(string code) => code[..code.IndexOf('-')];
}
