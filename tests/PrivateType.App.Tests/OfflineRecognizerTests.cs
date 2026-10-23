using System.Net;
using System.Net.Sockets;
using System.Text;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class OfflineRecognizerTests
{
    [Fact]
    public void Wraps_PCM_in_a_16_kHz_mono_PCM16_WAV_header()
    {
        byte[] samples = [1, 2, 3, 4];

        var wav = OfflineRecognizer.Wav(samples);

        Assert.Equal(48, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(40, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(32000, BitConverter.ToInt32(wav, 28));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(4, BitConverter.ToInt32(wav, 40));
        Assert.Equal(samples, wav[44..]);
    }

    [Fact]
    public void Reads_the_transcript_from_the_engine_json_response()
    {
        Assert.Equal(new EngineTranscript("Zażółć gęślą jaźń.", null), OfflineRecognizer.ParseTranscript("""{"text":"Zażółć gęślą jaźń."}"""));
        Assert.Throws<InvalidDataException>(() => OfflineRecognizer.ParseTranscript("""{"error":{"message":"x"}}"""));
    }

    [Fact]
    public void Reads_where_the_last_word_ends_from_word_timings()
    {
        var transcript = OfflineRecognizer.ParseTranscript("""{"text":"Hello world.","words":[{"word":"Hello","start":0,"end":0.4},{"word":"world.","start":0.5,"end":1.25}]}""");

        Assert.Equal(new EngineTranscript("Hello world.", 1.25), transcript);
        Assert.Null(OfflineRecognizer.ParseTranscript("""{"text":"","words":[]}""").LastWordEnd);
        Assert.Null(OfflineRecognizer.ParseTranscript("""{"text":"a","words":[null,1,{"word":"a"}]}""").LastWordEnd);
    }

    [Fact]
    public void Previews_only_the_latest_speech_of_a_long_dictation()
    {
        Assert.Equal(0, OfflineRecognizer.PreviewStart(OfflineRecognizer.PreviewWindowBytes / 2));
        Assert.Equal(64_000, OfflineRecognizer.PreviewStart(OfflineRecognizer.PreviewWindowBytes + 64_000));
    }

    [Fact]
    public async Task Transcribes_again_the_speech_after_the_last_recognized_word_and_appends_it()
    {
        // One quiet second, five seconds of voice, then a quiet second; the model stops after two seconds.
        using var server = new CountingServer(respond: request => request == 1
            ? """{"text":"First part.","words":[{"word":"First","start":1.0,"end":1.5},{"word":"part.","start":1.6,"end":2.0}]}"""
            : """{"text":"Second part.","words":[{"word":"Second","start":0.2,"end":3.0},{"word":"part.","start":3.1,"end":3.9}]}""");
        var diagnostics = new List<(string Name, (string Name, object? Value)[] Details)>();
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint), record: (name, details) => diagnostics.Add((name, details)));
        var updates = CollectAsync(recognizer);

        for (var frame = 0; frame < 10; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 0), CancellationToken.None);
        for (var frame = 0; frame < 50; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 8_000), CancellationToken.None);
        for (var frame = 0; frame < 10; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 0), CancellationToken.None);
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal("First part. Second part.", Assert.Single(await updates).Text);
        Assert.Equal(2, server.Count);
        Assert.Equal((7 * 32_000) - 64_000 + 44, server.LastWavBytes);
        var coverage = Assert.Single(diagnostics, entry => entry.Name == "transcription.coverage").Details.ToDictionary(pair => pair.Name, pair => pair.Value);
        Assert.Equal(5.0, coverage["voicedSeconds"]);
        Assert.Equal(6.0, coverage["lastVoicedSeconds"]);
        Assert.Equal(5.9, coverage["lastWordSeconds"]);
        Assert.Equal(12, coverage["recoveredCharacters"]);
    }

    [Theory]
    // A broken recovery keeps the text already recognized.
    [InlineData("not json", "First part.")]
    // Recovered text without word timings is kept, and recovery stops there.
    [InlineData("""{"text":"Second part.","words":[]}""", "First part. Second part.")]
    public async Task Recovery_never_loses_text_already_recognized(string tailAnswer, string expected)
    {
        using var server = new CountingServer(respond: request => request == 1
            ? """{"text":"First part.","words":[{"word":"part.","start":1.6,"end":2.0}]}"""
            : tailAnswer);
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint));
        var updates = CollectAsync(recognizer);

        for (var frame = 0; frame < 10; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 0), CancellationToken.None);
        for (var frame = 0; frame < 50; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 8_000), CancellationToken.None);
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal(expected, Assert.Single(await updates).Text);
        Assert.Equal(2, server.Count);
    }

    [Fact]
    public async Task Sends_one_pass_when_the_text_reaches_the_end_of_the_voice()
    {
        using var server = new CountingServer(respond: _ => """{"text":"All of it.","words":[{"word":"it.","start":5.0,"end":5.8}]}""");
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint));
        var updates = CollectAsync(recognizer);

        for (var frame = 0; frame < 10; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 0), CancellationToken.None);
        for (var frame = 0; frame < 50; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 8_000), CancellationToken.None);
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal("All of it.", Assert.Single(await updates).Text);
        Assert.Equal(1, server.Count);
    }

    // 100 ms of a square wave at the given amplitude.
    private static byte[] Pcm(short amplitude)
    {
        var pcm = new byte[3_200];
        for (var sample = 0; sample < pcm.Length / 2; sample++)
            BitConverter.TryWriteBytes(pcm.AsSpan(sample * 2), sample % 2 == 0 ? amplitude : (short)-amplitude);
        return pcm;
    }

    [Fact]
    public void Gives_offline_models_longer_to_finish_after_release()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), DictationApplication.FinalizationTimeout(SpeechModelCatalog.Nemotron, offlinePreview: true));
        Assert.Equal(TimeSpan.FromSeconds(60), DictationApplication.FinalizationTimeout(SpeechModelCatalog.Parakeet, offlinePreview: false));
        // A preview still running on the engine's single worker delays the final pass.
        Assert.Equal(TimeSpan.FromSeconds(120), DictationApplication.FinalizationTimeout(SpeechModelCatalog.Parakeet, offlinePreview: true));
    }

    [Fact]
    public async Task Posts_the_whole_hold_once_on_completion_and_yields_one_committed_update()
    {
        using var server = new OneShotServer("""{"text":"Hello world."}""");
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint));
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("en-US"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[] { 1, 2 }, CancellationToken.None);
        await recognizer.PushPcmAsync(new byte[] { 3, 4 }, CancellationToken.None);
        Assert.False(server.Request.IsCompleted);
        await recognizer.CompleteAsync(CancellationToken.None);

        var update = Assert.Single(await updates);
        Assert.Equal("Hello world.", update.Text);
        Assert.True(update.IsCommitted);
        var request = await server.Request;
        Assert.StartsWith("POST /v1/audio/transcriptions ", request.Head, StringComparison.Ordinal);
        Assert.Contains("multipart/form-data", request.Head, StringComparison.OrdinalIgnoreCase);
        ByteAssertions.Contains(OfflineRecognizer.Wav([1, 2, 3, 4]), request.Body);
        Assert.DoesNotContain("name=language", Encoding.ASCII.GetString(request.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skips_the_request_for_an_empty_hold()
    {
        await using var recognizer = new OfflineRecognizer(At(new Uri("http://127.0.0.1:9/v1/audio/transcriptions")));
        var updates = CollectAsync(recognizer);

        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(await updates).Text);
    }

    [Fact]
    public async Task Surfaces_an_engine_error_to_both_completion_and_updates()
    {
        using var server = new OneShotServer("""{"error":{"message":"bad"}}""", status: "400 Bad Request");
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint));
        var updates = CollectAsync(recognizer);
        await recognizer.PushPcmAsync(new byte[] { 1, 2 }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => recognizer.CompleteAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => updates);
    }

    [Fact]
    public async Task Previews_the_audio_so_far_while_held_then_commits_one_final_pass()
    {
        using var server = new CountingServer();
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint), TimeSpan.FromMilliseconds(20));
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var received = new List<TranscriptUpdate>();
        var twoPreviews = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(async () =>
        {
            await foreach (var update in recognizer.ReadUpdatesAsync(CancellationToken.None))
            {
                lock (received)
                {
                    received.Add(update);
                    if (received.Count(item => !item.IsCommitted) >= 2)
                        twoPreviews.TrySetResult();
                }
            }
        });

        await recognizer.PushPcmAsync(new byte[16000], CancellationToken.None);
        await server.WaitForRequestsAsync(1);
        await recognizer.PushPcmAsync(new byte[16000], CancellationToken.None);
        await twoPreviews.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recognizer.CompleteAsync(CancellationToken.None);
        await reader;
        Assert.True(received.Count >= 3, $"Expected previews before the final update; got {received.Count}.");
        Assert.All(received[..^1], update => Assert.False(update.IsCommitted));
        Assert.True(received[^1].IsCommitted);
        Assert.Equal(32000 + 44, server.LastWavBytes);
        Assert.Equal($"request {server.Count}", received[^1].Text);
    }

    [Fact]
    public async Task Skips_previews_until_half_a_second_of_new_audio_arrives()
    {
        using var server = new CountingServer();
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint), TimeSpan.FromMilliseconds(10));
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[15998], CancellationToken.None);
        await Task.Delay(150);
        Assert.Equal(0, server.Count);
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.True(Assert.Single(await updates).IsCommitted);
        Assert.Equal(1, server.Count);
    }

    [Fact]
    public async Task Restarts_an_engine_still_busy_with_a_preview_and_sends_the_final_pass_to_the_new_one()
    {
        using var busy = new CountingServer(holdAnswers: new TaskCompletionSource().Task);
        using var fresh = new CountingServer();
        var running = busy.Endpoint;
        var restarts = 0;
        await using var recognizer = new OfflineRecognizer(() => Task.FromResult(running), TimeSpan.FromMilliseconds(10), () =>
        {
            restarts++;
            running = fresh.Endpoint;
            return Task.CompletedTask;
        });
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[16000], CancellationToken.None);
        await busy.Received.WaitAsync(TimeSpan.FromSeconds(5));
        await recognizer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("request 1", Assert.Single(await updates).Text);
        Assert.Equal(1, restarts);
        Assert.Equal(16000 + 44, fresh.LastWavBytes);
    }

    [Fact]
    public async Task Waits_for_a_preview_that_finishes_within_the_grace_instead_of_restarting()
    {
        var answer = new TaskCompletionSource();
        using var server = new CountingServer(holdAnswers: answer.Task);
        var restarts = 0;
        await using var recognizer = new OfflineRecognizer(At(server.Endpoint), TimeSpan.FromMilliseconds(10), () =>
        {
            restarts++;
            return Task.CompletedTask;
        });
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[16000], CancellationToken.None);
        await server.Received.WaitAsync(TimeSpan.FromSeconds(5));
        var completion = recognizer.CompleteAsync(CancellationToken.None);
        await Task.Delay(OfflineRecognizer.PreviewFinishGrace / 5);
        answer.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("request 2", Assert.Single(await updates).Text);
        Assert.Equal(0, restarts);
    }

    private static Func<Task<Uri>> At(Uri endpoint) => () => Task.FromResult(endpoint);

    private static async Task<List<TranscriptUpdate>> CollectAsync(OfflineRecognizer recognizer)
    {
        var updates = new List<TranscriptUpdate>();
        await foreach (var update in recognizer.ReadUpdatesAsync(CancellationToken.None))
            updates.Add(update);
        return updates;
    }

    // A minimal loopback HTTP/1.1 server that answers exactly one request.
    private sealed class OneShotServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        public OneShotServer(string responseBody, string status = "200 OK")
        {
            listener.Start();
            Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1/audio/transcriptions");
            Request = ServeAsync(responseBody, status);
        }

        public Uri Endpoint { get; }
        public Task<(string Head, byte[] Body)> Request { get; }

        private async Task<(string Head, byte[] Body)> ServeAsync(string responseBody, string status)
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var received = new MemoryStream();
            var buffer = new byte[8192];
            int headEnd;
            while ((headEnd = IndexOf(received.ToArray(), "\r\n\r\n"u8.ToArray())) < 0)
                received.Write(buffer, 0, await stream.ReadAsync(buffer));

            var head = Encoding.ASCII.GetString(received.ToArray(), 0, headEnd);
            var length = int.Parse(head.Split("\r\n").First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
            while (received.Length < headEnd + 4 + length)
                received.Write(buffer, 0, await stream.ReadAsync(buffer));

            var body = received.ToArray()[(headEnd + 4)..(headEnd + 4 + length)];
            var payload = Encoding.UTF8.GetBytes(responseBody);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(payload);
            return (head, body);
        }

        private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);

        public void Dispose() => listener.Stop();
    }
}

