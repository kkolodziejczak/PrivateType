using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PrivateType.Core;

namespace PrivateType.App;

internal sealed class RealtimeRecognizer(Uri endpoint) : IStreamingRecognizer
{
    private readonly ClientWebSocket socket = new();
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private const int MaximumInboundMessageBytes = 256_000;
    private string provisionalText = string.Empty;
    private long completedTranscriptSequence;

    public async Task StartAsync(RecognitionLanguage language, CancellationToken cancellationToken)
    {
        await socket.ConnectAsync(endpoint, cancellationToken);
        var languageCode = ToEngineLanguage(language);
        var update = JsonSerializer.Serialize(new { type = "session.update", session = new { sample_rate = 16000, language = languageCode, automatic_punctuation = true } });
        await SendTextAsync(update, cancellationToken);
    }

    public async Task PushPcmAsync(ReadOnlyMemory<byte> pcm16KhzMono, CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken);
        try { await socket.SendAsync(pcm16KhzMono, WebSocketMessageType.Binary, true, cancellationToken); }
        finally { sendGate.Release(); }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await SendTextAsync("{\"type\":\"input_audio_buffer.commit\"}", cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    public async IAsyncEnumerable<TranscriptUpdate> ReadUpdatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[32_768];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var payload = await ReceiveTextMessageAsync(buffer, cancellationToken);
            if (payload is null)
                yield break;

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type?.EndsWith(".delta", StringComparison.Ordinal) == true && root.TryGetProperty("delta", out var delta))
            {
                provisionalText += delta.GetString() ?? string.Empty;
                yield return new TranscriptUpdate(provisionalText, false);
            }
            if (type?.EndsWith(".completed", StringComparison.Ordinal) == true && root.TryGetProperty("transcript", out var transcript))
            {
                yield return new TranscriptUpdate(
                    transcript.GetString() ?? string.Empty,
                    true,
                    $"completed-{completedTranscriptSequence++}");
                provisionalText = string.Empty;
                completion.TrySetResult();
                yield break;
            }
            if (type == "error")
            {
                completion.TrySetException(new InvalidOperationException($"The local ASR server reported {DescribeError(root)}."));
                yield break;
            }
        }

        if (!cancellationToken.IsCancellationRequested)
            completion.TrySetException(new IOException("The local ASR server stopped sending transcript updates before finalizing dictation."));
    }

    private async Task<string?> ReceiveTextMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                completion.TrySetException(new IOException("The local ASR server closed the realtime connection before finalizing dictation."));
                return null;
            }
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("The local ASR server sent an unsupported WebSocket message.");

            if (message.Length + result.Count > MaximumInboundMessageBytes)
                throw new InvalidOperationException("The local ASR server sent an oversized realtime message.");

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    private static string DescribeError(JsonElement root)
    {
        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
            return $"error code {code.GetString()}";

        return "an unspecified error";
    }

    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "dictation finished", CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }
        socket.Dispose();
        sendGate.Dispose();
    }

    private async Task SendTextAsync(string message, CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken);
        try { await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancellationToken); }
        finally { sendGate.Release(); }
    }

    internal static string ToEngineLanguage(RecognitionLanguage language)
    {
        return language switch
        {
            RecognitionLanguage.Polish => "pl-PL",
            RecognitionLanguage.English => "en-US",
            RecognitionLanguage.Auto => "auto",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported recognition language.")
        };
    }
}
