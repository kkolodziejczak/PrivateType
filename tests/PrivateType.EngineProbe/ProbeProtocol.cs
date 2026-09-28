using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrivateType.EngineProbe;

internal sealed record SpeechContext(IReadOnlyList<string> Phrases, double Boost);

internal enum ProbeEventKind
{
    Delta,
    Completed,
    Error,
    Other
}

internal sealed record ProbeEvent(ProbeEventKind Kind, string Text);

internal static class ProbeProtocol
{
    // Mirrors RealtimeRecognizer's session.update, adding speech_contexts only when present.
    public static string SessionUpdate(string locale, IReadOnlyList<SpeechContext> contexts)
    {
        var session = new JsonObject
        {
            ["sample_rate"] = 16000,
            ["language"] = locale,
            ["automatic_punctuation"] = true
        };
        if (contexts.Count > 0)
        {
            session["speech_contexts"] = new JsonArray(contexts
                .Select(context => (JsonNode)new JsonObject
                {
                    ["phrases"] = new JsonArray(context.Phrases.Select(phrase => (JsonNode)JsonValue.Create(phrase)!).ToArray()),
                    ["boost"] = context.Boost
                })
                .ToArray());
        }

        return new JsonObject { ["type"] = "session.update", ["session"] = session }.ToJsonString();
    }

    public static string InvalidContextSessionUpdate(string locale)
    {
        var update = JsonNode.Parse(SessionUpdate(locale, []))!;
        update["session"]!["speech_contexts"] = new JsonObject { ["phrases"] = "not-an-array" };
        return update.ToJsonString();
    }

    public static int CountOccurrences(string transcript, string phrase)
    {
        var count = 0;
        var start = 0;
        while ((start = transcript.IndexOf(phrase, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + phrase.Length;
            if (IsBoundary(transcript, start - 1) && IsBoundary(transcript, end))
                count++;
            start = end;
        }

        return count;
    }

    // Distinct generated phrases whose combined UTF-8 size approaches the byte budget.
    public static IReadOnlyList<string> BoundaryPhrases(int count, int normalizedBytes)
    {
        var length = normalizedBytes / count;
        var phrases = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var prefix = $"boundary term {index:D3} ";
            phrases.Add(prefix + new string((char)('a' + index % 26), length - prefix.Length));
        }

        return phrases;
    }

    public static int NormalizedBytes(IEnumerable<string> phrases) =>
        phrases.Sum(phrase => Encoding.UTF8.GetByteCount(string.Join(' ', phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))));

    public static ProbeEvent ParseEvent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (type == "error")
            return new(ProbeEventKind.Error, root.TryGetProperty("code", out var code) ? code.ToString() : "unspecified");
        if (type?.EndsWith(".delta", StringComparison.Ordinal) == true && root.TryGetProperty("delta", out var delta))
            return new(ProbeEventKind.Delta, delta.GetString() ?? string.Empty);
        if (type?.EndsWith(".completed", StringComparison.Ordinal) == true && root.TryGetProperty("transcript", out var transcript))
            return new(ProbeEventKind.Completed, transcript.GetString() ?? string.Empty);
        return new(ProbeEventKind.Other, type ?? string.Empty);
    }

    private static bool IsBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !char.IsLetterOrDigit(text[index]);
}
