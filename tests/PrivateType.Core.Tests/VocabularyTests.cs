using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class VocabularyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"vocabulary-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Offers_shared_plus_the_28_base_languages_as_scopes()
    {
        var scopes = VocabularyScopes.All;

        Assert.Equal(29, scopes.Count);
        Assert.Equal(("shared", "Shared across languages"), (scopes[0].Code, scopes[0].DisplayName));
        Assert.Contains(scopes, scope => scope is { Code: "en", DisplayName: "English" });
        Assert.Contains(scopes, scope => scope is { Code: "nb", DisplayName: "Norwegian Bokmål" });
        Assert.Contains(scopes, scope => scope is { Code: "zh", DisplayName: "Chinese" });
        Assert.False(VocabularyScopes.IsSupported("auto"));
        Assert.False(VocabularyScopes.IsSupported("el"));
    }

    [Fact]
    public void Normalizes_to_nfc_and_trims_but_keeps_case_and_inner_spacing()
    {
        var decomposed = " Zólw  API ";

        Assert.Equal("Zólw  API", VocabularyRules.Normalize(decomposed));
        Assert.Equal("MVVM", VocabularyRules.Normalize("  MVVM\t"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("tab\u0007bell")]
    public void Rejects_blank_and_control_character_phrases(string phrase)
    {
        Assert.NotNull(VocabularyRules.Validate([new VocabularyEntry(phrase, "shared")]));
    }

    [Fact]
    public void Enforces_the_phrase_length_count_and_payload_limits_without_quoting_phrases()
    {
        var secret = new string('s', 121);
        var tooLong = VocabularyRules.Validate([new VocabularyEntry(secret, "shared")]);
        Assert.NotNull(tooLong);
        Assert.DoesNotContain(secret, tooLong);
        Assert.Null(VocabularyRules.Validate([new VocabularyEntry(new string('s', 120), "shared")]));

        var twoHundred = Enumerable.Range(0, 200).Select(index => new VocabularyEntry($"term {index}", "shared")).ToList();
        Assert.Null(VocabularyRules.Validate(twoHundred));
        Assert.NotNull(VocabularyRules.Validate([.. twoHundred, new VocabularyEntry("one more", "shared")]));

        var heavy = Enumerable.Range(0, 140).Select(index => new VocabularyEntry($"{index:D3}{new string('x', 117)}", "shared")).ToList();
        Assert.NotNull(VocabularyRules.Validate(heavy));
    }

    [Fact]
    public void Rejects_exact_duplicates_in_one_scope_but_allows_case_variants_and_other_scopes()
    {
        Assert.NotNull(VocabularyRules.Validate([new("MVVM", "shared"), new(" MVVM ", "shared")]));
        Assert.Null(VocabularyRules.Validate([new("MVVM", "shared"), new("mvvm", "shared"), new("MVVM", "en")]));
    }

    [Fact]
    public void Rejects_unknown_scopes()
    {
        Assert.NotNull(VocabularyRules.Validate([new VocabularyEntry("term", "xx")]));
    }

    [Fact]
    public void Composes_shared_plus_the_matching_base_language_for_explicit_locales()
    {
        IReadOnlyList<VocabularyEntry> entries =
        [
            new("Zeta", "shared"), new("Alpha", "en"), new("Beta", "pl"), new("Zeta", "en"), new("Gamma", "es")
        ];

        Assert.Equal(["Alpha", "Zeta"], VocabularyComposer.Compose(entries, "en-GB"));
        Assert.Equal(["Alpha", "Zeta"], VocabularyComposer.Compose(entries, "en-US"));
        Assert.Equal(["Beta", "Zeta"], VocabularyComposer.Compose(entries, "pl-PL"));
        Assert.Equal(["Gamma", "Zeta"], VocabularyComposer.Compose(entries, "es-US"));
    }

    [Fact]
    public void Composes_only_shared_terms_for_automatic_detection()
    {
        Assert.Equal(["Zeta"], VocabularyComposer.Compose([new("Zeta", "shared"), new("Alpha", "en")], "auto"));
        Assert.Empty(VocabularyComposer.Compose([], "pl-PL"));
    }

    [Fact]
    public void Maps_strengths_to_the_calibrated_boosts()
    {
        Assert.Equal(0.5, VocabularyStrengths.Boost("low"));
        Assert.Equal(1.0, VocabularyStrengths.Boost("normal"));
        Assert.Equal(2.0, VocabularyStrengths.Boost("strong"));
        Assert.Throws<ArgumentOutOfRangeException>(() => VocabularyStrengths.Boost("max"));
    }

    [Fact]
    public void Round_trips_vocabulary_and_strength_and_defaults_to_empty_normal()
    {
        Assert.Empty(PortableSettings.Default.Vocabulary);
        Assert.Equal("normal", PortableSettings.Default.VocabularyStrength);

        var store = new PortableSettingsStore(directory);
        var settings = PortableSettings.Default with
        {
            Vocabulary = [new("PrivateType", "shared"), new("Żółw", "pl")],
            VocabularyStrength = "strong"
        };
        store.Save(settings);

        var loaded = store.Load();
        Assert.Null(loaded.Warning);
        Assert.Equal(settings.Vocabulary, loaded.Settings.Vocabulary);
        Assert.Equal("strong", loaded.Settings.VocabularyStrength);
    }

    [Fact]
    public void Legacy_settings_load_with_an_empty_vocabulary()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), """{ "MicrophoneId": "default", "Shortcuts": [{ "Language": 0, "VirtualKey": 82 }] }""");

        var result = new PortableSettingsStore(directory).Load();

        Assert.Null(result.Warning);
        Assert.Empty(result.Settings.Vocabulary);
    }

    [Fact]
    public void Drops_only_invalid_entries_when_repairing_a_damaged_file()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), """
            { "SchemaVersion": 2, "MicrophoneId": "default", "Shortcuts": [{ "LocaleCode": "pl-PL", "VirtualKey": 82 }],
              "Vocabulary": [{ "Phrase": "Keep", "Scope": "shared" }, { "Phrase": "", "Scope": "shared" }, { "Phrase": "Keep", "Scope": "shared" }, { "Phrase": "Other", "Scope": "xx" }],
              "VocabularyStrength": "loud" }
            """);

        var result = new PortableSettingsStore(directory).Load();

        Assert.Equal([new VocabularyEntry("Keep", "shared")], result.Settings.Vocabulary);
        Assert.Equal("normal", result.Settings.VocabularyStrength);
        Assert.Equal("Some saved settings were invalid and have been reset: vocabulary, vocabulary strength.", result.Warning);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
