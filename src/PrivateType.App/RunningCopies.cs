using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PrivateType.App;

// Another PrivateType application running in this Windows session. Its start time tells it apart from a later
// process that happens to reuse its id.
internal sealed record RunningCopy(int ProcessId, string ExecutablePath, DateTime StartTime, Version? Version);

internal enum CopyStartDecision
{
    Start,
    AlreadyRunning,
    Ask
}

internal static class CopyStartPolicy
{
    // Only one copy runs at a time: another copy, or a different copy that Windows starts, means asking which to use.
    internal static CopyStartDecision Decide(string currentExecutablePath, IReadOnlyList<string> runningExecutablePaths, StartupOwnershipDecision startup)
    {
        if (runningExecutablePaths.Any(path => string.Equals(path, currentExecutablePath, StringComparison.OrdinalIgnoreCase)))
            return CopyStartDecision.AlreadyRunning;

        return runningExecutablePaths.Count > 0 || startup == StartupOwnershipDecision.ConfirmCurrent
            ? CopyStartDecision.Ask
            : CopyStartDecision.Start;
    }
}

internal static class RunningCopies
{
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(5);

    // The other copies in this session that started before this one. A copy started later weighs this one in turn, so
    // two copies started together never both ask, or both give way.
    internal static IReadOnlyList<RunningCopy> FindStartedEarlier()
    {
        using var self = Process.GetCurrentProcess();
        var copies = new List<RunningCopy>();
        foreach (var process in Process.GetProcessesByName("PrivateType"))
        {
            using (process)
            {
                if (process.Id == self.Id || !InSession(process, self.SessionId))
                    continue;
                var copy = Identify(process);
                if (copy is not null && !IsLauncher(copy.ExecutablePath) && StartedBefore(copy, self.StartTime, self.Id))
                    copies.Add(copy);
            }
        }
        return copies;
    }

    internal static bool StartedBefore(RunningCopy copy, DateTime startTime, int processId) =>
        copy.StartTime < startTime || (copy.StartTime == startTime && copy.ProcessId < processId);

    internal static bool IsRunning(RunningCopy copy)
    {
        using var process = Reopen(copy);
        return process is not null;
    }

    // The launcher shares the application's name and lives only long enough to start it, beside its app folder.
    internal static bool IsLauncher(string executablePath) =>
        File.Exists(Path.Combine(Path.GetDirectoryName(executablePath) ?? "", "app", "PrivateType.exe"));

    // Asks the copy to quit and waits. A copy too old to listen, or one that doesn't quit in time, is ended with its speech engine.
    internal static void Close(RunningCopy copy)
    {
        using var process = Reopen(copy);
        if (process is null)
            return;
        if (CopyQuitSignal.TryRequest(copy.ProcessId) && process.WaitForExit(QuitTimeout))
            return;

        try
        {
            process.Kill(entireProcessTree: true);
            if (process.WaitForExit(EndTimeout))
                return;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException && process.HasExited)
        {
            return;
        }
        catch (Win32Exception)
        {
            // Not ours to end, for example a copy running as administrator.
        }
        throw new InvalidOperationException($"{ApplicationVersion.Label(copy.Version)} did not close. Quit it from its tray icon, then start this copy again.");
    }

    // The same process that was found, or null once it has exited; never a new process that reused the id.
    private static Process? Reopen(RunningCopy copy)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(copy.ProcessId);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var current = Identify(process);
        if (current is not null && current.StartTime == copy.StartTime
            && string.Equals(current.ExecutablePath, copy.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            return process;

        process.Dispose();
        return null;
    }

    private static bool InSession(Process process, int sessionId)
    {
        try
        {
            return process.SessionId == sessionId;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Exited since the list was taken.
            return false;
        }
    }

    private static RunningCopy? Identify(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return path is null || !IsPrivateType(path)
                ? null
                : new RunningCopy(process.Id, path, process.StartTime, WindowsStartupRegistration.VersionOf(path));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Exited, or not ours to inspect.
            return null;
        }
    }

    // Every PrivateType build carries these in its version resource; another program named PrivateType.exe does not.
    internal static bool IsPrivateType(string executablePath)
    {
        if (!File.Exists(executablePath))
            return false;

        var info = FileVersionInfo.GetVersionInfo(executablePath);
        return info.ProductName == "PrivateType" && string.Equals(info.InternalName, "PrivateType.dll", StringComparison.OrdinalIgnoreCase);
    }
}

// How a newer copy asks a running copy to quit through its own Quit: a named event per process id in this session.
internal static class CopyQuitSignal
{
    internal static IDisposable Listen(int processId, Action quit)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name(processId));
        var wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => quit(), null, Timeout.Infinite, executeOnlyOnce: true);
        return new Listening(signal, wait);
    }

    internal static bool TryRequest(int processId)
    {
        if (!EventWaitHandle.TryOpenExisting(Name(processId), out var signal))
            return false;

        using (signal)
            return signal.Set();
    }

    // Whether the copy can be asked to quit, so the choice says when it will be ended instead.
    internal static bool IsListening(int processId)
    {
        if (!EventWaitHandle.TryOpenExisting(Name(processId), out var signal))
            return false;

        signal.Dispose();
        return true;
    }

    private static string Name(int processId) => $"Local\\PrivateType.Quit.{processId}";

    private sealed class Listening(EventWaitHandle signal, RegisteredWaitHandle wait) : IDisposable
    {
        public void Dispose()
        {
            wait.Unregister(null);
            signal.Dispose();
        }
    }
}
