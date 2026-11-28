using System.Text;
using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class OfflineRecognizerTests
{
    [Fact]
    public void Previews_only_the_latest_speech_of_a_long_dictation()
    {
        Assert.Equal(0, OfflineRecognizer.PreviewStart(OfflineRecognizer.PreviewWindowBytes / 2));
        Assert.Equal(64_000, OfflineRecognizer.PreviewStart(OfflineRecognizer.PreviewWindowBytes + 64_000));
    }

    [Fact]
    public async Task Transcribes_the_whole_hold_once_on_completion_as_16_kHz_float_samples()
    {
        var engine = new FakeEngine(_ => "Hello from the runtime.");
        await using var recognizer = new OfflineRecognizer(engine.TranscribeAsync);
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("en-US"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        for (var frame = 0; frame < 3; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 16_384), CancellationToken.None);
        Assert.Equal(0, engine.Count);
        await recognizer.CompleteAsync(CancellationToken.None);

        var update = Assert.Single(await updates);
        Assert.Equal("Hello from the runtime.", update.Text);
        Assert.True(update.IsCommitted);
        var samples = Assert.Single(engine.Received);
        Assert.Equal(3 * 1_600, samples.Length);
        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(-0.5f, samples[1]);
    }

    [Fact]
    public async Task Keeps_every_frame_recorded_before_the_local_engine_was_ready()
    {
        var engineReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<int>();
        await using var recognizer = new OfflineRecognizer(async (samples, _) =>
        {
            await engineReady.Task;
            received.Add(samples.Length);
            return "Spoken while loading.";
        });
        var updates = CollectAsync(recognizer);

        for (var frame = 0; frame < 5; frame++)
            await recognizer.PushPcmAsync(Pcm(amplitude: 8_000), CancellationToken.None);
        var completed = recognizer.CompleteAsync(CancellationToken.None);
        Assert.False(completed.IsCompleted);
        engineReady.SetResult();
        await completed;

        Assert.Equal("Spoken while loading.", Assert.Single(await updates).Text);
        Assert.Equal(5 * 1_600, Assert.Single(received));
    }

    [Fact]
    public async Task Skips_the_engine_for_an_empty_hold()
    {
        var engine = new FakeEngine();
        await using var recognizer = new OfflineRecognizer(engine.TranscribeAsync);
        var updates = CollectAsync(recognizer);

        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(await updates).Text);
        Assert.Equal(0, engine.Count);
    }

    [Fact]
    public async Task Surfaces_an_engine_error_to_both_completion_and_updates()
    {
        await using var recognizer = new OfflineRecognizer((_, _) => throw new InvalidOperationException("The engine failed."));
        var updates = CollectAsync(recognizer);
        await recognizer.PushPcmAsync(new byte[] { 1, 2 }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => recognizer.CompleteAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => updates);
    }

    [Fact]
    public async Task Still_finishes_the_text_after_the_local_engine_fails_a_preview()
    {
        var previewFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var recognizer = new OfflineRecognizer((_, _) =>
        {
            if (Interlocked.Increment(ref calls) > 1)
                return Task.FromResult("Final text.");
            previewFailed.SetResult();
            // For example the model file, which the engine rereads after losing the graphics card.
            throw new IOException("The model file is in use.");
        }, TimeSpan.FromMilliseconds(10));
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[32_000], CancellationToken.None);
        await previewFailed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.Equal("Final text.", Assert.Single(await updates).Text);
    }

    [Fact]
    public async Task Previews_the_audio_so_far_while_held_then_commits_one_final_pass()
    {
        var engine = new FakeEngine();
        await using var recognizer = new OfflineRecognizer(engine.TranscribeAsync, TimeSpan.FromMilliseconds(20));
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
        await engine.WaitForCallsAsync(1);
        await recognizer.PushPcmAsync(new byte[16000], CancellationToken.None);
        await twoPreviews.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recognizer.CompleteAsync(CancellationToken.None);
        await reader;

        Assert.True(received.Count >= 3, $"Expected previews before the final update; got {received.Count}.");
        Assert.All(received[..^1], update => Assert.False(update.IsCommitted));
        Assert.True(received[^1].IsCommitted);
        Assert.Equal(16000, engine.Received[^1].Length);
        Assert.Equal($"pass {engine.Count}", received[^1].Text);
    }

    [Fact]
    public async Task Skips_previews_until_half_a_second_of_new_audio_arrives()
    {
        var engine = new FakeEngine();
        await using var recognizer = new OfflineRecognizer(engine.TranscribeAsync, TimeSpan.FromMilliseconds(10));
        await recognizer.StartAsync(RecognitionRequest.WithoutVocabulary("pl-PL"), CancellationToken.None);
        var updates = CollectAsync(recognizer);

        await recognizer.PushPcmAsync(new byte[15998], CancellationToken.None);
        await Task.Delay(150);
        Assert.Equal(0, engine.Count);
        await recognizer.CompleteAsync(CancellationToken.None);

        Assert.True(Assert.Single(await updates).IsCommitted);
        Assert.Equal(1, engine.Count);
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
    public void Warms_up_the_server_engine_with_one_second_of_16_kHz_mono_PCM16_silence()
    {
        var wav = EngineHost.SilentWav(32_000);

        Assert.Equal(44 + 32_000, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + 32_000, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(32000, BitConverter.ToInt32(wav, 28));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(32_000, BitConverter.ToInt32(wav, 40));
        Assert.All(wav[44..], sample => Assert.Equal(0, sample));
    }

    // 100 ms of a square wave at the given amplitude.
    private static byte[] Pcm(short amplitude)
    {
        var pcm = new byte[3_200];
        for (var sample = 0; sample < pcm.Length / 2; sample++)
            BitConverter.TryWriteBytes(pcm.AsSpan(sample * 2), sample % 2 == 0 ? amplitude : (short)-amplitude);
        return pcm;
    }

    private static async Task<List<TranscriptUpdate>> CollectAsync(OfflineRecognizer recognizer)
    {
        var updates = new List<TranscriptUpdate>();
        await foreach (var update in recognizer.ReadUpdatesAsync(CancellationToken.None))
            updates.Add(update);
        return updates;
    }

    // Answers pass N with "pass N", or with respond(N), and keeps a copy of the samples it was given.
    private sealed class FakeEngine(Func<int, string>? respond = null)
    {
        private readonly List<float[]> received = [];

        public int Count
        {
            get
            {
                lock (received)
                    return received.Count;
            }
        }

        public IReadOnlyList<float[]> Received
        {
            get
            {
                lock (received)
                    return received.ToList();
            }
        }

        public Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
        {
            int pass;
            lock (received)
            {
                received.Add(samples.ToArray());
                pass = received.Count;
            }
            return Task.FromResult(respond?.Invoke(pass) ?? $"pass {pass}");
        }

        public async Task WaitForCallsAsync(int expected)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Count < expected && DateTime.UtcNow < deadline)
                await Task.Delay(5);
            Assert.True(Count >= expected, $"Expected {expected} passes; got {Count}.");
        }
    }
}
