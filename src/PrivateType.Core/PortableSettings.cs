using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrivateType.Core;

public sealed record ShortcutBinding(string LocaleCode, int VirtualKey)
{
    public static readonly IReadOnlyList<ShortcutBinding> Defaults =
    [
        new(RecognitionLocaleCatalog.Polish, 0x52),
        new(RecognitionLocaleCatalog.English, 0x45)
    ];
}

public sealed record PortableSettings(
    string MicrophoneId,
    IReadOnlyList<ShortcutBinding> Shortcuts,
    double? PanelLeftFraction = null,
    double? PanelTopFraction = null,
    string? PanelDisplayDeviceName = null,
    bool StartWithWindows = false,
    int ModelIdleTimeoutMinutes = 10)
{
    public int SchemaVersion { get; init; } = PortableSettingsMigration.CurrentSchemaVersion;
    public string ReadySound { get; init; } = "ping";
    public int ReadySoundVolume { get; init; } = 80;
    public string? CustomReadySoundPath { get; init; }
    public string ShortcutMode { get; init; } = DictationShortcutModes.Hold;
    // Pasting is atomic, so an early Enter cannot send half-typed text.
    public string InsertionMode { get; init; } = TextInsertionModes.Paste;
    public IReadOnlyList<VocabularyEntry> Vocabulary { get; init; } = [];
    public IReadOnlyList<VocabularyPack> VocabularyPacks { get; init; } = [];
    public string VocabularyStrength { get; init; } = VocabularyStrengths.Normal;
    public IReadOnlyList<VocabularyCorrection> VocabularyCorrections { get; init; } = [];
    // Rewrites spoken forms and taught wordings in the finished sentence before it is inserted.
    public bool CorrectAfterDictation { get; init; } = true;

    public static PortableSettings Default { get; } = new("default", ShortcutBinding.Defaults);
}

public static class DictationShortcutModes
{
    public const string Hold = "hold";
    public const string Toggle = "toggle";
}

public static class TextInsertionModes
{
    public const string Type = "type";
    public const string Paste = "paste";
}

public sealed record SettingsLoadResult(PortableSettings Settings, string? Warning = null);

public static class PortableSettingsValidator
{
    // Each check owns one group of fields, so an invalid group can be reset on load
    // without discarding the user's other settings.
    private sealed record Check(string Name, Func<PortableSettings, string?> Validate, Func<PortableSettings, PortableSettings> Reset);

    private static readonly Check[] Checks =
    [
        new("microphone",
            settings => string.IsNullOrWhiteSpace(settings.MicrophoneId) ? "Choose a microphone before saving settings." : null,
            settings => settings with { MicrophoneId = PortableSettings.Default.MicrophoneId }),
        new("shortcuts",
            ValidateShortcuts,
            settings => settings with { Shortcuts = ShortcutBinding.Defaults }),
        new("bubble position",
            settings => settings.PanelLeftFraction is < 0 or > 1 || settings.PanelTopFraction is < 0 or > 1
                ? "The saved panel position is outside the screen."
                : null,
            settings => settings with { PanelLeftFraction = null, PanelTopFraction = null, PanelDisplayDeviceName = null }),
        new("model idle timeout",
            settings => settings.ModelIdleTimeoutMinutes is not (5 or 10 or 15 or 30) ? "Choose a supported model idle timeout." : null,
            settings => settings with { ModelIdleTimeoutMinutes = PortableSettings.Default.ModelIdleTimeoutMinutes }),
        new("ready sound",
            ValidateReadySound,
            settings => settings with { ReadySound = PortableSettings.Default.ReadySound, CustomReadySoundPath = null }),
        new("shortcut behavior",
            settings => settings.ShortcutMode is not (DictationShortcutModes.Hold or DictationShortcutModes.Toggle) ? "Choose how shortcuts start and stop dictation." : null,
            settings => settings with { ShortcutMode = DictationShortcutModes.Hold }),
        new("text insertion",
            settings => settings.InsertionMode is not (TextInsertionModes.Type or TextInsertionModes.Paste) ? "Choose how dictated text is inserted." : null,
            settings => settings with { InsertionMode = PortableSettings.Default.InsertionMode }),
        new("vocabulary",
            settings => settings.Vocabulary is null || settings.VocabularyPacks is null
                ? "Vocabulary is missing."
                : VocabularyRules.Validate(settings.Vocabulary, settings.VocabularyPacks),
            settings =>
            {
                var personal = VocabularyRules.KeepValid(settings.Vocabulary);
                return settings with { Vocabulary = personal, VocabularyPacks = VocabularyRules.KeepValidPacks(personal, settings.VocabularyPacks) };
            }),
        new("vocabulary corrections",
            settings => VocabularyCorrectionRules.Validate(settings.VocabularyCorrections),
            settings => settings with { VocabularyCorrections = VocabularyCorrectionRules.KeepValid(settings.VocabularyCorrections) }),
        new("vocabulary strength",
            settings => VocabularyStrengths.IsSupported(settings.VocabularyStrength) ? null : "Choose a supported vocabulary strength.",
            settings => settings with { VocabularyStrength = VocabularyStrengths.Normal }),
        new("ready sound volume",
            settings => settings.ReadySoundVolume is < 0 or > 100 ? "Choose a ready sound volume between 0 and 100%." : null,
            settings => settings with { ReadySoundVolume = PortableSettings.Default.ReadySoundVolume })
    ];

