using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using PrivateType.Core;

namespace PrivateType.App;

// For models that cannot stream (Parakeet TDT): the held audio stays in memory and is
// transcribed in one request when the shortcut is released. Nothing is written to disk.
// With a preview interval, the audio so far is also re-transcribed while the shortcut is
// held and shown as provisional text, one request at a time, so mistakes show up early.
internal sealed class OfflineRecognizer(Uri endpoint, TimeSpan? previewInterval = null) : IStreamingRecognizer
{
    private const int SampleRate = 16000;
    // Preview only once there is at least half a second of new speech to add.
    private const long MinimumNewPreviewBytes = SampleRate;
    private readonly HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
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
            var text = BufferedBytes == 0 ? string.Empty : await TranscribeAsync(cancellationToken);
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

                var text = await TranscribeAsync(cancellationToken);
                previewedBytes = bytes;
                if (!cancellationToken.IsCancellationRequested)
                    updates.Writer.TryWrite(new TranscriptUpdate(text, false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException)
            {
            }
        }
    }

    private async Task StopPreviewAsync()
    {
        previewStop.Cancel();
        await previewLoop;
    }

    private async Task<string> TranscribeAsync(CancellationToken cancellationToken)
    {
        byte[] wav;
        lock (gate)
            wav = Wav(chunks, pcmBytes);
        try
        {
            using var content = new MultipartFormDataContent();
            var audio = new ByteArrayContent(wav);
            audio.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            content.Add(audio, "file", "dictation.wav");
            content.Add(new StringContent("json"), "response_format");
            content.Add(new StringContent("true"), "automatic_punctuation");

            using var response = await client.PostAsync(endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The local ASR server reported HTTP {(int)response.StatusCode}.");

            return ParseTranscript(body);
        }
        finally
        {
            Array.Clear(wav);
        }
    }

    internal static string ParseTranscript(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : throw new InvalidDataException("The local ASR server returned no transcript.");
    }

    internal static byte[] Wav(ReadOnlySpan<byte> samples) => Wav([samples.ToArray()], samples.Length);

    // A canonical 44-byte RIFF header for 16 kHz mono PCM16 followed by the samples.
    private static byte[] Wav(IReadOnlyList<byte[]> chunks, long length)
    {
        if (length > int.MaxValue - 44)
            throw new InvalidOperationException("The dictation is too long to transcribe in one request.");

        var samplesLength = (int)length;
        var wav = new byte[44 + samplesLength];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + samplesLength);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samplesLength);
        var offset = 44;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(span[offset..]);
            offset += chunk.Length;
        }
        return wav;
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
        client.Dispose();
    }
}
