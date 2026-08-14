using System.Text.Json;

namespace PrivateType.Core;

public sealed record ShortcutBinding(RecognitionLanguage Language, int VirtualKey)
{
    public static readonly IReadOnlyList<ShortcutBinding> Defaults =
    [
        new(RecognitionLanguage.Polish, 0x52),
        new(RecognitionLanguage.English, 0x45)
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
    public static PortableSettings Default { get; } = new("default", ShortcutBinding.Defaults);
}

public sealed record SettingsLoadResult(PortableSettings Settings, string? Warning = null);

public static class PortableSettingsValidator
{
    public static string? Validate(PortableSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.MicrophoneId))
            return "Choose a microphone before saving settings.";

        if (settings.Shortcuts.Count == 0)
            return "Add at least one shortcut.";

        if (settings.Shortcuts.Any(binding => binding.Language is not RecognitionLanguage.Polish and not RecognitionLanguage.English and not RecognitionLanguage.Auto))
            return "Choose a supported recognition language.";

        if (settings.Shortcuts.Any(binding => binding.VirtualKey is < 0x30 or > 0xFE))
            return "Each shortcut must use a letter, number, or function key with Ctrl+Shift.";

        if (settings.Shortcuts.Select(binding => binding.VirtualKey).Distinct().Count() != settings.Shortcuts.Count)
            return "Each shortcut must use a different key.";

        if (settings.PanelLeftFraction is < 0 or > 1 || settings.PanelTopFraction is < 0 or > 1)
            return "The saved panel position is outside the screen.";

        if (settings.ModelIdleTimeoutMinutes is not (5 or 10 or 15 or 30))
            return "Choose a supported model idle timeout.";

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

            var settings = JsonSerializer.Deserialize<PortableSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            var validationError = settings is null ? "Settings file is empty." : PortableSettingsValidator.Validate(settings);
            return validationError is null
                ? new SettingsLoadResult(settings!)
                : new SettingsLoadResult(PortableSettings.Default, $"Saved settings were ignored: {validationError}");
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
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
