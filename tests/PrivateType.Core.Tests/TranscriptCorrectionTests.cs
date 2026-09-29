using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

// Synthetic sentences only; never real dictated content.
public sealed class TranscriptCorrectionTests
{
    private static TranscriptCorrector Corrector(string locale, IReadOnlyList<VocabularyEntry>? entries = null,
        IReadOnlyList<VocabularyCorrection>? corrections = null, IReadOnlyList<VocabularyPack>? packs = null) =>
        TranscriptCorrector.Create(entries ?? [], packs ?? [], corrections ?? [], locale);

    [Theory]
    [InlineData("I like three d printing a lot.", "I like 3D printing a lot.")]
    [InlineData("Three-D printing is fun", "3D printing is fun")]
    [InlineData("about 3 D printing", "about 3D printing")]
    [InlineData("about 3d printing", "about 3D printing")]
    [InlineData("lubię trzy de printing", "lubię 3D printing")]
    [InlineData("lubię trzy D printing", "lubię 3D printing")]
    public void Spoken_forms_of_a_phrase_with_digits_and_letters_become_the_phrase(string heard, string expected)
    {
        var corrector = Corrector("en-US", [new("3D printing", VocabularyScopes.Shared)]);

        Assert.Equal(expected, corrector.Correct(heard));
    }

    [Theory]
    [InlineData("open fusion three sixty now", "open Fusion 360 now")]
    [InlineData("open Fusion three hundred sixty.", "open Fusion 360.")]
    [InlineData("open fusion three hundred and sixty", "open Fusion 360")]
    [InlineData("open fusion three six zero", "open Fusion 360")]
    [InlineData("otwórz fusion trzysta sześćdziesiąt", "otwórz Fusion 360")]
    public void Numbers_are_matched_in_every_common_reading(string heard, string expected)
    {
        var corrector = Corrector("pl-PL", [new("Fusion 360", "en")]);

        Assert.Equal(expected, Corrector("en-US", [new("Fusion 360", VocabularyScopes.Shared)]).Correct(heard));
        Assert.Equal(expected, Corrector("pl-PL", [new("Fusion 360", "pl")]).Correct(heard));
        Assert.Equal(heard, corrector.Correct(heard));
    }

    [Theory]
    [InlineData("three. D printing")]
    [InlineData("three, d printing")]
    [InlineData("threefold d printing")]
    [InlineData("I have three")]
    public void Matches_never_cross_punctuation_or_split_words(string heard)
    {
        var corrector = Corrector("en-US", [new("3D printing", VocabularyScopes.Shared), new("3", VocabularyScopes.Shared)]);

        Assert.Equal(heard, corrector.Correct(heard));
    }

    [Fact]
    public void Capitalization_is_fixed_only_for_multi_word_or_mixed_case_phrases()
    {
        var corrector = Corrector("en-US", [new("Visual Studio", "en"), new("PostgreSQL", "en"), new("Kubernetes", "en")]);

        Assert.Equal("open Visual Studio with PostgreSQL on kubernetes", corrector.Correct("open visual studio with postgresql on kubernetes"));
    }

    [Fact]
    public void Acronyms_heard_letter_by_letter_are_joined()
    {
        var corrector = Corrector("en-US", [new("REST API", VocabularyScopes.Shared)]);

        Assert.Equal("call the REST API.", corrector.Correct("call the rest a p i."));
    }

    [Fact]
    public void Taught_wordings_are_rewritten_and_win_over_generated_forms()
    {
        var corrector = Corrector("en-US",
            [new("3D printing", VocabularyScopes.Shared)],
            [new("free the printing", "3D printing", "en"), new("three d printing", "3-D printing", VocabularyScopes.Shared)]);

        Assert.Equal("I love 3D printing and 3-D printing", corrector.Correct("I love free the printing and three d printing"));
    }

    [Fact]
    public void Only_phrases_and_corrections_for_the_dictation_language_apply()
    {
        IReadOnlyList<VocabularyCorrection> corrections = [new("free the", "3D", "pl"), new("sea sharp", "C#", VocabularyScopes.Shared)];

        Assert.Equal("free the C#", Corrector("en-US", corrections: corrections).Correct("free the sea sharp"));
        Assert.Equal("3D C#", Corrector("pl-PL", corrections: corrections).Correct("free the sea sharp"));
        Assert.Equal("free the C#", Corrector(RecognitionLocaleCatalog.Automatic, corrections: corrections).Correct("free the sea sharp"));
    }

    [Fact]
    public void Enabled_packs_contribute_and_disabled_packs_do_not()
    {
        var corrector = Corrector("en-US", packs:
        [
            new VocabularyPack("On", VocabularyScopes.Shared, true, ["Fusion 360"]),
            new VocabularyPack("Off", VocabularyScopes.Shared, false, ["3D printing"])
        ]);

        Assert.Equal("Fusion 360 and three d printing", corrector.Correct("fusion three sixty and three d printing"));
    }

    [Fact]
    public void An_empty_corrector_returns_the_text_unchanged()
    {
        Assert.Equal("anything at all", TranscriptCorrector.Empty.Correct("anything at all"));
        Assert.Equal(0, TranscriptCorrector.Empty.RuleCount);
    }

