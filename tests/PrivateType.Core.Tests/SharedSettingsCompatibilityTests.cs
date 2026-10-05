using System.Text.Json.Nodes;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

// Every copy for a Windows user shares one settings file, so an older copy must not damage what a
// newer one wrote.
public sealed class SharedSettingsCompatibilityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"shared-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Saving_keeps_settings_this_version_does_not_know()
    {
        var store = Write("""{ "SchemaVersion": 2, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }], "FutureFeature": { "Level": 3 }, "FutureFlag": true }""");

        var loaded = store.Load();
        store.Save(loaded.Settings with { ReadySoundVolume = 55 });

        var saved = JsonNode.Parse(File.ReadAllText(store.SettingsPath))!.AsObject();
        Assert.Equal(3, saved["FutureFeature"]!["Level"]!.GetValue<int>());
        Assert.True(saved["FutureFlag"]!.GetValue<bool>());
        Assert.Equal(55, saved["ReadySoundVolume"]!.GetValue<int>());
        Assert.Null(loaded.Warning);
    }

    [Fact]
    public void Saving_other_settings_over_a_newer_file_keeps_its_unknown_settings_and_schema()
    {
        var store = Write("""{ "SchemaVersion": 9, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }], "FutureFeature": { "Level": 3 } }""");

        store.Save(PortableSettings.Default with { ReadySoundVolume = 20 });

        var saved = JsonNode.Parse(File.ReadAllText(store.SettingsPath))!.AsObject();
        Assert.Equal(3, saved["FutureFeature"]!["Level"]!.GetValue<int>());
        Assert.Equal(9, saved["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(20, saved["ReadySoundVolume"]!.GetValue<int>());
    }

    [Fact]
    public void Saving_over_an_unreadable_file_still_saves()
    {
        var store = Write("{ broken");

        store.Save(PortableSettings.Default with { ReadySoundVolume = 20 });

        Assert.Equal(20, store.Load().Settings.ReadySoundVolume);
    }

    [Fact]
    public void Saving_never_lowers_a_newer_schema_version()
    {
        var store = Write("""{ "SchemaVersion": 9, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }] }""");

        store.Save(store.Load().Settings);

        Assert.Equal(9, JsonNode.Parse(File.ReadAllText(store.SettingsPath))!["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Saving_older_settings_writes_the_current_schema_version()
    {
        var store = Write("""{ "MicrophoneId": "default", "Shortcuts": [{ "Language": 0, "VirtualKey": 82 }] }""");

        store.Save(store.Load().Settings);

        Assert.Equal(PortableSettingsMigration.CurrentSchemaVersion, JsonNode.Parse(File.ReadAllText(store.SettingsPath))!["SchemaVersion"]!.GetValue<int>());
    }

    private PortableSettingsStore Write(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), json);
        return new PortableSettingsStore(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
