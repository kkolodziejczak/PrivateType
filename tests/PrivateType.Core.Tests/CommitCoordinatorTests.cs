using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class CommitCoordinatorTests
{
    [Fact]
    public void Replaces_the_provisional_snapshot_when_the_recognizer_revises_it()
    {
        var coordinator = new CommitCoordinator();

        coordinator.Apply(new TranscriptUpdate("pierwsza wersja", false));
        coordinator.Apply(new TranscriptUpdate("ostateczna wersja", false));

        Assert.Equal("ostateczna wersja", coordinator.ProvisionalText);
    }

    [Fact]
    public void Preserves_non_overlapping_committed_text_exactly()
    {
        var coordinator = new CommitCoordinator();

        coordinator.Apply(new TranscriptUpdate("abcdef", true, "commit-1"));
        coordinator.Apply(new TranscriptUpdate("XYZ", true, "commit-2"));

        Assert.Equal("abcdefXYZ", coordinator.TakeFinalText());
    }

    [Fact]
    public void Final_text_can_be_taken_only_once()
    {
        var coordinator = new CommitCoordinator();
        coordinator.Apply(new TranscriptUpdate("gotowy tekst", true, "commit-1"));

        Assert.Equal("gotowy tekst", coordinator.TakeFinalText());
        Assert.Equal(string.Empty, coordinator.TakeFinalText());
    }

    [Fact]
    public void Applies_declared_boundary_overlap_without_rewriting_whitespace()
    {
        var coordinator = new CommitCoordinator();

        coordinator.Apply(new TranscriptUpdate("Ala ma ", true, "commit-1"));
        coordinator.Apply(new TranscriptUpdate("ma kota", true, "commit-2", BoundaryOverlap: 3));
        coordinator.Apply(new TranscriptUpdate(string.Empty, true, "commit-3"));

        Assert.Equal("Ala ma kota", coordinator.TakeFinalText());
    }

    [Fact]
    public void Preserves_repeated_speech_from_distinct_committed_segments()
    {
        var coordinator = new CommitCoordinator();

        coordinator.Apply(new TranscriptUpdate("nie ", true, "commit-1"));
        coordinator.Apply(new TranscriptUpdate("nie ", true, "commit-2"));

        Assert.Equal("nie nie ", coordinator.TakeFinalText());
    }

    [Fact]
    public void Ignores_a_replayed_committed_segment_with_the_same_identity()
    {
        var coordinator = new CommitCoordinator();

        coordinator.Apply(new TranscriptUpdate("nie ", true, "commit-1"));
        coordinator.Apply(new TranscriptUpdate("nie ", true, "commit-1"));

        Assert.Equal("nie ", coordinator.TakeFinalText());
    }
}
