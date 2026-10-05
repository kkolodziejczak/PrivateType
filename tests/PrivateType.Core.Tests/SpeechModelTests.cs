using System.Security.Cryptography;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class SpeechModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"private-type-speech-model-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Recommends_Parakeet_for_new_installs()
    {
        Assert.Equal(SpeechModelCatalog.ParakeetId, SpeechModelCatalog.DefaultId);
        Assert.Equal(SpeechModelCatalog.ParakeetId, PortableSettings.Default.SpeechModel);
        Assert.Equal(RecognitionStyle.Streaming, SpeechModelCatalog.Nemotron.Style);
    }

    [Fact]
    public void A_new_install_without_a_settings_file_uses_Parakeet()
    {
        Assert.Equal(SpeechModelCatalog.ParakeetId, new PortableSettingsStore(directory).Load().Settings.SpeechModel);
    }

    [Fact]
    public void Pins_Parakeet_to_NVIDIAs_official_Q8_0_GGUF_at_a_fixed_revision()
    {
        var manifest = SpeechModelCatalog.Parakeet.Manifest;

        Assert.Equal(RecognitionStyle.Offline, SpeechModelCatalog.Parakeet.Style);
        Assert.False(SpeechModelCatalog.Parakeet.UsesShortcutLanguage);
        Assert.False(SpeechModelCatalog.Parakeet.UsesVocabularyBoost);
        Assert.Equal("https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3/resolve/541d1f99c6b0c3cd0b11a95167540bb8edefd82b/parakeet-tdt-0.6b-v3.q8_0.gguf", manifest.DownloadUri.AbsoluteUri);
        Assert.Equal(713975456L, manifest.ExpectedBytes);
        Assert.Equal("e3880d0aaaaf2c308ea2c35016b2b895c423eb3fda924c1b463d1c19b7f4d32e", ModelArtifactVerifier.NormalizeSha256(manifest.Sha256));
    }

    [Theory]
    [InlineData(new string[0], SpeechModelCatalog.ParakeetId)]
    [InlineData(new[] { SpeechModelCatalog.ParakeetId }, SpeechModelCatalog.ParakeetId)]
    [InlineData(new[] { SpeechModelCatalog.NemotronId }, SpeechModelCatalog.NemotronId)]
    [InlineData(new[] { SpeechModelCatalog.NemotronId, SpeechModelCatalog.ParakeetId }, SpeechModelCatalog.NemotronId)]
    public void A_copy_without_a_saved_choice_reuses_a_verified_model_as_earlier_versions_did(string[] available, string expected)
    {
        Assert.Equal(expected, SpeechModelCatalog.ChooseWithoutSavedChoice(model => available.Contains(model.Id)));
    }

    [Fact]
    public void A_missing_settings_file_has_no_saved_model_choice()
    {
        Assert.False(new PortableSettingsStore(directory).Load().HasSavedModel);
    }

    [Theory]
    [InlineData(SpeechModelCatalog.NemotronId)]
    [InlineData(SpeechModelCatalog.ParakeetId)]
    public void A_file_broken_elsewhere_keeps_its_saved_model(string model)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), $$"""{"SchemaVersion":2,"MicrophoneId":"default","ReadySoundVolume":"loud","SpeechModel":"{{model}}"}""");

        var loaded = new PortableSettingsStore(directory).Load();

        Assert.True(loaded.HasSavedModel);
        Assert.Equal(model, loaded.Settings.SpeechModel);
        Assert.NotNull(loaded.Warning);
    }

    [Fact]
    public void An_unparseable_file_has_no_saved_model_choice()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{ not json");

        var loaded = new PortableSettingsStore(directory).Load();

        Assert.False(loaded.HasSavedModel);
        Assert.NotNull(loaded.Warning);
    }

    [Fact]
    public void Gives_every_model_a_distinct_id_file_and_hash()
    {
        var models = SpeechModelCatalog.All;

        Assert.Equal(models.Count, models.Select(model => model.Id).Distinct().Count());
        Assert.Equal(models.Count, models.Select(model => model.Manifest.FileName).Distinct().Count());
        Assert.Equal(models.Count, models.Select(model => model.Manifest.Sha256).Distinct().Count());
        Assert.All(models, model => Assert.Equal("https", model.LicenseUri.Scheme));
    }

    [Fact]
    public void Resets_an_unknown_speech_model_to_the_default_without_touching_other_settings()
    {
        var settings = PortableSettings.Default with { SpeechModel = "whisper-large", InsertionMode = TextInsertionModes.Type };

        var (repaired, resetNames) = PortableSettingsValidator.Repair(settings);

        Assert.Equal(SpeechModelCatalog.DefaultId, repaired.SpeechModel);
        Assert.Equal(TextInsertionModes.Type, repaired.InsertionMode);
        Assert.Equal(["speech model"], resetNames);
    }

    [Fact]
    public void Keeps_settings_saved_before_model_choice_on_Nemotron_which_those_versions_ran()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), """{"SchemaVersion":2,"MicrophoneId":"default","Shortcuts":[{"LocaleCode":"pl-PL","VirtualKey":82}]}""");

        var loaded = new PortableSettingsStore(directory).Load();

        Assert.Null(loaded.Warning);
        Assert.Equal(SpeechModelCatalog.NemotronId, loaded.Settings.SpeechModel);
        Assert.True(loaded.Settings.OfflinePreview);
    }

    [Fact]
    public void Round_trips_the_chosen_speech_model()
    {
        var store = new PortableSettingsStore(directory);

        store.Save(PortableSettings.Default with { SpeechModel = SpeechModelCatalog.NemotronId });

        Assert.Equal(SpeechModelCatalog.NemotronId, store.Load().Settings.SpeechModel);
    }

    [Fact]
    public async Task Reports_presence_by_size_and_deletes_only_its_own_model()
    {
        var payload = "synthetic model"u8.ToArray();
        var manifest = new ModelManifest("test", new Uri("https://example.test/model"), "model.gguf", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
        var provisioner = new ModelProvisioner(directory, manifest, new ThrowingDownloader());
        Directory.CreateDirectory(directory);
        var neighbour = Path.Combine(directory, "other.gguf");
        File.WriteAllBytes(neighbour, payload);
        Assert.False(provisioner.IsPresent());

        File.WriteAllBytes(provisioner.ModelPath, payload);
        File.WriteAllBytes($"{provisioner.ModelPath}.{ModelArtifactVerifier.NormalizeSha256(manifest.Sha256)}.abc.partial", [1]);
        Assert.True(provisioner.IsPresent());

        await provisioner.DeleteAsync(CancellationToken.None);

        Assert.False(provisioner.IsPresent());
        Assert.False(File.Exists(provisioner.ModelPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.partial"));
        Assert.True(File.Exists(neighbour));
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task Treats_deleting_a_model_that_was_never_downloaded_as_done()
    {
        var provisioner = new ModelProvisioner(Path.Combine(directory, "missing"), SpeechModelCatalog.Parakeet.Manifest, new ThrowingDownloader());

        await provisioner.DeleteAsync(CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(directory, "missing")));
    }

    [Fact]
    public async Task Refuses_to_delete_a_model_that_is_open_elsewhere()
    {
        var payload = "synthetic model"u8.ToArray();
        var manifest = new ModelManifest("test", new Uri("https://example.test/model"), "model.gguf", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
        var provisioner = new ModelProvisioner(directory, manifest, new ThrowingDownloader());
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(provisioner.ModelPath, payload);

        using (new FileStream(provisioner.ModelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => provisioner.DeleteAsync(CancellationToken.None));

        Assert.True(provisioner.IsPresent());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class ThrowingDownloader : IModelDownloadClient
    {
        public Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
            => throw new InvalidOperationException("No download expected.");
    }
}
