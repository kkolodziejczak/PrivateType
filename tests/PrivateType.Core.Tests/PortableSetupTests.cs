using System.Security.Cryptography;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class PortableSetupTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"live-dictation-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Loads_safe_defaults_when_settings_json_is_malformed()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{");

        var result = new PortableSettingsStore(directory).Load();

        Assert.Equal("default", result.Settings.MicrophoneId);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Rejects_conflicting_shortcut_bindings()
    {
        var settings = new PortableSettings("wavein:1", [
            new ShortcutBinding(RecognitionLanguage.Polish, 0x52),
            new ShortcutBinding(RecognitionLanguage.English, 0x52)
        ]);

        Assert.Equal("Each shortcut must use a different key.", PortableSettingsValidator.Validate(settings));
    }

    [Fact]
    public void Rejects_unknown_shortcut_languages()
    {
        var settings = new PortableSettings("wavein:1", [
            new ShortcutBinding((RecognitionLanguage)99, 0x52),
            new ShortcutBinding(RecognitionLanguage.English, 0x45)
        ]);

        Assert.Equal("Choose a supported recognition language.", PortableSettingsValidator.Validate(settings));
    }

    [Fact]
    public void Accepts_multiple_bindings_for_the_same_supported_language_and_automatic_recognition()
    {
        var settings = new PortableSettings("wavein:1", [
            new ShortcutBinding(RecognitionLanguage.Polish, 0x52),
            new ShortcutBinding(RecognitionLanguage.English, 0x45),
            new ShortcutBinding(RecognitionLanguage.Polish, 0x50),
            new ShortcutBinding(RecognitionLanguage.Auto, 0x41)
        ]);

        Assert.Null(PortableSettingsValidator.Validate(settings));
    }

    [Fact]
    public void Retains_the_selected_microphone_id_without_enumerating_or_replacing_it()
    {
        var store = new PortableSettingsStore(directory);
        var settings = new PortableSettings("wavein:retained-device-id", ShortcutBinding.Defaults);

        store.Save(settings);

        var loaded = store.Load().Settings;
        Assert.Equal(settings.MicrophoneId, loaded.MicrophoneId);
        Assert.Equal(settings.Shortcuts, loaded.Shortcuts);
    }

    [Fact]
    public void Retains_a_scale_independent_panel_position()
    {
        var store = new PortableSettingsStore(directory);
        var settings = PortableSettings.Default with { PanelDisplayDeviceName = "\\\\.\\DISPLAY2", PanelLeftFraction = 0.42, PanelTopFraction = 0.31, StartWithWindows = true, ModelIdleTimeoutMinutes = 15 };

        store.Save(settings);

        var loaded = store.Load().Settings;
        Assert.Equal(0.42, loaded.PanelLeftFraction);
        Assert.Equal(0.31, loaded.PanelTopFraction);
        Assert.Equal("\\\\.\\DISPLAY2", loaded.PanelDisplayDeviceName);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(15, loaded.ModelIdleTimeoutMinutes);
    }

    [Fact]
    public async Task Rejects_corrupt_download_and_leaves_no_active_or_partial_model()
    {
        var manifest = CreateManifest("expected model"u8.ToArray());
        var provisioner = new ModelProvisioner(directory, manifest, new FakeDownloader("wrong bytes"u8.ToArray()));

        await Assert.ThrowsAsync<InvalidDataException>(() => provisioner.EnsureAvailableAsync(null, CancellationToken.None));

        Assert.False(provisioner.IsAvailable());
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Cancelling_download_leaves_no_active_or_partial_model_and_retry_can_succeed()
    {
        var payload = "expected model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var provisioner = new ModelProvisioner(directory, manifest, new CancellingDownloader());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provisioner.EnsureAvailableAsync(null, new CancellationTokenSource().Token));
        Assert.False(provisioner.IsAvailable());
        Assert.Empty(Directory.GetFiles(directory));

        var retry = new ModelProvisioner(directory, manifest, new FakeDownloader(payload));
        Assert.Equal(retry.ModelPath, await retry.EnsureAvailableAsync(null, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static ModelManifest CreateManifest(byte[] payload) => new(
        "test", new Uri("https://example.test/model"), "model.gguf", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));

    private sealed class FakeDownloader(byte[] bytes) : IModelDownloadClient
    {
        public Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            destination.Write(bytes);
            progress?.Report(bytes.Length);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingDownloader : IModelDownloadClient
    {
        public Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
            => Task.FromCanceled(new CancellationToken(true));
    }
}
