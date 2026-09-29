using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

// Synthetic sentences only; never real dictated content.
public sealed class QuickTeachTests
{
    [Fact]
    public void Buffer_holds_one_result_and_reports_changes()
    {
        var buffer = new EphemeralTranscriptBuffer();
        var changes = 0;
        buffer.Changed += () => changes++;

        Assert.False(buffer.HasValue);
        buffer.Replace(new FinalizedDictation("first sentence", "en-US"));
        buffer.Replace(new FinalizedDictation("second sentence", "pl-PL"));

        Assert.True(buffer.HasValue);
        Assert.Equal("second sentence", buffer.Current!.Text);
        buffer.Clear();
        buffer.Clear();
        Assert.False(buffer.HasValue);
        Assert.Null(buffer.Current);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Buffer_ignores_blank_results()
    {
        var buffer = new EphemeralTranscriptBuffer();

        buffer.Replace(new FinalizedDictation("   ", "en-US"));

        Assert.False(buffer.HasValue);
    }

    [Fact]
    public void Splits_words_on_whitespace_keeping_punctuation_attached()
    {
        var text = "The app uses model, view  model.";

        var spans = WordSpans.Split(text);

        Assert.Equal(["The", "app", "uses", "model,", "view", "model."], spans.Select(span => text.Substring(span.Start, span.Length)));
        Assert.Empty(WordSpans.Split("  \t "));
        Assert.Equal(["Zażółć", "gęślą"], WordSpans.Split("Zażółć gęślą").Select(span => "Zażółć gęślą".Substring(span.Start, span.Length)));
    }

    [Fact]
    public void Selects_one_word_then_a_contiguous_range_in_either_direction()
    {
        var selection = new WordRangeSelection();

        selection.Activate(3);
        Assert.Equal((3, 3), selection.Range);
        selection.Activate(5);
        Assert.Equal((3, 5), selection.Range);

        selection.Activate(4);
        Assert.Equal((4, 4), selection.Range);
        selection.Activate(1);
        Assert.Equal((1, 4), selection.Range);

        selection.Clear();
        Assert.Null(selection.Range);
    }

    [Fact]
    public void Keyboard_extension_grows_the_range_from_the_anchor()
    {
        var selection = new WordRangeSelection();

        selection.ExtendTo(2);
        Assert.Equal((2, 2), selection.Range);
        selection.Activate(2);
        selection.ExtendTo(3);
        selection.ExtendTo(4);
        Assert.Equal((2, 4), selection.Range);
        selection.ExtendTo(1);
        Assert.Equal((1, 2), selection.Range);
    }

    [Fact]
    public void Selected_text_preserves_the_original_substring_between_words()
    {
        var text = "uses model,  view model. now";
        var spans = WordSpans.Split(text);
        var selection = new WordRangeSelection();

        selection.Activate(1);
        selection.Activate(3);

        Assert.Equal("model,  view model.", selection.SelectedText(text, spans));
    }

    [Fact]
    public void Fix_list_keeps_non_overlapping_fixes_in_sentence_order()
    {
        var fixes = new TeachFixList();
        var mvvm = new TeachFix(3, 5, new VocabularyEntry("MVVM", "en"));
        var app = new TeachFix(1, 1, new VocabularyEntry("App", VocabularyScopes.Shared));

        Assert.True(fixes.TryAdd(mvvm));
        Assert.True(fixes.TryAdd(app));
        Assert.False(fixes.TryAdd(new TeachFix(5, 6, new VocabularyEntry("other", "en"))));
        Assert.False(fixes.TryAdd(new TeachFix(0, 7, new VocabularyEntry("other", "en"))));

        Assert.Equal([app, mvvm], fixes.Fixes);
        Assert.True(fixes.Covers(4));
        Assert.False(fixes.Covers(2));
        Assert.True(fixes.Overlaps(2, 3));
        Assert.False(fixes.Overlaps(6, 8));

        fixes.Remove(mvvm);
        Assert.False(fixes.Covers(4));
        Assert.True(fixes.TryAdd(new TeachFix(4, 6, new VocabularyEntry("other", "en"))));
    }

    [Fact]
    public void Fix_list_saves_a_repeated_phrase_once()
    {
        var fixes = new TeachFixList();
        fixes.TryAdd(new TeachFix(0, 0, new VocabularyEntry("MVVM", "en")));
        fixes.TryAdd(new TeachFix(4, 4, new VocabularyEntry("MVVM", "en")));
        fixes.TryAdd(new TeachFix(6, 6, new VocabularyEntry("MVVM", "pl")));

        Assert.Equal([new VocabularyEntry("MVVM", "en"), new VocabularyEntry("MVVM", "pl")], fixes.Entries);
    }

    [Fact]
    public void Merge_appends_only_new_phrases_after_the_existing_ones()
    {
        IReadOnlyList<VocabularyEntry> existing = [new("Kubernetes", VocabularyScopes.Shared), new("MVVM", "en")];

        var merged = TaughtVocabulary.Merge(existing, [new("MVVM", "en"), new("WPF", "en"), new("WPF", "en"), new("MVVM", "pl")]);

        Assert.Equal([.. existing, new("WPF", "en"), new("MVVM", "pl")], merged);
    }

    [Fact]
    public async Task Session_reports_the_recognized_text_even_when_insertion_is_skipped()
    {
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("synthetic sentence", true, "commit-1"));
        var results = new List<FinalizedDictation>();
        await using var session = new DictationSession(new FakeCapture(), recognizer, new ForegroundTargetGuard(new FakeForegroundTarget(TargetEligibility.Changed)),
            new FakeInjector(), RecognitionRequest.WithoutVocabulary("en-GB"), TimeSpan.FromSeconds(1));
        session.Finalized += results.Add;

        await session.StartAsync();
        await session.StopAsync();

        Assert.Equal([new FinalizedDictation("synthetic sentence", "en-GB")], results);
    }

    [Fact]
    public async Task Session_reports_nothing_for_an_empty_result()
    {
        var results = new List<FinalizedDictation>();
        await using var session = new DictationSession(new FakeCapture(), new FakeRecognizer(), new ForegroundTargetGuard(new FakeForegroundTarget()),
            new FakeInjector(), RecognitionRequest.WithoutVocabulary("en-GB"), TimeSpan.FromSeconds(1));
        session.Finalized += results.Add;

        await session.StartAsync();
        await session.StopAsync();

        Assert.Empty(results);
    }
}
