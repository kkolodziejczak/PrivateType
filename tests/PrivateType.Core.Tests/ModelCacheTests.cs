using System.Security.Cryptography;
using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class ModelCacheTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"private-type-model-cache-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Reuses_an_exact_existing_artifact_without_downloading()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var downloader = new CountingDownloader(payload);
        var provisioner = new ModelProvisioner(directory, manifest, downloader);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(provisioner.ModelPath, payload);

        var path = await provisioner.EnsureAvailableAsync(null, CancellationToken.None);

        Assert.Equal(provisioner.ModelPath, path);
        Assert.Equal(0, downloader.CallCount);
    }

    [Fact]
    public async Task Replaces_a_corrupt_existing_artifact_with_a_verified_download()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var provisioner = new ModelProvisioner(directory, manifest, new CountingDownloader(payload));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(provisioner.ModelPath, "corrupt"u8.ToArray());

        await provisioner.EnsureAvailableAsync(null, CancellationToken.None);

        Assert.Equal(payload, File.ReadAllBytes(provisioner.ModelPath));
        Assert.True(provisioner.IsAvailable());
    }

    [Fact]
    public async Task Two_provisioners_download_and_promote_only_once()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var downloader = new GateDownloader(payload);
        var first = new ModelProvisioner(directory, manifest, downloader);
        var second = new ModelProvisioner(directory, manifest, downloader);

        var firstTask = first.EnsureAvailableAsync(null, CancellationToken.None);
        await downloader.Started.WaitAsync(TimeSpan.FromSeconds(2));
        var secondTask = second.EnsureAvailableAsync(null, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(secondTask.IsCompleted);

        downloader.Release();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, downloader.CallCount);
        Assert.True(first.IsAvailable());
        Assert.Equal(first.ModelPath, second.ModelPath);
    }

    [Fact]
    public async Task Cancelling_a_waiter_does_not_cancel_the_active_writer()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var downloader = new GateDownloader(payload);
        var writer = new ModelProvisioner(directory, manifest, downloader);
        var waiter = new ModelProvisioner(directory, manifest, downloader);

        var writerTask = writer.EnsureAvailableAsync(null, CancellationToken.None);
        await downloader.Started.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var waiterTask = waiter.EnsureAvailableAsync(null, cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiterTask);
        downloader.Release();
        await writerTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, downloader.CallCount);
        Assert.True(writer.IsAvailable());
    }

    [Fact]
    public async Task Releasing_an_abandoned_owner_allows_recovery_without_manual_cleanup()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var coordinator = new ModelCacheCoordinator(directory, manifest, TimeSpan.FromSeconds(2));
        var provisioner = new ModelProvisioner(directory, manifest, new CountingDownloader(payload));
        var abandonedOwner = await coordinator.AcquireAsync(CancellationToken.None);

        var recoveryTask = provisioner.EnsureAvailableAsync(null, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(recoveryTask.IsCompleted);
        await abandonedOwner.DisposeAsync();

        await recoveryTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(provisioner.IsAvailable());
    }

    [Fact]
    public async Task Cleans_only_matching_stale_partial_state()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var provisioner = new ModelProvisioner(directory, manifest, new CountingDownloader(payload));
        Directory.CreateDirectory(directory);
        var matchingPartial = Path.Combine(directory, $"{manifest.FileName}.{manifest.Sha256.ToLowerInvariant()}.stale.partial");
        var unrelatedPartial = Path.Combine(directory, "unrelated-model.stale.partial");
        File.WriteAllText(matchingPartial, "stale");
        File.WriteAllText(unrelatedPartial, "keep");

        await provisioner.EnsureAvailableAsync(null, CancellationToken.None);

        Assert.False(File.Exists(matchingPartial));
        Assert.True(File.Exists(unrelatedPartial));
    }

    [Fact]
    public async Task Does_not_discover_or_mutate_a_model_in_an_unrelated_old_release_folder()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var oldDirectory = Path.Combine(directory, "old-release", "models");
        Directory.CreateDirectory(oldDirectory);
        var oldPath = Path.Combine(oldDirectory, manifest.FileName);
        File.WriteAllBytes(oldPath, payload);
        var provisioner = new ModelProvisioner(Path.Combine(directory, "new-cache"), manifest, new CountingDownloader(payload));

        await provisioner.EnsureAvailableAsync(null, CancellationToken.None);

        Assert.Equal(payload, File.ReadAllBytes(oldPath));
        Assert.NotEqual(oldPath, provisioner.ModelPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static ModelManifest CreateManifest(byte[] payload) => new(
        "test", new Uri("https://example.test/model"), "model.gguf", payload.Length, Convert.ToHexString(SHA256.HashData(payload)));

    private sealed class CountingDownloader(byte[] payload) : IModelDownloadClient
    {
        public int CallCount { get; private set; }

        public Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            CallCount++;
            destination.Write(payload);
            progress?.Report(payload.Length);
            return Task.CompletedTask;
        }
    }

    private sealed class GateDownloader(byte[] payload) : IModelDownloadClient
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => started.Task;
        public int CallCount { get; private set; }

        public async Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            CallCount++;
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await destination.WriteAsync(payload, cancellationToken);
            progress?.Report(payload.Length);
        }

        public void Release() => release.TrySetResult();
    }
}
