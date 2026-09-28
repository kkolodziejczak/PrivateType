using System.Text.Json;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class RecognitionLocaleTests : IDisposable
{
    private static readonly string[] TranscriptionReady =
        ["en-US", "en-GB", "es-US", "es-ES", "fr-FR", "fr-CA", "it-IT", "pt-BR", "pt-PT", "nl-NL", "de-DE", "tr-TR", "ru-RU", "ar-AR", "hi-IN", "ja-JP", "ko-KR", "vi-VN", "uk-UA"];

    private static readonly string[] BroadCoverage =
        ["pl-PL", "sv-SE", "cs-CZ", "nb-NO", "da-DK", "bg-BG", "fi-FI", "hr-HR", "sk-SK", "zh-CN", "hu-HU", "ro-RO", "et-EE"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"locale-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Offers_automatic_plus_exactly_the_32_usable_explicit_locales()
    {
        var all = RecognitionLocaleCatalog.All;

        Assert.Equal(33, all.Count);
        Assert.Equal(33, all.Select(locale => locale.Code).Distinct().Count());
        Assert.Equal("auto", all[0].Code);
        Assert.Equal(RecognitionSupportTier.Automatic, all[0].Tier);
        Assert.Null(all[0].BaseLanguageCode);
        Assert.Equal(TranscriptionReady.Order(), all.Where(locale => locale.Tier == RecognitionSupportTier.TranscriptionReady).Select(locale => locale.Code).Order());
        Assert.Equal(BroadCoverage.Order(), all.Where(locale => locale.Tier == RecognitionSupportTier.BroadCoverage).Select(locale => locale.Code).Order());
    }

    [Theory]
    [InlineData("el-GR")]
    [InlineData("lt-LT")]
    [InlineData("lv-LV")]
    [InlineData("mt-MT")]
    [InlineData("sl-SI")]
    [InlineData("he-IL")]
    [InlineData("th-TH")]
    [InlineData("nn-NO")]
    [InlineData("PL-pl")]
    [InlineData("")]
    public void Excludes_adaptation_only_and_malformed_locales(string code)
    {
        Assert.False(RecognitionLocaleCatalog.IsSupported(code));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecognitionLocaleCatalog.Get(code));
    }

    [Theory]
    [InlineData("en-US", "en-GB", "en")]
    [InlineData("es-US", "es-ES", "es")]
    [InlineData("fr-FR", "fr-CA", "fr")]
    [InlineData("pt-BR", "pt-PT", "pt")]
    public void Maps_regional_locales_to_a_shared_base_language(string first, string second, string baseLanguage)
    {
        Assert.Equal(baseLanguage, RecognitionLocaleCatalog.Get(first).BaseLanguageCode);
        Assert.Equal(baseLanguage, RecognitionLocaleCatalog.Get(second).BaseLanguageCode);
    }

    [Fact]
    public void Sorts_explicit_locales_by_display_name_and_covers_28_base_languages()
    {
        var explicitLocales = RecognitionLocaleCatalog.All.Skip(1).ToList();

        Assert.Equal(explicitLocales.Select(locale => locale.DisplayName).Order(StringComparer.Ordinal), explicitLocales.Select(locale => locale.DisplayName));
        Assert.Equal(28, explicitLocales.Select(locale => locale.BaseLanguageCode).Distinct().Count());
    }

    [Fact]
    public void Migrates_schema_1_numeric_languages_without_losing_other_settings()
    {
        var result = Load("""
            {
              "MicrophoneId": "wasapi:test-endpoint",
              "Shortcuts": [
                { "Language": 0, "VirtualKey": 82 },
                { "Language": 1, "VirtualKey": 69 },
                { "Language": 2, "VirtualKey": 65 }
              ],
              "PanelLeftFraction": 0.25,
              "PanelTopFraction": 0.5,
              "PanelDisplayDeviceName": "display",
              "StartWithWindows": true,
              "ModelIdleTimeoutMinutes": 30,
              "ReadySound": "bell",
              "ReadySoundVolume": 55
            }
            """);

        Assert.Null(result.Warning);
        Assert.Equal(
            [new ShortcutBinding("pl-PL", 82), new ShortcutBinding("en-US", 69), new ShortcutBinding("auto", 65)],
            result.Settings.Shortcuts);
        Assert.Equal("wasapi:test-endpoint", result.Settings.MicrophoneId);
        Assert.Equal(0.25, result.Settings.PanelLeftFraction);
        Assert.Equal(0.5, result.Settings.PanelTopFraction);
        Assert.Equal("display", result.Settings.PanelDisplayDeviceName);
        Assert.True(result.Settings.StartWithWindows);
        Assert.Equal(30, result.Settings.ModelIdleTimeoutMinutes);
        Assert.Equal("bell", result.Settings.ReadySound);
        Assert.Equal(55, result.Settings.ReadySoundVolume);
    }

    [Fact]
    public void Loading_a_legacy_file_does_not_rewrite_it()
    {
        const string legacy = """{ "MicrophoneId": "default", "Shortcuts": [{ "Language": 1, "VirtualKey": 69 }] }""";

        Load(legacy);

        Assert.Equal(legacy, File.ReadAllText(Path.Combine(directory, "settings.json")));
    }

    [Fact]
    public void Resets_shortcuts_with_a_warning_when_a_legacy_language_is_unknown()
    {
        var result = Load("""{ "MicrophoneId": "default", "Shortcuts": [{ "Language": 7, "VirtualKey": 69 }], "ModelIdleTimeoutMinutes": 15 }""");

        Assert.Equal(ShortcutBinding.Defaults, result.Settings.Shortcuts);
        Assert.Equal(15, result.Settings.ModelIdleTimeoutMinutes);
        Assert.Contains("shortcuts", result.Warning);
    }

    [Fact]
    public void Rejects_unsupported_locale_codes_instead_of_falling_back_to_automatic()
    {
        var settings = PortableSettings.Default with { Shortcuts = [new ShortcutBinding("th-TH", 0x52)] };

        Assert.Equal("Choose a supported recognition language.", PortableSettingsValidator.Validate(settings));

        var result = Load("""{ "SchemaVersion": 2, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "xx-XX", "VirtualKey": 69 }] }""");
        Assert.Equal(ShortcutBinding.Defaults, result.Settings.Shortcuts);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Saves_schema_2_with_locale_codes_and_reloads_it()
    {
        var store = new PortableSettingsStore(directory);
        var settings = PortableSettings.Default with { Shortcuts = [new ShortcutBinding("es-ES", 0x53), new ShortcutBinding("auto", 0x41)] };

        store.Save(settings);

        using var document = JsonDocument.Parse(File.ReadAllText(store.SettingsPath));
        Assert.Equal(2, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        var shortcut = document.RootElement.GetProperty("Shortcuts")[0];
        Assert.Equal("es-ES", shortcut.GetProperty("LocaleCode").GetString());
        Assert.False(shortcut.TryGetProperty("Language", out _));
        var reloaded = store.Load();
        Assert.Null(reloaded.Warning);
        Assert.Equal(settings.Shortcuts, reloaded.Settings.Shortcuts);
    }

    [Fact]
    public void New_installs_keep_the_polish_and_english_defaults()
    {
        Assert.Equal([new ShortcutBinding("pl-PL", 0x52), new ShortcutBinding("en-US", 0x45)], PortableSettings.Default.Shortcuts);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private SettingsLoadResult Load(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), json);
        return new PortableSettingsStore(directory).Load();
    }
}
