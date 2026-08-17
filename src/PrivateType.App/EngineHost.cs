using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace PrivateType.App;

internal sealed class EngineHost : IDisposable
{
    private const int Port = 8098;
    private readonly EngineProcessJob engineJob = new();
    private Process? process;

    public Uri RealtimeEndpoint => new($"ws://127.0.0.1:{Port}/v1/realtime");

    public bool IsRunning => process is { HasExited: false };

    internal static EnginePrerequisiteStatus VerifyPrerequisites()
    {
        var runtime = FindRuntime();
        if (!File.Exists(runtime.ExecutablePath))
            return ClassifyPrerequisites(executableExists: false, versionProbeSucceeded: false);

        try
        {
            using var probe = Process.Start(new ProcessStartInfo(runtime.ExecutablePath, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = runtime.WorkingDirectory
            });
            if (probe is not null && probe.WaitForExit(5000) && probe.ExitCode == 0)
                return ClassifyPrerequisites(executableExists: true, versionProbeSucceeded: true);
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        return ClassifyPrerequisites(executableExists: true, versionProbeSucceeded: false);
    }

    internal static EnginePrerequisiteStatus ClassifyPrerequisites(bool executableExists, bool versionProbeSucceeded)
        => !executableExists
            ? EnginePrerequisiteStatus.MissingEngine
            : versionProbeSucceeded
                ? EnginePrerequisiteStatus.Ready
                : EnginePrerequisiteStatus.CouldNotStart;

    public async Task StartAsync(string modelPath, CancellationToken cancellationToken)
    {
        if (IsRunning && await IsReadyAsync(cancellationToken))
            return;

        Stop();
        EnsureEndpointIsAvailable(await IsReadyAsync(cancellationToken));

        var runtime = FindRuntime();
        var executable = runtime.ExecutablePath;
        if (!File.Exists(executable) || !File.Exists(modelPath))
            throw new FileNotFoundException("The local NeMo-Speech runtime or verified pinned model is missing.");

        process = Process.Start(new ProcessStartInfo(executable,
            $"serve --host 127.0.0.1 --port {Port} --threads 1 --asr-model \"{modelPath}\" --device cpu")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = runtime.WorkingDirectory
        }) ?? throw new InvalidOperationException("Could not start the local speech runtime.");
        engineJob.Assign(process);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException("The local speech runtime stopped before it became ready.");
            if (await IsReadyAsync(cancellationToken))
                return;
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("The local speech runtime did not become ready within 20 seconds.");
    }

    public void Stop()
    {
        if (process is null)
            return;

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        process?.Dispose();
        process = null;
    }

    public void Dispose()
    {
        Stop();
        engineJob.Dispose();
    }

    internal static void EnsureEndpointIsAvailable(bool endpointIsReady)
    {
        if (endpointIsReady)
        {
            throw new InvalidOperationException(
                $"Silnik lokalnego dyktowania działa już na porcie {Port}. Zamknij istniejącą instancję przed uruchomieniem PrivateType.");
        }
    }

    private static async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync($"http://127.0.0.1:{Port}/ready", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static EngineRuntime FindRuntime()
    {
        var portableRuntime = Path.Combine(AppContext.BaseDirectory, "engine", "bin", "nemo-speech.exe");
        if (File.Exists(portableRuntime))
            return new EngineRuntime(portableRuntime, Path.GetDirectoryName(portableRuntime)!);

        var configured = Environment.GetEnvironmentVariable("LIVE_DICTATION_ENGINE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var root = Path.GetFullPath(configured);
            return new EngineRuntime(Path.Combine(root, "build-cpu-realtime-manual", "bin", "nemo-speech.exe"), root);
        }

        var developmentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".engine"));
        return new EngineRuntime(Path.Combine(developmentRoot, "build-cpu-realtime-manual", "bin", "nemo-speech.exe"), developmentRoot);
    }

    private sealed record EngineRuntime(string ExecutablePath, string WorkingDirectory);
}

internal enum EnginePrerequisiteStatus
{
    Ready,
    MissingEngine,
    CouldNotStart
}
