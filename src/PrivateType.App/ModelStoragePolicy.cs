using System.IO;
using PrivateType.Core;

namespace PrivateType.App;

internal enum ModelStorageMode
{
    Shared,
    Portable
}

internal sealed record ModelStorageLocation(ModelStorageMode Mode, string Directory);

internal static class ModelStoragePolicy
{
    internal static ModelStorageLocation Resolve(ModelManifest manifest, string baseDirectory, string? localAppData)
    {
        var applicationDirectory = Path.GetFullPath(baseDirectory);
        var portableDirectory = Path.Combine(applicationDirectory, "models");
        if (File.Exists(portableDirectory))
            throw new IOException($"The portable model path exists but is not a directory: {portableDirectory}");

        if (Directory.Exists(portableDirectory))
            return new ModelStorageLocation(ModelStorageMode.Portable, portableDirectory);

        if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
            throw new InvalidOperationException("Windows LocalAppData is unavailable. Restore that folder or create the app\\models folder in the extracted release before launch to select portable-local storage.");

        var localAppDataDirectory = Path.GetFullPath(localAppData);
        if (File.Exists(localAppDataDirectory))
            throw new InvalidOperationException("Windows LocalAppData is not a directory; PrivateType cannot select a safe shared model cache.");

        var modelHash = ModelArtifactVerifier.NormalizeSha256(manifest.Sha256);
        var privateTypeDirectory = Path.Combine(localAppDataDirectory, "PrivateType");
        var modelsDirectory = Path.Combine(privateTypeDirectory, "models");
        var sharedDirectory = Path.Combine(modelsDirectory, modelHash);
        foreach (var directory in new[] { privateTypeDirectory, modelsDirectory, sharedDirectory })
        {
            if (File.Exists(directory))
                throw new InvalidOperationException($"The shared model cache path exists but is not a directory: {directory}");
        }

        return new ModelStorageLocation(
            ModelStorageMode.Shared,
            sharedDirectory);
    }

    internal static ModelStorageLocation Resolve(ModelManifest manifest)
        => Resolve(
            manifest,
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
