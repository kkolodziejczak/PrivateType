using System.Buffers.Binary;
using System.Threading.Channels;
using PrivateType.Core;

namespace PrivateType.App;

// For models that cannot stream (Parakeet TDT): the held audio stays in memory and is
// transcribed in one pass when the shortcut is released. Nothing is written to disk.
// With a preview interval, the latest speech is also transcribed while the shortcut is held and
// shown as provisional text, one pass at a time, so mistakes show up early.
// transcribe is the in-process engine; it takes 16 kHz mono float samples and queues a pass behind
// one still running, so the final pass waits for a preview rather than racing it.
internal sealed class OfflineRecognizer(
    Func<float[], CancellationToken, Task<string>> transcribe,
    TimeSpan? previewInterval = null,
    Action<string, (string Name, object? Value)[]>? record = null) : IStreamingRecognizer
{
    private const int SampleRate = 16000;
    private const int BytesPerSecond = SampleRate * 2;
    // Preview only once there is at least half a second of new speech to add.
    private const long MinimumNewPreviewBytes = SampleRate;
    // The bubble shows only its last few lines, so a preview covers only the latest speech and
    // costs the same however long the dictation runs.
    internal const long PreviewWindowBytes = 20L * BytesPerSecond;
    private readonly object gate = new();
    // Chunks rather than a growing stream, so no unreachable copy of the audio is left behind.
    private readonly List<byte[]> chunks = [];
    private readonly Channel<TranscriptUpdate> updates = Channel.CreateUnbounded<TranscriptUpdate>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource previewStop = new();
    private Task previewLoop = Task.CompletedTask;
    private long pcmBytes;

    // The model detects the language itself and has no vocabulary boosting, so the request
    // carries neither; phrase fixes still run on the finished text inside the session.
    public Task StartAsync(RecognitionRequest request, CancellationToken cancellationToken)
    {
        if (previewInterval is { } interval)
            previewLoop = Task.Run(() => PreviewAsync(interval, previewStop.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task PushPcmAsync(ReadOnlyMemory<byte> pcm16KhzMono, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            chunks.Add(pcm16KhzMono.ToArray());
            pcmBytes += pcm16KhzMono.Length;
        }
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopPreviewAsync();
            var text = BufferedBytes == 0 ? string.Empty : await TranscribeAsync(0, cancellationToken);
            updates.Writer.TryWrite(new TranscriptUpdate(text, true, "completed-0"));
            updates.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            updates.Writer.TryComplete(exception);
            throw;
        }
    }

    public async IAsyncEnumerable<TranscriptUpdate> ReadUpdatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
            if (update.IsCommitted)
                yield break;
        }
    }

    private long BufferedBytes
    {
        get
        {
            lock (gate)
                return pcmBytes;
        }
    }

    // A failed or slow preview never affects the final text; the next tick simply tries again.
    private async Task PreviewAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        long previewedBytes = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                var bytes = BufferedBytes;
                if (bytes - previewedBytes < MinimumNewPreviewBytes)
                    continue;

                // Release stops waiting for the preview; the engine finishes it before the final pass.
                var text = await PreviewTranscriptAsync(PreviewStart(bytes)).WaitAsync(cancellationToken);
                if (text is null)
                    continue;

                previewedBytes = bytes;
                if (!cancellationToken.IsCancellationRequested)
                    updates.Writer.TryWrite(new TranscriptUpdate(text, false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal static long PreviewStart(long bufferedBytes) => Math.Max(0, bufferedBytes - PreviewWindowBytes);

    private async Task<string?> PreviewTranscriptAsync(long startByte)
    {
        try
        {
            return await TranscribeAsync(startByte, CancellationToken.None);
        }
        // A preview is only a hint. Whatever it fails with (the engine may reread its model file),
        // the final pass decides the text.
        catch (Exception exception)
        {
            record?.Invoke("preview.failed", [("error", exception.GetType().Name)]);
            return null;
        }
    }

    private async Task StopPreviewAsync()
    {
        previewStop.Cancel();
        await previewLoop;
    }

    private async Task<string> TranscribeAsync(long startByte, CancellationToken cancellationToken)
    {
        float[] samples;
        lock (gate)
            samples = Samples(chunks, startByte, pcmBytes);
        try
        {
            return await transcribe(samples, cancellationToken);
        }
        finally
        {
            Array.Clear(samples);
        }
    }

    // The PCM16 from start to end as samples in the range -1 to 1; start is on a sample boundary.
    private static float[] Samples(IReadOnlyList<byte[]> chunks, long start, long end)
    {
        var samples = new float[(end - start) / 2];
        var index = 0;
        long position = 0;
        foreach (var chunk in chunks)
        {
            for (var offset = (int)Math.Clamp(start - position, 0, chunk.Length); offset + 1 < chunk.Length; offset += 2)
                samples[index++] = BinaryPrimitives.ReadInt16LittleEndian(chunk.AsSpan(offset)) / 32768f;
            position += chunk.Length;
        }
        return samples;
    }

    public async ValueTask DisposeAsync()
    {
        await StopPreviewAsync();
        // Clear the held audio before releasing it; it is private.
        lock (gate)
        {
            foreach (var chunk in chunks)
                Array.Clear(chunk);
            chunks.Clear();
            pcmBytes = 0;
        }
        updates.Writer.TryComplete();
        previewStop.Dispose();
    }
}
