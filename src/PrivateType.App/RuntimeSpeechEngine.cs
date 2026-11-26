using System.Diagnostics;
using System.IO;
using ParakeetRuntime.Net;
using PrivateType.Core;

namespace PrivateType.App;

// Spike: Parakeet TDT runs inside this process on the ParakeetRuntime package instead of NVIDIA's
// nemo-speech server. It uses the package's own pinned GGUF model, so PrivateType's model download is
// not needed. Load once and keep it; the engine transcribes one request at a time and queues the rest.
// Its events also go to SpikeLogPath (timings and the backend only, never audio or text), so the spike
// can be checked from outside the app.
internal sealed class RuntimeSpeechEngine(Action<string, Exception?, (string Name, object? Value)[]?> diagnostics) : IDisposable
{
    private SpeechEngine? engine;

    public static SpeechModel Model { get; } = SpeechModel.Default();

    public static string SpikeLogPath { get; } = Path.Combine(Path.GetTempPath(), "PrivateType-ParakeetRuntime-spike.log");

    public bool IsReady => engine is not null;

    public string? Backend => engine?.Backend;

    public string? LoadedDevicePreference { get; private set; }

    public async Task StartAsync(string devicePreference, CancellationToken cancellationToken)
    {
        Stop();
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (!Model.IsInstalled)
            {
                Record("runtime.model.installing");
                await Model.EnsureInstalledAsync(null, cancellationToken);
            }

            var loaded = await SpeechEngine.LoadAsync(Model, new SpeechEngineOptions(UseGpu: devicePreference != ModelDevices.Processor), cancellationToken);
            engine = loaded;
            LoadedDevicePreference = devicePreference;
            Record("runtime.loaded", details:
            [
                ("backend", loaded.Backend),
                ("usingGpu", loaded.IsUsingGpu),
                ("tookSeconds", Seconds(started))
            ]);
        }
        catch (Exception exception)
        {
            Record("runtime.load.failed", exception, [("error", exception.GetType().Name), ("tookSeconds", Seconds(started))]);
            throw;
        }
    }

    public async Task<string> TranscribeAsync(float[] samples16KhzMono, CancellationToken cancellationToken)
    {
        var running = engine ?? throw new InvalidOperationException("The speech engine is not loaded.");
        var started = Stopwatch.GetTimestamp();
        var text = await running.TranscribeAsync(samples16KhzMono, cancellationToken);
        Record("runtime.transcribed", details:
        [
            ("audioSeconds", Math.Round(samples16KhzMono.Length / 16000.0, 1)),
            ("tookSeconds", Seconds(started)),
            ("backend", running.Backend)
        ]);
        return text;
    }

    // Waits for a running transcription to finish before the model is released.
    public void Stop()
    {
        var running = engine;
        engine = null;
        LoadedDevicePreference = null;
        if (running is null)
            return;
        var started = Stopwatch.GetTimestamp();
        running.Dispose();
        Record("runtime.unloaded", details: [("tookSeconds", Seconds(started))]);
    }

    public void Dispose() => Stop();

    private static double Seconds(long started) => Math.Round(Stopwatch.GetElapsedTime(started).TotalSeconds, 2);

    private void Record(string eventName, Exception? error = null, (string Name, object? Value)[]? details = null)
    {
        diagnostics(eventName, error, details);
        try
        {
            var values = string.Join(" ", (details ?? []).Select(detail => $"{detail.Name}={detail.Value}"));
            File.AppendAllText(SpikeLogPath, $"{DateTimeOffset.Now:O} {eventName} {values}{Environment.NewLine}");
        }
        // The spike log is optional; it must never fail a load or a transcription that succeeded.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
