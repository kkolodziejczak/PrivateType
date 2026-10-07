using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using PrivateType.Core;

namespace PrivateType.App;

// Runs the local NeMo-Speech engine on the fastest device that works. A freshly started engine
// takes several seconds over its first transcription, so a silent warm-up is sent right away,
// in the background; dictations ask for the endpoint through the *Async methods, which wait for
// it. If the warm-up fails on a graphics device, the engine is restarted on the processor and
// graphics are not tried again until the app restarts.
internal sealed class EngineHost(Action<string, Exception?, (string Name, object? Value)[]?> record) : IDisposable
{
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(60);
    private readonly EngineProcessJob engineJob = new();
    private readonly object sync = new();
    private Process? process;
    private int port;
    private bool ready;
    // Bumped by Stop, so a background fallback can tell that its engine was replaced meanwhile.
    private int generation;
    private Task warmUp = Task.CompletedTask;
    private string? doctorJson;
    private bool graphicsFailed;

    public Uri RealtimeEndpoint => new($"ws://127.0.0.1:{port}/v1/realtime");

    public Uri TranscriptionEndpoint => new($"http://127.0.0.1:{port}/v1/audio/transcriptions");

    // The model the running process was started with, so a switch can tell it must restart.
    public string? LoadedModelPath { get; private set; }

    // The device preference the running process was started for.
    public string? LoadedDevicePreference { get; private set; }

    public EngineDevice? Device { get; private set; }

    // The graphics device failed this run, so the engine now runs on the processor.
    public bool FellBack => graphicsFailed;

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

    public async Task StartAsync(string modelPath, string devicePreference, CancellationToken cancellationToken)
    {
        // A fallback still in progress owns the engine until it finishes.
        await warmUp;

        if (IsRunning && (!string.Equals(LoadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase) || LoadedDevicePreference != devicePreference))
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
        if (!File.Exists(runtime.ExecutablePath) || !File.Exists(modelPath))
            throw new FileNotFoundException("The local NeMo-Speech runtime or verified pinned model is missing.");

        var device = graphicsFailed ? EngineDevice.Processor : EngineDevices.Choose(ReadDevices(runtime), devicePreference);
        await LaunchAsync(runtime, modelPath, device, cancellationToken);
        LoadedDevicePreference = devicePreference;
        var startedGeneration = generation;
        warmUp = Task.Run(() => WarmUpAsync(runtime, modelPath, startedGeneration));
    }

    // The device the next start would use, for Settings to show before the model loads.
    public EngineDevice PredictDevice(string devicePreference)
    {
        var runtime = FindRuntime();
        return graphicsFailed || !File.Exists(runtime.ExecutablePath)
            ? EngineDevice.Processor
            : EngineDevices.Choose(ReadDevices(runtime), devicePreference);
    }

    // The endpoints a dictation should use: they wait for the warm-up, after which the engine
    // answers at full speed on its final device.
    public async Task<Uri> TranscriptionEndpointAsync()
    {
        await warmUp;
        return TranscriptionEndpoint;
    }

    public async Task<Uri> RealtimeEndpointAsync()
    {
        await warmUp;
        return RealtimeEndpoint;
    }

    public void Stop()
    {
        lock (sync)
        {
            generation++;
            ready = false;
            LoadedModelPath = null;
            if (process is null)
                return;

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            process.Dispose();
            process = null;
        }
    }

    public void Dispose()
    {
        Stop();
        engineJob.Dispose();
    }

    // One realtime connection at a time, so a single HTTP worker is enough. The engine sizes its
    // own compute threads (up to eight) and runs on the chosen device.
    internal static string ServeArguments(int port, string modelPath, string device) =>
        $"serve --host 127.0.0.1 --port {port} --threads 1 --no-ui --asr-model \"{modelPath}\" --device {device}";

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

