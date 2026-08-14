using System.Security.Cryptography;

namespace PrivateType.Core;

public sealed record ModelManifest(string Version, Uri DownloadUri, string FileName, long ExpectedBytes, string Sha256);

public interface IModelDownloadClient
{
    Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);
}

public sealed class ModelProvisioner(string modelsDirectory, ModelManifest manifest, IModelDownloadClient downloader)
{
    public string ModelPath => Path.Combine(modelsDirectory, manifest.FileName);

    public async Task<string> EnsureAvailableAsync(IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (IsVerified(ModelPath))
            return ModelPath;

        Directory.CreateDirectory(modelsDirectory);
        var partialPath = $"{ModelPath}.{Guid.NewGuid():N}.partial";
        try
        {
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await downloader.DownloadAsync(manifest.DownloadUri, destination, progress, cancellationToken);

            if (!IsVerified(partialPath))
                throw new InvalidDataException("Downloaded model did not match the pinned size and SHA-256 hash.");

            File.Move(partialPath, ModelPath, true);
            return ModelPath;
        }
        finally
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);
        }
    }

    public bool IsAvailable() => IsVerified(ModelPath);

    private bool IsVerified(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != manifest.ExpectedBytes)
            return false;

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
