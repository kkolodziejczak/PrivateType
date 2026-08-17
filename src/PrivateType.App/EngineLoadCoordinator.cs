namespace PrivateType.App;

internal sealed class EngineLoadCoordinator(Func<Task> load, Func<bool> isLoaded)
{
    private readonly object sync = new();
    private Task? pendingLoad;

    public bool IsLoaded => isLoaded();

    public Task EnsureLoadedAsync()
    {
        lock (sync)
        {
            if (isLoaded())
                return Task.CompletedTask;

            if (pendingLoad is { IsCompleted: false })
                return pendingLoad;

            Task loadTask;
            try
            {
                loadTask = load();
                pendingLoad = loadTask;
            }
            catch
            {
                pendingLoad = null;
                throw;
            }

            _ = ClearFailedLoadAsync(loadTask);
            return loadTask;
        }
    }

    private async Task ClearFailedLoadAsync(Task loadTask)
    {
        try
        {
            await loadTask.ConfigureAwait(false);
        }
        catch
        {
            lock (sync)
            {
                if (ReferenceEquals(pendingLoad, loadTask))
                    pendingLoad = null;
            }
        }
    }
}
