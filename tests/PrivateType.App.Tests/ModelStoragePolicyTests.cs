using System.Security.Cryptography;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class ModelStoragePolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"private-type-storage-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Resolves_a_fresh_release_to_the_lowercase_sha_keyed_shared_cache_without_creating_models()
    {
        var appBase = Path.Combine(root, "release", "app");
        var localAppData = Path.Combine(root, "local-app-data");
        var manifest = CreateManifest();

        var location = ModelStoragePolicy.Resolve(manifest, appBase, localAppData);

        Assert.Equal(ModelStorageMode.Shared, location.Mode);
        Assert.Equal(
            Path.Combine(localAppData, "PrivateType", "models", manifest.Sha256.ToLowerInvariant()),
            location.Directory);
        Assert.False(Directory.Exists(Path.Combine(appBase, "models")));
        Assert.False(Directory.Exists(localAppData));
    }

    [Fact]
    public void Resolves_only_a_deliberately_preexisting_models_directory_to_portable_local_mode()
    {
        var appBase = Path.Combine(root, "release", "app");
        var portableModels = Path.Combine(appBase, "models");
        Directory.CreateDirectory(portableModels);

        var location = ModelStoragePolicy.Resolve(CreateManifest(), appBase, Path.Combine(root, "local-app-data"));

        Assert.Equal(ModelStorageMode.Portable, location.Mode);
        Assert.Equal(Path.GetFullPath(portableModels), location.Directory);
    }

    [Fact]
    public void Rejects_an_app_models_file_instead_of_silently_switching_storage_modes()
    {
        var appBase = Path.Combine(root, "release", "app");
        Directory.CreateDirectory(appBase);
        File.WriteAllText(Path.Combine(appBase, "models"), "not a directory");

        var exception = Assert.Throws<IOException>(() => ModelStoragePolicy.Resolve(CreateManifest(), appBase, Path.Combine(root, "local-app-data")));

        Assert.Contains("models", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-local-app-data")]
    public void Rejects_an_unusable_local_app_data_path_without_fallback(string? localAppData)
    {
        var appBase = Path.Combine(root, "release", "app");

        Assert.Throws<InvalidOperationException>(() => ModelStoragePolicy.Resolve(CreateManifest(), appBase, localAppData));
        Assert.False(Directory.Exists(Path.Combine(appBase, "models")));
    }

    [Fact]
    public void Ensures_only_portable_data_is_created_by_startup_writability_checks()
    {
        var appBase = Path.Combine(root, "release", "app");

        PortablePaths.EnsureWritable(appBase);

        Assert.True(Directory.Exists(Path.Combine(appBase, "data")));
        Assert.False(Directory.Exists(Path.Combine(appBase, "models")));
    }

    [Fact]
    public void Explains_shared_default_and_portable_local_override_in_setup_copy()
    {
        Assert.Contains("shared", ModelSetupWindow.StorageNotice(ModelStorageMode.Shared), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app\\models", ModelSetupWindow.StorageNotice(ModelStorageMode.Portable), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private static ModelManifest CreateManifest()
    {
        var payload = "synthetic model"u8.ToArray();
        return new("test", new Uri("https://example.test/model"), "model.gguf", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
    }
}
