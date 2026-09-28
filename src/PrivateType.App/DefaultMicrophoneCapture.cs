using PrivateType.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PrivateType.App;

internal sealed class DefaultMicrophoneCapture : IAudioCapture
{
    // 100 ms of 16 kHz PCM16 mono, matching the frame size the recognizer and meter were tuned for.
    internal const int ChunkBytes = 3_200;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private readonly MMDevice device;
    private readonly WasapiCapture input;
    private readonly byte[] pending = new byte[ChunkBytes];
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int pendingBytes;
    private volatile bool stopping;

    public event Func<ReadOnlyMemory<byte>, ValueTask>? PcmAvailable;
    public event Action<Exception>? Faulted;

    public DefaultMicrophoneCapture(string microphoneId)
    {
        device = MicrophoneCatalog.Open(microphoneId);
        // Shared mode with AUTOCONVERTPCM: Windows resamples the endpoint mix format to 16 kHz mono.
        input = new WasapiCapture(device, useEventSync: false, audioBufferMillisecondsLength: 100)
        {
            WaveFormat = new WaveFormat(16_000, 16, 1)
        };
        input.DataAvailable += OnDataAvailable;
        input.RecordingStopped += OnRecordingStopped;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        input.StartRecording();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        stopping = true;
        if (input.CaptureState == CaptureState.Stopped)
            return;

        input.StopRecording();
        try
        {
            await stopped.Task.WaitAsync(StopTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        input.DataAvailable -= OnDataAvailable;
        input.RecordingStopped -= OnRecordingStopped;
        input.Dispose();
        device.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            var offset = 0;
            while (offset < e.BytesRecorded)
            {
                var count = Math.Min(ChunkBytes - pendingBytes, e.BytesRecorded - offset);
                Buffer.BlockCopy(e.Buffer, offset, pending, pendingBytes, count);
                pendingBytes += count;
                offset += count;
                if (pendingBytes < ChunkBytes)
                    continue;

                pendingBytes = 0;
                PcmAvailable?.Invoke(pending.ToArray()).AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(exception);
            stopping = true;
            input.StopRecording();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        stopped.TrySetResult();
        if (e.Exception is not null && !stopping)
            Faulted?.Invoke(e.Exception);
    }
}
