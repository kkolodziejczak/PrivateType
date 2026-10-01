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
        Assert.Equal("Zażółć gęślą jaźń.", OfflineRecognizer.ParseTranscript("""{"text":"Zażółć gęślą jaźń."}"""));
        Assert.Throws<InvalidDataException>(() => OfflineRecognizer.ParseTranscript("""{"error":{"message":"x"}}"""));
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
        await using var recognizer = new OfflineRecognizer(server.Endpoint);
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
        await using var recognizer = new OfflineRecognizer(new Uri("http://127.0.0.1:9/v1/audio/transcriptions"));
        var updates = CollectAsync(recognizer);

        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(await updates).Text);
    }

    [Fact]
    public async Task Surfaces_an_engine_error_to_both_completion_and_updates()
    {
        using var server = new OneShotServer("""{"error":{"message":"bad"}}""", status: "400 Bad Request");
        await using var recognizer = new OfflineRecognizer(server.Endpoint);
        var updates = CollectAsync(recognizer);
        await recognizer.PushPcmAsync(new byte[] { 1, 2 }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => recognizer.CompleteAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => updates);
    }

    [Fact]
    public async Task Previews_the_audio_so_far_while_held_then_commits_one_final_pass()
    {
        using var server = new CountingServer();
        await using var recognizer = new OfflineRecognizer(server.Endpoint, TimeSpan.FromMilliseconds(20));
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
        await using var recognizer = new OfflineRecognizer(server.Endpoint, TimeSpan.FromMilliseconds(10));
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[15998], CancellationToken.None);
        await Task.Delay(150);
        Assert.Equal(0, server.Count);
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.True(Assert.Single(await updates).IsCommitted);
        Assert.Equal(1, server.Count);
    }

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

// Answers every request with "request N" and records the size of the uploaded WAV.
internal sealed class CountingServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private int count;

    public CountingServer()
    {
        listener.Start();
        Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1/audio/transcriptions");
        _ = ServeAsync();
    }

    public Uri Endpoint { get; }
    public int Count => Volatile.Read(ref count);
    public int LastWavBytes { get; private set; }

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
                var payload = Encoding.UTF8.GetBytes($"{{\"text\":\"request {Interlocked.Increment(ref count)}\"}}");
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
