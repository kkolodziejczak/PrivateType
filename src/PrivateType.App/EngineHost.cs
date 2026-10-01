using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace PrivateType.App;

internal sealed class EngineHost : IDisposable
{
    private readonly EngineProcessJob engineJob = new();
    private Process? process;
    private int port;
    private bool ready;

    public Uri RealtimeEndpoint => new($"ws://127.0.0.1:{port}/v1/realtime");

    public Uri TranscriptionEndpoint => new($"http://127.0.0.1:{port}/v1/audio/transcriptions");

    // The model the running process was started with, so a switch can tell it must restart.
    public string? LoadedModelPath { get; private set; }

    public bool IsRunning => process is { HasExited: false };

    public bool IsReady => IsRunning && ready;

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
        if (IsRunning && !string.Equals(LoadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            Stop();

        if (IsReady)
            return;

        if (IsRunning && await IsEndpointReadyAsync(port, cancellationToken))
        {
            ready = true;
            return;
        }

        Stop();

        var runtime = FindRuntime();
        var executable = runtime.ExecutablePath;
        if (!File.Exists(executable) || !File.Exists(modelPath))
            throw new FileNotFoundException("The local NeMo-Speech runtime or verified pinned model is missing.");

        try
        {
            port = ReserveLoopbackPort();
            process = Process.Start(new ProcessStartInfo(executable, ServeArguments(port, modelPath))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = runtime.WorkingDirectory
            }) ?? throw new InvalidOperationException("Could not start the local speech runtime.");
            engineJob.Assign(process);
            LoadedModelPath = modelPath;

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                    throw new InvalidOperationException("The local speech runtime stopped before it became ready.");
                if (await IsEndpointReadyAsync(port, cancellationToken))
                {
                    ready = true;
                    return;
                }
                await Task.Delay(250, cancellationToken);
            }
            throw new TimeoutException("The local speech runtime did not become ready within 20 seconds.");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        ready = false;
        LoadedModelPath = null;
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

    // One realtime connection at a time, so a single HTTP worker is enough.
    // ASR compute threading is fixed inside the engine.
    internal static string ServeArguments(int port, string modelPath) =>
        $"serve --host 127.0.0.1 --port {port} --threads 1 --no-ui --asr-model \"{modelPath}\" --device cpu";

    // Each engine gets a free loopback port, so concurrent PrivateType versions
    // and unrelated local services cannot collide on a fixed port.
    internal static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<bool> IsEndpointReadyAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync($"http://127.0.0.1:{port}/ready", cancellationToken);
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

        var configured = Environment.GetEnvironmentVariable("PRIVATETYPE_ENGINE_ROOT")
            ?? Environment.GetEnvironmentVariable("LIVE_DICTATION_ENGINE_ROOT");
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
