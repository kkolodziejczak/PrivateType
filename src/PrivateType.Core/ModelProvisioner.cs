namespace PrivateType.Core;

public sealed record ModelManifest(string Version, Uri DownloadUri, string FileName, long ExpectedBytes, string Sha256);

public interface IModelDownloadClient
{
    Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);
}

public sealed class ModelProvisioner
{
    private readonly ModelManifest manifest;
    private readonly IModelDownloadClient downloader;
    private readonly ModelCacheCoordinator coordinator;
    private readonly string normalizedSha256;

    public ModelProvisioner(
        string modelsDirectory,
        ModelManifest manifest,
        IModelDownloadClient downloader,
        TimeSpan? coordinationWaitTimeout = null)
    {
        ModelsDirectory = modelsDirectory;
        this.manifest = manifest;
        this.downloader = downloader;
        normalizedSha256 = ModelArtifactVerifier.NormalizeSha256(manifest.Sha256);
        coordinator = new ModelCacheCoordinator(modelsDirectory, manifest, coordinationWaitTimeout);
    }

    public string ModelsDirectory { get; }
    public string ModelPath => Path.Combine(ModelsDirectory, manifest.FileName);

    public async Task<string> EnsureAvailableAsync(IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (IsAvailable())
            return ModelPath;

        await using var lease = await coordinator.AcquireAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsAvailable())
            return ModelPath;

        CleanMatchingPartialFiles();
        DeleteInvalidActiveArtifact();
        var partialPath = $"{ModelPath}.{normalizedSha256}.{Guid.NewGuid():N}.partial";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await downloader.DownloadAsync(manifest.DownloadUri, destination, progress, cancellationToken);

            if (!ModelArtifactVerifier.IsVerified(partialPath, manifest.ExpectedBytes, manifest.Sha256))
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

    public bool IsAvailable() => ModelArtifactVerifier.IsVerified(ModelPath, manifest.ExpectedBytes, manifest.Sha256);

    private void CleanMatchingPartialFiles()
    {
        var pattern = $"{manifest.FileName}.{normalizedSha256}.*.partial";
        foreach (var partialPath in Directory.EnumerateFiles(ModelsDirectory, pattern, SearchOption.TopDirectoryOnly))
            File.Delete(partialPath);
    }

    private void DeleteInvalidActiveArtifact()
    {
        if (File.Exists(ModelPath) && !IsAvailable())
            File.Delete(ModelPath);
    }
}
