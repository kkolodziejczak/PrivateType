using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class ReadySoundSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ready-sound-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Loading_existing_settings_preserves_preferences_and_defaults_the_ready_sound()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), """
            {
              "MicrophoneId": "wavein:test-device",
              "Shortcuts": [{ "Language": 0, "VirtualKey": 65 }],
              "PanelLeftFraction": 0.42,
              "PanelTopFraction": 0.31,
              "PanelDisplayDeviceName": "test-display",
              "StartWithWindows": true,
              "ModelIdleTimeoutMinutes": 15
            }
            """);

        var result = new PortableSettingsStore(directory).Load();

        Assert.Null(result.Warning);
        Assert.Equal("wavein:test-device", result.Settings.MicrophoneId);
        Assert.Equal(65, Assert.Single(result.Settings.Shortcuts).VirtualKey);
        Assert.Equal(0.42, result.Settings.PanelLeftFraction);
        Assert.Equal(0.31, result.Settings.PanelTopFraction);
        Assert.Equal("test-display", result.Settings.PanelDisplayDeviceName);
        Assert.True(result.Settings.StartWithWindows);
        Assert.Equal(15, result.Settings.ModelIdleTimeoutMinutes);
        Assert.Equal("ping", result.Settings.ReadySound);
        Assert.Equal(80, result.Settings.ReadySoundVolume);
        Assert.Null(result.Settings.CustomReadySoundPath);
    }

    [Theory]
    [InlineData("ping", 0)]
    [InlineData("chime", 45)]
    [InlineData("bell", 100)]
    [InlineData("custom", 80)]
    public void Saving_and_loading_retains_sound_selection_volume_and_custom_path(string sound, int volume)
    {
        var customPath = Path.Combine(directory, "ready-sound.wav");
        var store = new PortableSettingsStore(directory);
        var settings = PortableSettings.Default with
        {
            ReadySound = sound,
            ReadySoundVolume = volume,
            CustomReadySoundPath = customPath
        };

        store.Save(settings);
        var result = store.Load();

        Assert.Null(result.Warning);
        Assert.Equal(sound, result.Settings.ReadySound);
        Assert.Equal(volume, result.Settings.ReadySoundVolume);
        Assert.Equal(customPath, result.Settings.CustomReadySoundPath);
    }

    [Theory]
    [InlineData("unknown", 80, null)]
    [InlineData("ping", -1, null)]
    [InlineData("ping", 101, null)]
    [InlineData("custom", 80, null)]
    [InlineData("custom", 80, " ")]
    public void Invalid_sound_preferences_cannot_be_saved(string sound, int volume, string? path)
    {
        var settings = PortableSettings.Default with
        {
            ReadySound = sound,
            ReadySoundVolume = volume,
            CustomReadySoundPath = path
        };

        Assert.NotNull(PortableSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => new PortableSettingsStore(directory).Save(settings));
        Assert.False(File.Exists(Path.Combine(directory, "settings.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
