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
    public async Task Preserves_a_verified_artifact_when_it_is_temporarily_unreadable()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var downloader = new CountingDownloader(payload);
        var provisioner = new ModelProvisioner(directory, manifest, downloader);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(provisioner.ModelPath, payload);
        await using var exclusiveReader = new FileStream(
            provisioner.ModelPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        await Assert.ThrowsAsync<IOException>(
            () => provisioner.EnsureAvailableAsync(null, CancellationToken.None));

        Assert.Equal(0, downloader.CallCount);
        Assert.True(File.Exists(provisioner.ModelPath));
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
    public async Task Times_out_when_another_process_keeps_the_model_lease()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var owner = new ModelCacheCoordinator(directory, manifest, TimeSpan.FromSeconds(2));
        var waiter = new ModelCacheCoordinator(directory, manifest, TimeSpan.FromMilliseconds(100));
        await using var lease = await owner.AcquireAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await using var unexpectedLease = await waiter.AcquireAsync(CancellationToken.None);
        });

        Assert.Contains(directory, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acquires_a_persistent_read_only_lock_sentinel()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var coordinator = new ModelCacheCoordinator(directory, manifest, TimeSpan.FromSeconds(2));
        await using (var initialLease = await coordinator.AcquireAsync(CancellationToken.None))
        {
        }

        var lockPath = Path.Combine(directory, $".model-{manifest.Sha256.ToLowerInvariant()}.lock");
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);
        try
        {
            await using var readOnlyLease = await coordinator.AcquireAsync(CancellationToken.None);
        }
        finally
        {
            File.SetAttributes(lockPath, FileAttributes.Normal);
        }
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
    public async Task Ignores_a_matching_partial_that_is_temporarily_locked()
    {
        var payload = "verified synthetic model"u8.ToArray();
        var manifest = CreateManifest(payload);
        var provisioner = new ModelProvisioner(directory, manifest, new CountingDownloader(payload));
        Directory.CreateDirectory(directory);
        var lockedPartial = Path.Combine(directory, $"{manifest.FileName}.{manifest.Sha256.ToLowerInvariant()}.locked.partial");
        await using var lockedStream = new FileStream(lockedPartial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        await provisioner.EnsureAvailableAsync(null, CancellationToken.None);

        Assert.True(File.Exists(lockedPartial));
        Assert.True(provisioner.IsAvailable());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Rejects_an_invalid_model_hash(string? sha256)
    {
        Assert.ThrowsAny<ArgumentException>(() => ModelArtifactVerifier.NormalizeSha256(sha256!));
    }

    [Fact]
    public void Treats_an_empty_artifact_path_as_unavailable()
    {
        Assert.False(ModelArtifactVerifier.IsVerified(string.Empty, 1, new string('0', 64)));
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
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        public Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            destination.Write(payload);
            progress?.Report(payload.Length);
            return Task.CompletedTask;
        }
    }

    private sealed class GateDownloader(byte[] payload) : IModelDownloadClient
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;
        public Task Started => started.Task;
        public int CallCount => Volatile.Read(ref callCount);

        public async Task DownloadAsync(Uri source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            await destination.WriteAsync(payload, cancellationToken);
            progress?.Report(payload.Length);
        }

        public void Release() => release.TrySetResult();
    }
}
