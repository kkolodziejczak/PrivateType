using PrivateType.Core;
using NAudio.Wave;

namespace PrivateType.App;

internal sealed class DefaultMicrophoneCapture : IAudioCapture
{
    private readonly WaveInEvent input;

    public event Func<ReadOnlyMemory<byte>, ValueTask>? PcmAvailable;
    public event Action<Exception>? Faulted;

    public DefaultMicrophoneCapture(string microphoneId)
    {
        input = new WaveInEvent { DeviceNumber = MicrophoneCatalog.ToDeviceNumber(microphoneId), WaveFormat = new WaveFormat(16_000, 16, 1), BufferMilliseconds = 100 };
        input.DataAvailable += OnDataAvailable;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        input.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        input.StopRecording();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        input.DataAvailable -= OnDataAvailable;
        input.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0)
            return;
        var pcm = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
        try
        {
            PcmAvailable?.Invoke(pcm).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(exception);
            input.StopRecording();
        }
    }
}
