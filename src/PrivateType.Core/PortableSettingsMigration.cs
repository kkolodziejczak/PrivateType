using System.Text.Json.Nodes;

namespace PrivateType.Core;

// Upgrades settings JSON in memory. A readable legacy file is never rewritten on load;
// the current schema is written only by the next successful save.
internal static class PortableSettingsMigration
{
    public const int CurrentSchemaVersion = 2;

    // Schema 1 (v1.0.7 and earlier) stored shortcut languages as numeric enum values.
    private static readonly string[] SchemaOneLanguages = [RecognitionLocaleCatalog.Polish, RecognitionLocaleCatalog.English, RecognitionLocaleCatalog.Automatic];

    public static JsonNode MigrateToCurrent(JsonNode root)
    {
        if (root is not JsonObject settings)
            return root;

        var version = settings["SchemaVersion"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 1;
        if (version >= CurrentSchemaVersion)
            return settings;

        if (settings["Shortcuts"] is JsonArray shortcuts)
        {
            foreach (var shortcut in shortcuts.OfType<JsonObject>())
            {
                var language = shortcut["Language"] is JsonValue languageValue && languageValue.TryGetValue<int>(out var index) ? index : -1;
                shortcut.Remove("Language");
                // An unknown legacy value becomes an invalid code, so validation resets shortcuts with a warning.
                shortcut["LocaleCode"] = language >= 0 && language < SchemaOneLanguages.Length ? SchemaOneLanguages[language] : string.Empty;
            }
        }

        settings["SchemaVersion"] = CurrentSchemaVersion;
        return settings;
    }
}
