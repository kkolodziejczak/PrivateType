using System.Diagnostics;

namespace PrivateType.Core;

public sealed class ModelCacheCoordinator
{
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private readonly string lockPath;
    private readonly TimeSpan waitTimeout;

    public ModelCacheCoordinator(string modelsDirectory, ModelManifest manifest, TimeSpan? waitTimeout = null)
    {
        ModelsDirectory = modelsDirectory;
        var modelHash = ModelArtifactVerifier.NormalizeSha256(manifest.Sha256);
        lockPath = Path.Combine(modelsDirectory, $".model-{modelHash}.lock");
        this.waitTimeout = waitTimeout ?? DefaultWaitTimeout;
        if (this.waitTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(waitTimeout), "The coordination wait timeout must be positive.");
    }

    public string ModelsDirectory { get; }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ModelsDirectory);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
                return new LockLease(stream, lockPath);
            }
            catch (IOException exception) when (IsContention(exception))
            {
                if (stopwatch.Elapsed >= waitTimeout)
                    throw new TimeoutException($"Timed out waiting for the model cache coordinator in '{ModelsDirectory}'.", exception);

                var remaining = waitTimeout - stopwatch.Elapsed;
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
            }
        }
    }

    private static bool IsContention(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33;
    }

    private sealed class LockLease(FileStream stream, string lockPath) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            try
            {
                File.Delete(lockPath);
            }
            catch (IOException)
            {
                // Another waiter may already hold the next lease.
            }
            return ValueTask.CompletedTask;
        }
    }
}
