using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using PrivateType.Core;

namespace PrivateType.App;

// For models that cannot stream (Parakeet TDT): the held audio stays in memory and is
// transcribed in one request when the shortcut is released. Nothing is written to disk.
// With a preview interval, the latest speech is also transcribed while the shortcut is held and
// shown as provisional text, one request at a time, so mistakes show up early.
// The engine cannot abandon a request, so a long preview still running at release would hold up
// the final pass; restartEngine then swaps it for a fresh engine. engineEndpoint is asked before
// every request, so it always reaches the engine that is running now.
// The model can stop emitting words partway through a long dictation. When the microphone heard a
// voice well past the last recognized word, the rest is transcribed again and appended.
internal sealed class OfflineRecognizer(
    Func<Task<Uri>> engineEndpoint,
    TimeSpan? previewInterval = null,
    Func<Task>? restartEngine = null,
    Action<string, (string Name, object? Value)[]>? record = null) : IStreamingRecognizer
{
    // Reloading the model takes about a second and a half, so a preview that finishes within a
    // second is cheaper to wait for than a restart.
    internal static readonly TimeSpan PreviewFinishGrace = TimeSpan.FromSeconds(1);
    private const int SampleRate = 16000;
    private const int BytesPerSecond = SampleRate * 2;
    // Preview only once there is at least half a second of new speech to add.
    private const long MinimumNewPreviewBytes = SampleRate;
    // The bubble shows only its last few lines, so a preview covers only the latest speech and
    // costs the same however long the dictation runs.
    internal const long PreviewWindowBytes = 20L * BytesPerSecond;
    // Voice this long past the last recognized word means the model stopped early.
    private const double UncoveredVoiceLimitSeconds = 1.5;
    private const int MaximumRecoveryPasses = 3;
    private readonly HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly object gate = new();
    // Chunks rather than a growing stream, so no unreachable copy of the audio is left behind.
    private readonly List<byte[]> chunks = [];
    private readonly List<AudioFrameLevel> levels = [];
    private readonly Channel<TranscriptUpdate> updates = Channel.CreateUnbounded<TranscriptUpdate>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource previewStop = new();
    private Task previewLoop = Task.CompletedTask;
    private Task<string?> previewRequest = Task.FromResult<string?>(null);
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
            levels.Add(new AudioFrameLevel(VoiceActivity.Rms(pcm16KhzMono.Span), pcm16KhzMono.Length));
            pcmBytes += pcm16KhzMono.Length;
        }
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopPreviewAsync();
            var text = BufferedBytes == 0 ? string.Empty : await TranscribeFinalAsync(cancellationToken);
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

                // Release stops waiting for the preview but leaves the request running, so the
                // final pass can tell whether the engine is still busy with it.
                previewRequest = PreviewTranscriptAsync(PreviewStart(bytes));
                var text = await previewRequest.WaitAsync(cancellationToken);
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
            return (await TranscribeAsync(startByte, timedWords: false, CancellationToken.None)).Text;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task StopPreviewAsync()
    {
        previewStop.Cancel();
        await previewLoop;
    }

    private async Task<string> TranscribeFinalAsync(CancellationToken cancellationToken)
    {
        var preview = previewRequest;
        if (restartEngine is not null && !preview.IsCompleted &&
            await Task.WhenAny(preview, Task.Delay(PreviewFinishGrace)) != preview)
            await restartEngine();

        VoiceSummary voice;
        long totalBytes;
        lock (gate)
        {
            voice = VoiceActivity.Summarize(levels, BytesPerSecond);
            totalBytes = pcmBytes;
        }
        var firstPassStarted = Stopwatch.GetTimestamp();
        var transcript = await TranscribeAsync(0, timedWords: true, cancellationToken);
        var (text, coveredSeconds, recoveredCharacters) = await RecoverMissingEndAsync(
            transcript, voice, totalBytes, RecoveryBudget(Stopwatch.GetElapsedTime(firstPassStarted)), cancellationToken);

        record?.Invoke("transcription.coverage",
        [
            ("voicedSeconds", Math.Round(voice.VoicedSeconds, 1)),
            ("lastVoicedSeconds", Math.Round(voice.LastVoicedSeconds, 1)),
            ("lastWordSeconds", Math.Round(coveredSeconds ?? 0, 1)),
            ("recoveredCharacters", recoveredCharacters)
        ]);
        return text;
    }

    // Each pass covers less audio than the first, so twice its time is plenty; the dictation's own
    // deadline still applies on top.
    private static TimeSpan RecoveryBudget(TimeSpan firstPass) => (firstPass * 2) + TimeSpan.FromSeconds(5);

    // Recovery only ever adds text: a failed or slow pass keeps what was already recognized.
    private async Task<(string Text, double? CoveredSeconds, int RecoveredCharacters)> RecoverMissingEndAsync(
        EngineTranscript transcript, VoiceSummary voice, long totalBytes, TimeSpan budget, CancellationToken cancellationToken)
    {
        var text = transcript.Text;
        var coveredSeconds = transcript.LastWordEnd;
        var recoveredCharacters = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        for (var pass = 0; pass < MaximumRecoveryPasses && coveredSeconds is { } covered && NeedsRecovery(voice, covered); pass++)
        {
            EngineTranscript tail;
            try
            {
                tail = await TranscribeAsync(ByteAt(covered, totalBytes), timedWords: true, timeout.Token);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or OperationCanceledException)
            {
                break;
            }

            text = Join(text, tail.Text);
            recoveredCharacters += tail.Text.Trim().Length;
            // Without timings, or with no progress, another pass would only repeat the same audio.
            if (tail.LastWordEnd is not { } tailEnd || tailEnd <= 0)
                break;
            coveredSeconds = covered + tailEnd;
        }
        return (text, coveredSeconds, recoveredCharacters);
    }

    private static bool NeedsRecovery(VoiceSummary voice, double coveredSeconds) =>
        voice.LastVoicedSeconds - coveredSeconds > UncoveredVoiceLimitSeconds;

    // The sample boundary at a time in the recording, never past its end.
    private static long ByteAt(double seconds, long totalBytes) => Math.Min((long)(seconds * SampleRate) * 2, totalBytes);

    private static string Join(string text, string tail)
    {
        tail = tail.Trim();
        text = text.TrimEnd();
        return text.Length == 0 ? tail : tail.Length == 0 ? text : $"{text} {tail}";
    }

    private async Task<EngineTranscript> TranscribeAsync(long startByte, bool timedWords, CancellationToken cancellationToken)
    {
        byte[] wav;
        lock (gate)
            wav = Wav(chunks, startByte, pcmBytes);
        try
        {
            using var content = new MultipartFormDataContent();
            var audio = new ByteArrayContent(wav);
            audio.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            content.Add(audio, "file", "dictation.wav");
            // verbose_json adds word timings; the text is the same.
            content.Add(new StringContent(timedWords ? "verbose_json" : "json"), "response_format");
            content.Add(new StringContent("true"), "automatic_punctuation");

            using var response = await client.PostAsync(await engineEndpoint(), content, cancellationToken);
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

    internal static EngineTranscript ParseTranscript(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The local ASR server returned no transcript.");

        double? lastWordEnd = null;
        if (root.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
        {
            foreach (var word in words.EnumerateArray())
            {
                if (word.ValueKind == JsonValueKind.Object && word.TryGetProperty("end", out var end) && end.TryGetDouble(out var seconds))
                    lastWordEnd = Math.Max(lastWordEnd ?? 0, seconds);
            }
        }
        return new EngineTranscript(text.GetString() ?? string.Empty, lastWordEnd);
    }

    internal static byte[] Wav(ReadOnlySpan<byte> samples) => Wav([samples.ToArray()], 0, samples.Length);

    // A canonical 44-byte RIFF header for 16 kHz mono PCM16 followed by the samples from start to end.
    private static byte[] Wav(IReadOnlyList<byte[]> chunks, long start, long end)
    {
        var length = end - start;
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
        long position = 0;
        foreach (var chunk in chunks)
        {
            var skip = (int)Math.Clamp(start - position, 0, chunk.Length);
            chunk.AsSpan(skip).CopyTo(span[offset..]);
            offset += chunk.Length - skip;
            position += chunk.Length;
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
            levels.Clear();
            pcmBytes = 0;
        }
        updates.Writer.TryComplete();
        previewStop.Dispose();
        client.Dispose();
    }
}

// LastWordEnd is in seconds from the start of the audio sent, or null when the engine gave no word timings.
internal sealed record EngineTranscript(string Text, double? LastWordEnd);
