namespace PrivateType.Core;

public sealed class DictationSessionCoordinator : IAsyncDisposable
{
    private readonly Func<string, DictationSession> createSession;
    private readonly object commandLock = new();
    private Task commands = Task.CompletedTask;
    private DictationSession? activeSession;
    // Completed when the active session is finished; replaced, under commandLock, for each new session.
    private TaskCompletionSource sessionEnded = new();
    private long activeHoldGeneration;
    private long holdGeneration;
    private bool hotkeyHeld;
    private bool disposed;
    private string heldLocale = RecognitionLocaleCatalog.Automatic;

    public DictationSessionCoordinator(Func<string, DictationSession> createSession)
    {
        this.createSession = createSession;
    }

    public bool IsRecording
    {
        get
        {
            lock (commandLock)
                return !disposed && hotkeyHeld && activeHoldGeneration == holdGeneration &&
                    activeSession?.State == DictationState.Recording;
        }
    }

    public Task HoldAsync(string localeCode)
    {
        lock (commandLock)
        {
            ThrowIfDisposed();
            hotkeyHeld = true;
            heldLocale = localeCode;
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

    // No hold or release is queued, and no dictation is recording or finalizing.
    public bool IsIdle
    {
        get
        {
            lock (commandLock)
                return commands.IsCompleted && activeSession is null;
        }
    }

    // Completes once the coordinator is idle. A dictation still held counts until it is released and
    // finished, so whatever waits here never takes the engine from under a recording.
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task queued, ended;
            lock (commandLock)
            {
                if (commands.IsCompleted && activeSession is null)
                    return;
                queued = commands;
                ended = activeSession is null ? Task.CompletedTask : sessionEnded.Task;
            }
            // A failed command is reported by whoever queued it; here it only means that command is over.
            await Task.WhenAll(queued, ended).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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

        // The UI thread may block on this during shutdown; never resume on its context.
        await finish.ConfigureAwait(false);
    }

    private Task EnqueueLocked(Func<Task> command)
    {
        commands = commands.ContinueWith(
            _ => command(),
            CancellationToken.None,
            // Publish the queued task before a command can enqueue fault cleanup.
            TaskContinuationOptions.None,
            TaskScheduler.Default).Unwrap();
        return commands;
    }

    private async Task StartIfHeldAsync()
    {
        if (!hotkeyHeld || activeSession is not null || disposed)
            return;

        var session = createSession(heldLocale);
        var sessionHoldGeneration = holdGeneration;
        session.Faulted += _ => QueueFaultCleanup(session, sessionHoldGeneration);
        lock (commandLock)
        {
            activeSession = session;
            activeHoldGeneration = sessionHoldGeneration;
            sessionEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

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
            TaskCompletionSource? ended = null;
            lock (commandLock)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                    activeHoldGeneration = 0;
                    ended = sessionEnded;
                }
            }
            ended?.TrySetResult();
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(DictationSessionCoordinator));
    }
}
