using System.Diagnostics;
using System.IO;
using ParakeetRuntime.Net;
using PrivateType.Core;

namespace PrivateType.App;

// Runs Parakeet TDT inside this process on the ParakeetRuntime package, instead of NVIDIA's nemo-speech
// server. It loads the file PrivateType downloaded and verified, which is the package's own pinned file.
// The engine transcribes one request at a time and queues the rest.
internal sealed class RuntimeSpeechEngine(Action<string, Exception?, (string Name, object? Value)[]?> diagnostics) : IDisposable
{
    private SpeechEngine? engine;

    public bool IsReady => engine is not null;

    public string? Backend => engine?.Backend;

    public string? LoadedDevicePreference { get; private set; }

    public async Task StartAsync(string modelPath, string devicePreference, CancellationToken cancellationToken)
    {
        Stop();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var model = new SpeechModel(Path.GetDirectoryName(modelPath)!, SpeechModel.Parakeet);
            // The package keeps a small record beside the file once it has checked it, so only the first load
            // reads the whole file. The check happens in place; nothing is copied or downloaded.
            if (!model.IsInstalled)
                await model.InstallFromFileAsync(modelPath, null, cancellationToken);

            var loaded = await SpeechEngine.LoadAsync(model, new SpeechEngineOptions(UseGpu: devicePreference != ModelDevices.Processor), cancellationToken);
            engine = loaded;
            LoadedDevicePreference = devicePreference;
            diagnostics("runtime.loaded", null,
            [
                ("backend", loaded.Backend),
                ("usingGpu", loaded.IsUsingGpu),
                ("tookSeconds", Seconds(started))
            ]);
        }
        catch (Exception exception)
        {
            diagnostics("runtime.load.failed", exception, [("error", exception.GetType().Name), ("tookSeconds", Seconds(started))]);
            throw;
        }
    }

    public async Task<string> TranscribeAsync(float[] samples16KhzMono, CancellationToken cancellationToken)
    {
        var running = engine ?? throw new InvalidOperationException("The speech engine is not loaded.");
        var started = Stopwatch.GetTimestamp();
        var text = await running.TranscribeAsync(samples16KhzMono, cancellationToken);
        diagnostics("runtime.transcribed", null,
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
        diagnostics("runtime.unloaded", null, [("tookSeconds", Seconds(started))]);
    }

    public void Dispose() => Stop();

    private static double Seconds(long started) => Math.Round(Stopwatch.GetElapsedTime(started).TotalSeconds, 2);
}
