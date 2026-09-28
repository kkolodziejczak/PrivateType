using System.Text.Json;
using PrivateType.EngineProbe;
using Xunit;

namespace PrivateType.EngineProbe.Tests;

public sealed class ProbeProtocolTests
{
    [Fact]
    public void Baseline_session_update_matches_the_production_request_without_contexts()
    {
        using var document = JsonDocument.Parse(ProbeProtocol.SessionUpdate("en-US", []));
        var session = document.RootElement.GetProperty("session");

        Assert.Equal("session.update", document.RootElement.GetProperty("type").GetString());
        Assert.Equal(16000, session.GetProperty("sample_rate").GetInt32());
        Assert.Equal("en-US", session.GetProperty("language").GetString());
        Assert.True(session.GetProperty("automatic_punctuation").GetBoolean());
        Assert.False(session.TryGetProperty("speech_contexts", out _));
        Assert.False(session.TryGetProperty("prompt", out _));
    }

    [Fact]
    public void Grouped_contexts_serialize_as_phrase_arrays_with_numeric_boosts()
    {
        var json = ProbeProtocol.SessionUpdate("pl-PL", [new(["alpha", "beta"], 1.5), new(["gamma"], 3)]);

        using var document = JsonDocument.Parse(json);
        var contexts = document.RootElement.GetProperty("session").GetProperty("speech_contexts");
        Assert.Equal(2, contexts.GetArrayLength());
        Assert.Equal(["alpha", "beta"], contexts[0].GetProperty("phrases").EnumerateArray().Select(phrase => phrase.GetString()));
        Assert.Equal(1.5, contexts[0].GetProperty("boost").GetDouble());
        Assert.Equal(3, contexts[1].GetProperty("boost").GetDouble());
    }

    [Fact]
    public void Invalid_shape_request_sends_contexts_as_an_object()
    {
        using var document = JsonDocument.Parse(ProbeProtocol.InvalidContextSessionUpdate("en-US"));

        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("session").GetProperty("speech_contexts").ValueKind);
    }

    [Theory]
    [InlineData("We deployed Kubernetes today.", "Kubernetes", 1)]
    [InlineData("kubernetes and KUBERNETES", "Kubernetes", 2)]
    [InlineData("We deployed Cooper Netties today.", "Kubernetes", 0)]
    [InlineData("The PostgreSQLs are fine.", "PostgreSQL", 0)]
    [InlineData("Run Tail scale now, then Tailscale.", "Tailscale", 1)]
    public void Counts_whole_phrase_occurrences_case_insensitively(string transcript, string phrase, int expected)
    {
        Assert.Equal(expected, ProbeProtocol.CountOccurrences(transcript, phrase));
    }

    [Fact]
    public void Boundary_payload_reaches_the_requested_phrase_count_and_normalized_size()
    {
        var phrases = ProbeProtocol.BoundaryPhrases(200, 16 * 1024);

        Assert.Equal(200, phrases.Count);
        Assert.Equal(200, phrases.Distinct(StringComparer.Ordinal).Count());
        Assert.InRange(ProbeProtocol.NormalizedBytes(phrases), 16 * 1024 - 200, 16 * 1024);
    }

    [Fact]
    public void Parses_completed_delta_and_error_events()
    {
        Assert.Equal(new ProbeEvent(ProbeEventKind.Delta, "hel"), ProbeProtocol.ParseEvent("""{"type":"conversation.item.input_audio_transcription.delta","delta":"hel"}"""));
        Assert.Equal(new ProbeEvent(ProbeEventKind.Completed, "hello"), ProbeProtocol.ParseEvent("""{"type":"conversation.item.input_audio_transcription.completed","transcript":"hello"}"""));
        Assert.Equal(ProbeEventKind.Error, ProbeProtocol.ParseEvent("""{"type":"error","code":"bad"}""").Kind);
        Assert.Equal(ProbeEventKind.Other, ProbeProtocol.ParseEvent("""{"type":"session.updated"}""").Kind);
    }
}