    public static string? Validate(PortableSettings settings)
    {
        foreach (var check in Checks)
        {
            if (check.Validate(settings) is { } error)
                return error;
        }

        return null;
    }

    public static (PortableSettings Settings, IReadOnlyList<string> ResetNames) Repair(PortableSettings settings)
    {
        var resetNames = new List<string>();
        foreach (var check in Checks)
        {
            if (check.Validate(settings) is null)
                continue;

            settings = check.Reset(settings);
            resetNames.Add(check.Name);
        }

        return (settings, resetNames);
    }

    private static string? ValidateShortcuts(PortableSettings settings)
    {
        if (settings.Shortcuts is null || settings.Shortcuts.Count == 0)
            return "Add at least one shortcut.";

        if (settings.Shortcuts.Any(binding => binding is null))
            return "Add at least one shortcut.";

        if (settings.Shortcuts.Any(binding => !RecognitionLocaleCatalog.IsSupported(binding.LocaleCode)))
            return "Choose a supported recognition language.";

        if (settings.Shortcuts.Any(binding => binding.VirtualKey is < 0x30 or > 0xFE))
            return "Each shortcut must use a letter, number, or function key with Ctrl+Shift.";

        if (settings.Shortcuts.Select(binding => binding.VirtualKey).Distinct().Count() != settings.Shortcuts.Count)
            return "Each shortcut must use a different key.";

        return null;
    }

    private static string? ValidateReadySound(PortableSettings settings)
    {
        if (settings.ReadySound is not ("ping" or "chime" or "bell" or "custom"))
            return "Choose a supported ready sound.";

        if (settings.ReadySound == "custom" && string.IsNullOrWhiteSpace(settings.CustomReadySoundPath))
            return "Choose a custom ready sound file.";

        return null;
    }
}

public sealed class PortableSettingsStore(string dataDirectory)
{
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SettingsPath => Path.Combine(dataDirectory, SettingsFileName);

    public SettingsLoadResult Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new SettingsLoadResult(PortableSettings.Default);

            var root = JsonNode.Parse(File.ReadAllText(SettingsPath));
            var settings = root is null ? null : PortableSettingsMigration.MigrateToCurrent(root).Deserialize<PortableSettings>(JsonOptions);
            if (settings is null)
                return new SettingsLoadResult(PortableSettings.Default, "Saved settings were ignored: Settings file is empty.");

            var (repaired, resetNames) = PortableSettingsValidator.Repair(settings);
            return resetNames.Count == 0
                ? new SettingsLoadResult(settings)
                : new SettingsLoadResult(repaired, $"Some saved settings were invalid and have been reset: {string.Join(", ", resetNames)}.");
        }
        catch (JsonException)
        {
            return new SettingsLoadResult(PortableSettings.Default, "Saved settings could not be read; safe defaults were restored.");
        }
        catch (IOException)
        {
            return new SettingsLoadResult(PortableSettings.Default, "Saved settings could not be read; safe defaults were restored.");
        }
    }

    public void Save(PortableSettings settings)
    {
        var validationError = PortableSettingsValidator.Validate(settings);
        if (validationError is not null)
            throw new ArgumentException(validationError, nameof(settings));

        Directory.CreateDirectory(dataDirectory);
        var temporaryPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings with { SchemaVersion = PortableSettingsMigration.CurrentSchemaVersion }, JsonOptions));
            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
