namespace PrivateType.Core;

public sealed class DictationSessionCoordinator : IAsyncDisposable
{
    private readonly Func<RecognitionLanguage, DictationSession> createSession;
    private readonly object commandLock = new();
    private Task commands = Task.CompletedTask;
    private DictationSession? activeSession;
    private long activeHoldGeneration;
    private long holdGeneration;
    private bool hotkeyHeld;
    private bool disposed;
    private RecognitionLanguage heldLanguage;

    public DictationSessionCoordinator(Func<RecognitionLanguage, DictationSession> createSession)
    {
        this.createSession = createSession;
    }

    public Task HoldAsync(RecognitionLanguage language)
    {
        lock (commandLock)
        {
            ThrowIfDisposed();
            hotkeyHeld = true;
            heldLanguage = language;
            holdGeneration++;
            return EnqueueLocked(StartIfHeldAsync);
        }
    }

    public Task ReleaseAsync()
    {
        lock (commandLock)
        {
            if (disposed)
                return Task.CompletedTask;

            hotkeyHeld = false;
            return EnqueueLocked(FinishActiveSessionAsync);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task finish;
        lock (commandLock)
        {
            if (disposed)
                return;

            disposed = true;
            hotkeyHeld = false;
            finish = EnqueueLocked(FinishActiveSessionAsync);
        }

        await finish;
    }

    private Task EnqueueLocked(Func<Task> command)
    {
        commands = commands.ContinueWith(
            _ => command(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
        return commands;
    }

    private async Task StartIfHeldAsync()
    {
        if (!hotkeyHeld || activeSession is not null || disposed)
            return;

        var session = createSession(heldLanguage);
        var sessionHoldGeneration = holdGeneration;
        session.Faulted += _ => QueueFaultCleanup(session, sessionHoldGeneration);
        activeSession = session;
        activeHoldGeneration = sessionHoldGeneration;

        try
        {
            await session.StartAsync();
        }
        catch
        {
            await FinishSessionAsync(session);
        }
    }

    private void QueueFaultCleanup(DictationSession session, long sessionHoldGeneration)
    {
        lock (commandLock)
        {
            if (activeHoldGeneration == sessionHoldGeneration && holdGeneration == sessionHoldGeneration)
                hotkeyHeld = false;
            _ = EnqueueLocked(() => FinishSessionAsync(session));
        }
    }

    private Task FinishActiveSessionAsync()
    {
        return activeSession is null ? Task.CompletedTask : FinishSessionAsync(activeSession);
    }

    private async Task FinishSessionAsync(DictationSession session)
    {
        if (!ReferenceEquals(activeSession, session))
            return;

        try
        {
            await session.StopAsync();
        }
        finally
        {
            await session.DisposeAsync();
            if (ReferenceEquals(activeSession, session))
            {
                activeSession = null;
                activeHoldGeneration = 0;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(DictationSessionCoordinator));
    }
}