    [Theory]
    [InlineData("2024", "en", "twenty twenty four")]
    [InlineData("2024", "en", "two thousand twenty four")]
    [InlineData("305", "en", "three oh five")]
    [InlineData("1900", "en", "nineteen hundred")]
    [InlineData("007", "en", "zero zero seven")]
    [InlineData("2024", "pl", "dwa tysiące dwadzieścia cztery")]
    [InlineData("1000", "pl", "tysiąc")]
    [InlineData("5015", "pl", "pięć tysięcy piętnaście")]
    [InlineData("200", "pl", "dwieście")]
    public void Numbers_are_read_aloud(string digits, string language, string reading)
    {
        Assert.Contains(reading, SpokenForms.NumberReadings(digits, language));
    }

    [Fact]
    public void Validation_rejects_duplicate_wordings_regardless_of_case_and_punctuation()
    {
        Assert.Null(VocabularyCorrectionRules.Validate([new("free the", "3D", "en"), new("free the", "3D", "pl")]));
        var error = VocabularyCorrectionRules.Validate([new("free the", "3D", "en"), new("Free, the!", "3-D", "en")]);
        Assert.NotNull(error);
        Assert.DoesNotContain("free", error, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(VocabularyCorrectionRules.Validate([new("?!", "3D", "en")]));
        Assert.NotNull(VocabularyCorrectionRules.Validate([new("free the", "3D", "xx")]));
        Assert.NotNull(VocabularyCorrectionRules.Validate([new("free the", " ", "en")]));
        Assert.NotNull(VocabularyCorrectionRules.Validate(null));
    }

    [Fact]
    public void Repair_keeps_valid_corrections_and_merge_replaces_the_same_wording()
    {
        var kept = VocabularyCorrectionRules.KeepValid([new(" free the ", "3D", "en"), new("?!", "x", "en"), new("Free the", "3-D", "en")]);
        Assert.Equal([new VocabularyCorrection("free the", "3D", "en")], kept);

        var merged = VocabularyCorrectionRules.Merge([new("free the", "3D", "en"), new("sea sharp", "C#", "en")], [new("FREE THE", "3-D", "en")]);
        Assert.Equal([new VocabularyCorrection("sea sharp", "C#", "en"), new VocabularyCorrection("FREE THE", "3-D", "en")], merged);
    }

    [Fact]
    public void Settings_default_to_correcting_with_no_taught_wordings()
    {
        Assert.True(PortableSettings.Default.CorrectAfterDictation);
        Assert.Empty(PortableSettings.Default.VocabularyCorrections);
        Assert.NotNull(PortableSettingsValidator.Validate(PortableSettings.Default with { VocabularyCorrections = [new("a", "b", "xx")] }));
    }

    [Fact]
    public void A_teach_fix_remembers_the_heard_wording_only_when_it_differs()
    {
        Assert.Equal(new VocabularyCorrection("free the printing", "3D printing", "en"),
            new TeachFix(0, 2, new VocabularyEntry("3D printing", "en"), "free the printing").Correction);
        Assert.Null(new TeachFix(0, 1, new VocabularyEntry("Visual Studio", "en"), "visual studio,").Correction);
        Assert.Null(new TeachFix(0, 0, new VocabularyEntry("MVVM", "en")).Correction);

        var fixes = new TeachFixList();
        fixes.TryAdd(new TeachFix(0, 2, new VocabularyEntry("3D printing", "en"), "free the printing"));
        fixes.TryAdd(new TeachFix(4, 4, new VocabularyEntry("MVVM", "en"), "MVVM"));
        var taught = fixes.ToTaughtTerms();
        Assert.Equal(2, taught.Entries.Count);
        Assert.Equal([new VocabularyCorrection("free the printing", "3D printing", "en")], taught.Corrections);
    }

    [Fact]
    public async Task Session_inserts_and_reports_the_corrected_text()
    {
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("three d printing", true, "commit-1"));
        var injector = new FakeInjector();
        var results = new List<FinalizedDictation>();
        await using var session = new DictationSession(new FakeCapture(), recognizer, new ForegroundTargetGuard(new FakeForegroundTarget()),
            injector, RecognitionRequest.WithoutVocabulary("en-US"), TimeSpan.FromSeconds(1),
            correctText: Corrector("en-US", [new("3D printing", VocabularyScopes.Shared)]).Correct);
        session.Finalized += results.Add;

        await session.StartAsync();
        await session.StopAsync();

        Assert.Equal(["3D printing"], injector.Texts);
        Assert.Equal("3D printing", results.Single().Text);
    }

    [Fact]
    public async Task Session_keeps_the_recognized_text_when_correction_fails()
    {
        var recognizer = new FakeRecognizer();
        recognizer.CompletionUpdates.Add(new TranscriptUpdate("synthetic sentence", true, "commit-1"));
        var injector = new FakeInjector();
        await using var session = new DictationSession(new FakeCapture(), recognizer, new ForegroundTargetGuard(new FakeForegroundTarget()),
            injector, RecognitionRequest.WithoutVocabulary("en-US"), TimeSpan.FromSeconds(1),
            correctText: _ => throw new InvalidOperationException("boom"));

        await session.StartAsync();
        await session.StopAsync();

        Assert.Equal(["synthetic sentence"], injector.Texts);
        Assert.Equal(DictationState.Ready, session.State);
    }
}
