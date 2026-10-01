using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using PrivateType.Core;

namespace PrivateType.App;

// For models that cannot stream (Parakeet TDT): the held audio stays in memory and is
// transcribed in one request when the shortcut is released. Nothing is written to disk.
internal sealed class OfflineRecognizer(Uri endpoint) : IStreamingRecognizer
{
    private const int SampleRate = 16000;
    private readonly HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
    // Chunks rather than a growing stream, so no unreachable copy of the audio is left behind.
    private readonly List<byte[]> chunks = [];
    private long pcmBytes;
    private readonly TaskCompletionSource<string> transcript = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The model detects the language itself and has no vocabulary boosting, so the request
    // carries neither; phrase fixes still run on the finished text inside the session.
    public Task StartAsync(RecognitionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PushPcmAsync(ReadOnlyMemory<byte> pcm16KhzMono, CancellationToken cancellationToken)
    {
        chunks.Add(pcm16KhzMono.ToArray());
        pcmBytes += pcm16KhzMono.Length;
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            transcript.TrySetResult(pcmBytes == 0 ? string.Empty : await TranscribeAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            transcript.TrySetException(exception);
            throw;
        }
    }

    public async IAsyncEnumerable<TranscriptUpdate> ReadUpdatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = await transcript.Task.WaitAsync(cancellationToken);
        yield return new TranscriptUpdate(text, true, "completed-0");
    }

    private async Task<string> TranscribeAsync(CancellationToken cancellationToken)
    {
        var wav = Wav(chunks, pcmBytes);
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

    public ValueTask DisposeAsync()
    {
        // Clear the held audio before releasing it; it is private.
        foreach (var chunk in chunks)
            Array.Clear(chunk);
        chunks.Clear();
        pcmBytes = 0;
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