// Answers every request with "request N", or with respond(N), and records the size of the uploaded
// WAV. Like the engine, it serves one request at a time; holdAnswers keeps it busy until that task completes.
internal sealed class CountingServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task? holdAnswers;
    private readonly Func<int, string> respond;
    private int count;

    public CountingServer(Task? holdAnswers = null, Func<int, string>? respond = null)
    {
        this.holdAnswers = holdAnswers;
        this.respond = respond ?? (request => $"{{\"text\":\"request {request}\"}}");
        listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1/audio/transcriptions");
        _ = ServeAsync();
    }

    public Uri Endpoint { get; }
    public int Count => Volatile.Read(ref count);
    public int LastWavBytes { get; private set; }
    public Task Received => firstRequest.Task;

    public async Task WaitForRequestsAsync(int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        Assert.True(Count >= expected, $"Expected {expected} requests; got {Count}.");
    }

    private async Task ServeAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception) { return; }
            using (client)
            {
                var stream = client.GetStream();
                var received = new MemoryStream();
                var buffer = new byte[65536];
                int headEnd;
                while ((headEnd = received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8)) < 0)
                    received.Write(buffer, 0, await stream.ReadAsync(buffer));
                var head = Encoding.ASCII.GetString(received.ToArray(), 0, headEnd);
                var length = int.Parse(head.Split("\r\n").First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
                while (received.Length < headEnd + 4 + length)
                    received.Write(buffer, 0, await stream.ReadAsync(buffer));
                var body = received.ToArray()[(headEnd + 4)..];
                var riff = body.AsSpan().IndexOf("RIFF"u8);
                LastWavBytes = 8 + BitConverter.ToInt32(body, riff + 4);
                firstRequest.TrySetResult();
                if (holdAnswers is not null)
                {
                    try { await holdAnswers.WaitAsync(stop.Token); }
                    catch (OperationCanceledException) { return; }
                }
                var payload = Encoding.UTF8.GetBytes(respond(Interlocked.Increment(ref count)));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
                await stream.WriteAsync(payload);
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}

internal static class ByteAssertions
{
    public static void Contains(byte[] expected, byte[] actual)
    {
        if (actual.AsSpan().IndexOf(expected) < 0)
            throw new Xunit.Sdk.XunitException("The request body did not contain the expected WAV payload.");
    }
}