    // Read once per app run: listing devices initialises every graphics driver, which takes a moment.
    private string ReadDevices(EngineRuntime runtime)
    {
        if (doctorJson is not null)
            return doctorJson;

        try
        {
            var start = new ProcessStartInfo(runtime.ExecutablePath, "--json doctor")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WorkingDirectory = runtime.WorkingDirectory
            };
            using var doctor = Process.Start(start) ?? throw new InvalidOperationException("The device check did not start.");
            var output = doctor.StandardOutput.ReadToEndAsync();
            doctorJson = doctor.WaitForExit(15_000) && output.Wait(1_000) ? output.Result : string.Empty;
            if (!doctor.HasExited)
                doctor.Kill();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            record("engine.devices.failed", exception, null);
            doctorJson = string.Empty;
        }
        return doctorJson;
    }

    private async Task LaunchAsync(EngineRuntime runtime, string modelPath, EngineDevice device, CancellationToken cancellationToken)
    {
        try
        {
            var launchPort = ReserveLoopbackPort();
            var launched = Process.Start(new ProcessStartInfo(runtime.ExecutablePath, ServeArguments(launchPort, modelPath, device.Argument))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = runtime.WorkingDirectory
            }) ?? throw new InvalidOperationException("Could not start the local speech runtime.");
            lock (sync)
            {
                process = launched;
                port = launchPort;
            }
            engineJob.Assign(launched);
            LoadedModelPath = modelPath;
            Device = device;

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (launched.HasExited)
                    throw new InvalidOperationException("The local speech runtime stopped before it became ready.");
                if (await IsEndpointReadyAsync(launchPort, cancellationToken))
                {
                    ready = true;
                    record("engine.started", null, [("device", device.Description)]);
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

    private async Task WarmUpAsync(EngineRuntime runtime, string modelPath, int startedGeneration)
    {
        if (await TryWarmUpAsync() || Device?.Kind == EngineDeviceKind.Processor)
            return;

        lock (sync)
        {
            // Unloaded or replaced meanwhile; nothing to fall back from.
            if (generation != startedGeneration)
                return;
        }
        graphicsFailed = true;
        record("engine.fallback", null, [("device", Device?.Description)]);
        Stop();
        try
        {
            await LaunchAsync(runtime, modelPath, EngineDevice.Processor, CancellationToken.None);
            await TryWarmUpAsync();
        }
        catch (Exception exception)
        {
            // The next dictation finds the engine stopped and loads it again, on the processor.
            record("engine.fallback.failed", exception, null);
        }
    }

    // False only when the engine itself failed: an error response or a broken connection. A request
    // the loaded model rejects (4xx) says nothing about the device, so it counts as warm.
    private async Task<bool> TryWarmUpAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = WarmUpTimeout };
            using var content = new MultipartFormDataContent();
            // One second of silence, 16 kHz mono PCM16.
            content.Add(new ByteArrayContent(OfflineRecognizer.Wav(new byte[32_000])), "file", "warm-up.wav");
            content.Add(new StringContent("json"), "response_format");
            using var response = await client.PostAsync(TranscriptionEndpoint, content);
            var warm = (int)response.StatusCode < 500;
            record(warm ? "engine.warmed" : "engine.warm-up.failed", null, [("device", Device?.Description)]);
            return warm;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            record("engine.warm-up.failed", exception, [("device", Device?.Description)]);
            return false;
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

    // The portable release keeps the engine next to the app; a checkout uses the runtime that
    // Get-NemoSpeechRuntime.ps1 unpacks into .engine\runtime.
    private static EngineRuntime FindRuntime()
    {
        var portableRuntime = Path.Combine(AppContext.BaseDirectory, "engine", "bin", "nemo-speech.exe");
        if (File.Exists(portableRuntime))
            return new EngineRuntime(portableRuntime, Path.GetDirectoryName(portableRuntime)!);

        var configured = Environment.GetEnvironmentVariable("PRIVATETYPE_ENGINE_ROOT")
            ?? Environment.GetEnvironmentVariable("LIVE_DICTATION_ENGINE_ROOT");
        var engineRoot = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".engine"));
        var bin = Path.Combine(engineRoot, "runtime", "bin");
        return new EngineRuntime(Path.Combine(bin, "nemo-speech.exe"), bin);
    }

    private sealed record EngineRuntime(string ExecutablePath, string WorkingDirectory);
}

internal enum EnginePrerequisiteStatus
{
    Ready,
    MissingEngine,
    CouldNotStart
}
