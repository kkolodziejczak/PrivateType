using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class SettingsRepairTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"settings-repair-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Resets_only_the_invalid_setting_and_keeps_the_rest()
    {
        var result = Load("""
            {
              "MicrophoneId": "wasapi:test-endpoint",
              "Shortcuts": [{ "Language": 1, "VirtualKey": 69 }],
              "StartWithWindows": true,
              "ModelIdleTimeoutMinutes": 7,
              "ReadySound": "chime",
              "ReadySoundVolume": 40
            }
            """);

        Assert.Equal("wasapi:test-endpoint", result.Settings.MicrophoneId);
        Assert.Equal(69, Assert.Single(result.Settings.Shortcuts).VirtualKey);
        Assert.True(result.Settings.StartWithWindows);
        Assert.Equal(10, result.Settings.ModelIdleTimeoutMinutes);
        Assert.Equal("chime", result.Settings.ReadySound);
        Assert.Equal(40, result.Settings.ReadySoundVolume);
        Assert.Equal("Some saved settings were invalid and have been reset: model idle timeout.", result.Warning);
    }

    [Fact]
    public void Restores_default_shortcuts_when_the_shortcut_list_is_missing()
    {
        var result = Load("""
            {
              "MicrophoneId": "wasapi:test-endpoint",
              "ModelIdleTimeoutMinutes": 15
            }
            """);

        Assert.Equal(ShortcutBinding.Defaults, result.Settings.Shortcuts);
        Assert.Equal("wasapi:test-endpoint", result.Settings.MicrophoneId);
        Assert.Equal(15, result.Settings.ModelIdleTimeoutMinutes);
        Assert.Contains("shortcuts", result.Warning);
    }

    [Fact]
    public void Lists_every_reset_setting_and_leaves_a_valid_result()
    {
        var settings = PortableSettings.Default with
        {
            MicrophoneId = " ",
            PanelLeftFraction = 3,
            PanelTopFraction = 0.5,
            PanelDisplayDeviceName = "display",
            ReadySound = "custom",
            ReadySoundVolume = 150
        };

        var (repaired, resetNames) = PortableSettingsValidator.Repair(settings);

        Assert.Equal(["microphone", "bubble position", "ready sound", "ready sound volume"], resetNames);
        Assert.Null(PortableSettingsValidator.Validate(repaired));
        Assert.Null(repaired.PanelDisplayDeviceName);
        Assert.Equal("ping", repaired.ReadySound);
    }

    [Fact]
    public void Leaves_valid_settings_unchanged_without_a_warning()
    {
        var store = new PortableSettingsStore(directory);
        var settings = PortableSettings.Default with { ReadySound = "bell", ModelIdleTimeoutMinutes = 30 };
        store.Save(settings);

        var result = store.Load();

        Assert.Null(result.Warning);
        Assert.Equal("bell", result.Settings.ReadySound);
        Assert.Equal(30, result.Settings.ModelIdleTimeoutMinutes);
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
